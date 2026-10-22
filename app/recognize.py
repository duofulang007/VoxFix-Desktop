"""Local SenseVoiceSmall: bounded VAD segments, revisable drafts, append-only finals."""
import base64
from collections import deque
import json
import os
from pathlib import Path
import re
import sys
import threading

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
SAMPLE_RATE = 16000
VAD_FRAME = SAMPLE_RATE // 10  # Stable gain and endpoints even when capture packets are batched.
MAX_SECONDS = 180
PREVIEW_STEP = 5600  # 350 ms; replace obsolete drafts instead of queueing them.
MIN_PREVIEW = 8800
BOUNDARY_PAD = 6400
_RANGES = ((0x0020, 0x007E), (0x2000, 0x206F), (0x3000, 0x303F),
           (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xFE30, 0xFE4F), (0xFF00, 0xFFEF))
_SYMBOLS = frozenset('£¥©®°±µ·×÷€')


def outside_product_scripts(text):
    return any(not char.isspace() and char not in _SYMBOLS
               and not any(low <= ord(char) <= high for low, high in _RANGES)
               for char in text)


def join_text(previous, text):
    if previous and text and previous[-1].isascii() and previous[-1].isalnum() \
            and text[0].isascii() and text[0].isalnum():
        return previous + ' ' + text
    return previous + text


def punctuation_only(original, result):
    tokens = r"[A-Za-z0-9_]+(?:[.'/:+-][A-Za-z0-9_]+)*|[^\s，。！？；：、,.!?;:]"
    if re.findall(tokens, original) != re.findall(tokens, result):
        return False
    # Existing punctuation also carries meaning (for example the dot in .NET).
    remaining = iter(result)
    return all(char in remaining for char in original if not char.isspace())


class QuietBoost:
    def __init__(self):
        self.level = 0.0

    def process(self, audio):
        if not audio.size:
            return audio
        self.level = max(float(np.sqrt(np.mean(audio ** 2))), self.level * .96)
        peak = float(np.max(np.abs(audio)))
        if not peak:
            return audio.copy()
        gain = min(32.0, max(1.0, .025 / max(self.level, 1e-8)), .95 / peak)
        return audio * gain


def create_vad():
    import sherpa_onnx as sherpa
    config = sherpa.VadModelConfig()
    config.silero_vad.model = str(ROOT / 'models/vad/silero_vad.onnx')
    config.silero_vad.threshold = .30
    config.silero_vad.min_speech_duration = .12
    config.silero_vad.min_silence_duration = .65
    config.silero_vad.max_speech_duration = 10
    config.sample_rate = SAMPLE_RATE
    config.num_threads = 1
    return sherpa.VoiceActivityDetector(config, buffer_size_in_seconds=30)


class SenseVoiceSession:
    def __init__(self):
        import sherpa_onnx as sherpa
        self.recognizer = sherpa.OfflineRecognizer.from_sense_voice(
            model=str(ROOT / 'models/sensevoice/model.int8.onnx'),
            tokens=str(ROOT / 'models/sensevoice/tokens.txt'),
            # ITN changed "一百二十八元五角" into "128元5元角" in the fixed corpus.
            language='auto', use_itn=False, num_threads=4, provider='cpu')
        config = sherpa.OfflinePunctuationConfig()
        config.model.ct_transformer = str(ROOT / 'models/punctuation/model.int8.onnx')
        config.model.num_threads = 2
        self.punctuation = sherpa.OfflinePunctuation(config)

    def run(self, audio):
        stream = self.recognizer.create_stream()
        stream.accept_waveform(SAMPLE_RATE, audio)
        self.recognizer.decode_stream(stream)
        return stream.result

    def punctuate(self, text):
        if not text:
            return text
        try:
            result = self.punctuation.add_punctuation(text)
        except Exception:
            print('SenseVoice: punctuation failed; returning original transcript', file=sys.stderr, flush=True)
            return text
        # Punctuation must never rewrite words, numbers, names or negation.
        if not punctuation_only(text, result):
            return text
        return result


class Recognition:
    def __init__(self, session, vad=None):
        self.session = session
        self.vad = vad if vad is not None else create_vad()
        self.condition = threading.Condition()
        self.worker = None
        self.clear()

    def clear(self):
        with self.condition:
            self.cancelled = True
            self.condition.notify_all()
        if self.worker is not None:
            self.worker.join()
            self.worker = None
        with self.condition:
            self.id = None
            self.samples = self.last_end = self.last_preview = 0
            self.route = self.vad_offset = 0
            self.segments = []
            self.carry = False
            self.level = 0.0
            self.audio = np.zeros(SAMPLE_RATE * MAX_SECONDS, dtype=np.float32)
            self.boost = QuietBoost()
            self.vad_pending = np.zeros(0, dtype=np.float32)
            self.pending = deque()
            self.draft = None
            self.active_key = None
            self.preview_key = None
            self.committed = self.preview = self.error = ''
            self.ending = False
            self.decoding = False
            self.vad.reset()

    def snapshot(self, event='partial'):
        with self.condition:
            if self.error:
                raise RuntimeError(self.error)
            return {'event': event, 'id': self.id, 'text': self.committed,
                    'preview': self.preview, 'samples': self.samples, 'level': self.level,
                    'preview_segment': ({'start': self.preview_key[0], 'route': self.preview_key[1],
                                         'text': self.preview} if self.preview and self.preview_key else None),
                    'speaking': bool(self.vad.is_speech_detected()),
                    'transcribing': bool(self.vad.is_speech_detected() or self.pending or self.draft is not None or self.decoding),
                    'segments': list(self.segments)}

    def change_route(self, route):
        if isinstance(route, bool) or not isinstance(route, int) or not 0 <= route <= 100000:
            raise ValueError('无效的输入阶段')
        if route == self.route:
            return
        self.flush_audio()
        self.vad.flush()
        self.collect_segments(flushing=True)
        self.vad.reset()
        self.vad_offset = self.last_end = self.samples
        self.carry = False
        self.draft = self.active_key = None
        self.preview = ''
        self.route = route

    def handle(self, request):
        operation, identity = request.get('op'), request.get('id')
        if not isinstance(identity, str) or not 0 < len(identity) <= 128:
            raise ValueError('无效的录音编号')
        if operation == 'start':
            if self.id is not None:
                raise ValueError('上一段录音尚未结束')
            self.clear()
            self.id, self.cancelled = identity, False
            self.worker = threading.Thread(target=self.loop, daemon=True)
            self.worker.start()
            return {'event': 'started', 'id': identity}
        if self.id is None or identity != self.id:
            raise ValueError('录音已结束或编号不匹配')
        if operation == 'cancel':
            self.clear()
            return {'event': 'cancelled', 'id': identity}
        if operation == 'poll':
            return self.snapshot()
        if operation == 'route':
            with self.condition:
                self.change_route(request.get('route'))
                self.condition.notify_all()
            return self.snapshot('routed')
        if operation == 'audio':
            encoded = request.get('pcm')
            if not isinstance(encoded, str) or len(encoded) > 43000:
                raise ValueError('无效的音频数据')
            raw = base64.b64decode(encoded, validate=True)
            if not raw or len(raw) % 2 or len(raw) > 32000:
                raise ValueError('需要不超过一秒的 PCM16 单声道音频')
            audio = np.frombuffer(raw, dtype='<i2').astype(np.float32) / 32768
            with self.condition:
                self.change_route(request.get('route', self.route))
                if self.samples + self.vad_pending.size + audio.size > SAMPLE_RATE * MAX_SECONDS:
                    raise ValueError('每段录音最长三分钟')
                self.vad_pending = np.concatenate((self.vad_pending, audio))
                while self.vad_pending.size >= VAD_FRAME:
                    self.accept_frame(self.vad_pending[:VAD_FRAME])
                    self.vad_pending = self.vad_pending[VAD_FRAME:]
                self.condition.notify_all()
            return self.snapshot()
        if operation == 'stop':
            with self.condition:
                self.flush_audio()
                self.vad.flush()
                self.collect_segments(flushing=True)
                self.ending = True
                self.draft = None
                self.condition.notify_all()
            self.worker.join()
            self.worker = None
            result = self.snapshot('final')
            self.clear()
            return result
        raise ValueError('未知的识别请求')

    def accept_frame(self, audio):
        boosted = self.boost.process(audio)
        self.level = min(1.0, float(np.sqrt(np.mean(boosted ** 2))) * 12)
        # Keep the waveform intact. VAD may use adaptive gain, but ASR
        # gets one uniform gain per segment, preserving quiet consonants.
        self.audio[self.samples:self.samples + audio.size] = audio
        self.samples += audio.size
        self.vad.accept_waveform(boosted)
        self.collect_segments()
        if self.vad.is_speech_detected():
            current = self.vad.current_segment
            start = self.last_end if self.carry else max(self.last_end, self.vad_offset + current.start - BOUNDARY_PAD)
            self.active_key = (start, self.route)
            if self.samples - start >= MIN_PREVIEW and self.samples - self.last_preview >= PREVIEW_STEP:
                self.draft = (self.active_key, self.audio[start:self.samples].copy())
                self.last_preview = self.samples

    def flush_audio(self):
        if self.vad_pending.size:
            self.accept_frame(self.vad_pending)
            self.vad_pending = np.zeros(0, dtype=np.float32)

    def collect_segments(self, flushing=False):
        while not self.vad.empty():
            segment = self.vad.front
            position = self.vad_offset + segment.start
            start = self.last_end if self.carry else max(self.last_end, position - BOUNDARY_PAD)
            if flushing and self.last_end == 0 and self.samples <= 6 * SAMPLE_RATE:
                start = 0
            end = min(self.samples, position + len(segment.samples) + BOUNDARY_PAD)
            forced = len(segment.samples) >= int(9.5 * SAMPLE_RATE) and not flushing
            if forced:
                # A duration limit is not a word boundary; cut at nearby quiet audio
                # and carry the remaining samples into the next segment.
                target = min(self.samples, position + len(segment.samples))
                search = max(start + SAMPLE_RATE, target - 24000)
                window = self.audio[search:target]
                count = len(window) // 160
                if count:
                    energy = np.mean(window[:count * 160].reshape(count, 160) ** 2, axis=1)
                    end = search + int(np.argmin(energy)) * 160 + 80
            if flushing:
                end = self.samples
            self.queue_segment(start, end, forced)
            self.vad.pop()
        # A forced VAD endpoint can leave carry audio after its completed queue is empty.
        if flushing and self.carry and self.last_end < self.samples:
            self.queue_segment(self.last_end, self.samples, False)

    def queue_segment(self, start, end, forced):
        if end > start:
            if len(self.pending) >= 24:
                raise RuntimeError('识别队列积压，请结束本次录音后重试')
            preview_start = self.active_key[0] if self.active_key is not None else start
            self.pending.append((self.route, start, end, preview_start, self.audio[start:end].copy()))
            self.last_end = end
            self.carry = forced
        self.draft = None
        self.active_key = None
        self.preview = ''

    def loop(self):
        try:
            while True:
                with self.condition:
                    self.condition.wait_for(lambda: self.cancelled or self.ending or self.pending or self.draft is not None)
                    if self.cancelled:
                        return
                    if self.pending:
                        final, key = True, None
                        route, start, end, preview_start, audio = self.pending.popleft()
                    elif self.ending:
                        return
                    else:
                        final = False
                        key, audio = self.draft
                        self.draft = None
                    self.decoding = True
                text = re.sub(r'<\|[^|]*\|>', '', self.session.run(QuietBoost().process(audio)).text).strip()
                if final and hasattr(self.session, 'punctuate'):
                    text = self.session.punctuate(text)
                with self.condition:
                    self.decoding = False
                    if outside_product_scripts(text):
                        if not final:
                            continue
                        raise RuntimeError('识别到中文和英文之外的文字，本次结果未自动输入')
                    if self.cancelled:
                        return
                    if final:
                        previous = self.committed
                        self.committed = join_text(previous, text)
                        self.segments.append({'route': route, 'start': start, 'end': end,
                                              'preview_start': preview_start,
                                              'text': self.committed[len(previous):]})
                    elif key == self.active_key and not self.ending:
                        self.preview = text
                        self.preview_key = key
        except Exception as error:
            with self.condition:
                self.decoding = False
                self.error = str(error)
                self.condition.notify_all()


def load_model():
    sys.path.insert(0, str(ROOT / 'runtime/sherpa-onnx-1.13.8'))
    return SenseVoiceSession(), 'cpu'


def main():
    protocol = os.fdopen(os.dup(sys.stdout.fileno()), 'w', encoding='utf-8', buffering=1)
    os.dup2(sys.stderr.fileno(), sys.stdout.fileno())

    def send(value):
        print(json.dumps(value, ensure_ascii=True), file=protocol, flush=True)

    recognition = None
    try:
        session, backend = load_model()
        recognition = Recognition(session)
        send({'ready': True, 'streaming': False, 'segmented': True,
              'preview': True, 'model': 'SenseVoiceSmall INT8', 'backend': backend})
        for line in sys.stdin:
            try:
                send(recognition.handle(json.loads(line)))
            except Exception as error:
                recovery, draft = recognition.committed, recognition.preview
                segments = list(recognition.segments)
                recognition.clear()
                send({'error': str(error), 'text': recovery, 'preview': draft,
                      'segments': segments})
    except Exception as error:
        send({'error': str(error)})
        return 1
    finally:
        if recognition is not None:
            recognition.clear()
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
