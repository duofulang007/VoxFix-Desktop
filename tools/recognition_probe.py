"""Drive the real app/recognize.py Recognition class over fixed audio.

Used for before/after comparison of the production protocol (100 ms chunks,
prefix guard, stop finalisation). The module path is selectable so the old and
new versions can be measured on identical audio.

Usage:
  python tools/recognition_probe.py --ids word-13,sentence-7 --out work/ab.json
  python tools/recognition_probe.py --ids word-13 --recognize work/x/old.py --pace
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import sys
import time
import wave

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
os.environ.setdefault('TRANSCRIBE_LIBRARY', str(
    ROOT / 'runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll'))
MODEL = ROOT / 'models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf'
CHUNK = 1600


def load_module(path):
    spec = importlib.util.spec_from_file_location('recognize_under_test', str(path))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_wav(path):
    with wave.open(str(path), 'rb') as wav:
        if wav.getframerate() != 16000 or wav.getnchannels() != 1 or wav.getsampwidth() != 2:
            raise ValueError(f'{path}: need 16 kHz mono PCM16')
        return np.frombuffer(wav.readframes(wav.getnframes()), dtype='<i2')


def read_media(path):
    """Decode a video/audio file to 16 kHz mono PCM16 in memory (no temp file)."""
    import subprocess
    ffmpeg = ROOT / '.tmp/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
    result = subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', str(path),
                             '-vn', '-f', 's16le', '-ar', '16000', '-ac', '1', 'pipe:1'],
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True)
    return np.frombuffer(result.stdout, dtype='<i2')


def run_clip(module, session, pcm16, pace):
    recognition = module.Recognition(session)
    identity = 'probe'
    started = time.perf_counter()
    first_wall = first_audio = None
    partials = []
    error = None
    try:
        recognition.handle({'op': 'start', 'id': identity})
        for offset in range(0, len(pcm16), CHUNK):
            piece = pcm16[offset:offset + CHUNK]
            reply = recognition.handle({'op': 'audio', 'id': identity,
                                        'pcm': __import__('base64').b64encode(piece.tobytes()).decode()})
            text = reply.get('text', '')
            if text and first_wall is None:
                first_wall = round((time.perf_counter() - started) * 1000, 1)
                first_audio = round(offset / 16, 1)
            partials.append({'atAudioMs': round(offset / 16, 1), 'text': text})
            if pace:
                target = started + (offset + CHUNK) / 16000
                delay = target - time.perf_counter()
                if delay > 0:
                    time.sleep(delay)
        final = recognition.handle({'op': 'stop', 'id': identity})
        result = {'text': final.get('text', '')}
        if first_wall is None and result['text']:
            first_wall = round((time.perf_counter() - started) * 1000, 1)
            first_audio = round(len(pcm16) / 16, 1)
    except Exception as exc:  # production raises on prefix regression
        error = str(exc)
        result = {'text': partials[-1]['text'] if partials else ''}
    finally:
        try:
            recognition.reset()
        except Exception:
            pass
    return {
        'error': error,
        'firstTextWallMs': first_wall,
        'firstTextAudioMs': first_audio,
        'liveFinal': result['text'],
        'wallMs': round((time.perf_counter() - started) * 1000, 1),
        'partials': partials[-3:],
        'partialCount': len(partials),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--recognize', type=Path, default=ROOT / 'app/recognize.py')
    parser.add_argument('--manifest', type=Path, default=ROOT / 'work/p0/manifest.json')
    parser.add_argument('--ids', default='')
    parser.add_argument('--all', action='store_true')
    parser.add_argument('--wav', action='append', default=[])
    parser.add_argument('--media', action='append', default=[], help='video/audio file, decoded in memory')
    parser.add_argument('--pace', action='store_true')
    parser.add_argument('--backend', default='cpu', choices=['cpu', 'vulkan'])
    parser.add_argument('--model', type=Path, default=MODEL)
    parser.add_argument('--att-right', type=int, default=None,
                        help='wrap the session with ParakeetStreamOptions(att_context_right=N)')
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()

    rows = []
    manifest = json.loads(args.manifest.read_text(encoding='utf8'))
    if args.all:
        rows = [{'id': m['id'], 'expected': m.get('expected', ''), 'wav': m['wav']} for m in manifest]
    elif args.ids:
        wanted = set(x for x in args.ids.split(',') if x)
        rows = [{'id': m['id'], 'expected': m.get('expected', ''), 'wav': m['wav']}
                for m in manifest if m['id'] in wanted]
    for path in args.wav:
        rows.append({'id': Path(path).stem, 'expected': '', 'wav': path})
    for path in args.media:
        rows.append({'id': Path(path).stem, 'expected': '', 'wav': path, 'media': True})
    if not rows:
        raise SystemExit('no clips selected')

    module = load_module(args.recognize.resolve())
    import transcribe_cpp as tc

    class FamilySession:
        """Same session, with a family stream option applied to every stream."""

        def __init__(self, inner, right):
            self.inner, self.right = inner, right

        def stream(self, **kwargs):
            return self.inner.stream(family=tc.ParakeetStreamOptions(att_context_right=self.right), **kwargs)

    results = []
    with tc.Model(args.model, backend=args.backend) as model:
        with model.session(n_threads=4) as raw:
            session = FamilySession(raw, args.att_right) if args.att_right is not None else raw
            for row in rows:
                record = {'id': row['id'], 'expected': row['expected'], 'wav': row['wav']}
                pcm = read_media(row['wav']) if row.get('media') else read_wav(row['wav'])
                record.update(run_clip(module, session, pcm, args.pace))
                results.append(record)
    payload = {'recognize': str(args.recognize), 'pace': args.pace,
               'attRight': args.att_right, 'backend': args.backend, 'results': results}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding='utf8')


if __name__ == '__main__':
    main()
