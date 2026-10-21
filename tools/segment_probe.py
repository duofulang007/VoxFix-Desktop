"""Segment-level streaming probe: energy VAD + offline model per segment.

Measures what a "one segment in, one segment out" delivery would look like:
segment boundaries, text per segment, decode cost and the audio position of the
first emitted characters. Audio is only held in memory.
"""
import argparse
import json
import os
from pathlib import Path
import sys
import wave

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
os.environ.setdefault('TRANSCRIBE_LIBRARY', str(
    ROOT / 'runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll'))


def read(path):
    with wave.open(str(path), 'rb') as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float32) / 32768


def frame_rms(audio, size=320):
    count = len(audio) // size
    return np.sqrt(np.mean(audio[:count * size].reshape(count, size) ** 2, axis=1) + 1e-12)


class Segmenter:
    """Cheap energy gate: speech starts above the noise floor, ends after a pause."""

    def __init__(self, silence_ms=320, min_ms=300, max_ms=12000, margin_ms=200):
        self.silence = silence_ms
        self.minimum = min_ms
        self.maximum = max_ms
        self.margin = margin_ms
        self.floor = 0.0015
        self.speaking = False
        self.start = 0
        self.last_voice = 0
        self.count = 0

    def push(self, frame, index):
        level = float(np.sqrt(np.mean(frame ** 2) + 1e-12))
        if not self.speaking:
            self.floor = min(0.02, max(0.0004, self.floor * 0.995 + level * 0.005))
        voiced = level > max(0.004, self.floor * 3.0)
        if voiced:
            self.last_voice = index
            if not self.speaking:
                self.speaking = True
                self.start = max(0, index - self.margin // 20)
        elif self.speaking:
            quiet = index - self.last_voice
            long_enough = (index - self.start) * 20 >= self.minimum
            if (quiet * 20 >= self.silence and long_enough) or (index - self.start) * 20 >= self.maximum:
                self.speaking = False
                return (self.start, index + self.margin // 20)
        return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', default='models/qwen3-asr-0.6b/Qwen3-ASR-0.6B-Q8_0.gguf')
    parser.add_argument('--backend', default='vulkan')
    parser.add_argument('--wav', action='append', default=[])
    parser.add_argument('--media', action='append', default=[])
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()

    import subprocess
    import transcribe_cpp as tc

    clips = [(Path(p).stem, read(p)) for p in args.wav]
    for path in args.media:
        ffmpeg = ROOT / '.tmp/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
        result = subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', path,
                                 '-vn', '-f', 's16le', '-ar', '16000', '-ac', '1', 'pipe:1'],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True)
        audio = np.frombuffer(result.stdout, dtype='<i2').astype(np.float32) / 32768
        clips.append((Path(path).stem, audio))

    report = []
    with tc.Model(ROOT / args.model, backend=args.backend) as model:
        with model.session(n_threads=4) as session:
            for name, audio in clips:
                segmenter = Segmenter()
                segments = []
                for index in range(len(audio) // 320):
                    frame = audio[index * 320:(index + 1) * 320]
                    found = segmenter.push(frame, index)
                    if found:
                        segments.append(found)
                if segmenter.speaking:
                    segments.append((segmenter.start, len(audio) // 320))
                rows = []
                first_text_audio = None
                for start, end in segments:
                    chunk = audio[start * 320:end * 320]
                    if len(chunk) < 1600:
                        continue
                    import time
                    tick = time.perf_counter()
                    text = session.run(chunk, timestamps='none').text.strip()
                    cost = (time.perf_counter() - tick) * 1000
                    if text and first_text_audio is None:
                        first_text_audio = round(end * 20, 1)
                    rows.append({'startMs': start * 20, 'endMs': end * 20,
                                 'audioMs': round(len(chunk) / 16, 1),
                                 'decodeMs': round(cost, 1), 'text': text})
                joined = ''.join(r['text'] for r in rows)
                report.append({'id': name, 'audioMs': round(len(audio) / 16, 1),
                               'segments': len(rows), 'firstTextAudioMs': first_text_audio,
                               'text': joined, 'detail': rows})
                print(f"{name}: {len(rows)} segs, first text at {first_text_audio} ms, "
                      f"decode {[r['decodeMs'] for r in rows][:6]}")
                for r in rows:
                    print(f"   {r['startMs']:7.0f}-{r['endMs']:7.0f} {r['decodeMs']:6.0f}ms {r['text'][:70]!r}")

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding='utf8')


if __name__ == '__main__':
    main()
