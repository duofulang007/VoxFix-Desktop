"""Probe the production streaming protocol with an explicit language setting.

Mirrors app/recognize.py (100 ms chunks, display text, prefix guard) but lets the
caller choose the language hint and reports the model's own detected language.

Usage:
  python tools/language_probe.py --language auto --ids word-13,sentence-7
  python tools/language_probe.py --language zh-CN --ids mix-huihui-4 --repeat 3
  python tools/language_probe.py --wav path/to.wav --language en-US
Results are written as JSON; stdout stays quiet.
"""
import argparse
import json
import os
from pathlib import Path
import sys
import time

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
os.environ.setdefault('TRANSCRIBE_LIBRARY', str(
    ROOT / 'runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll'))
sys.path.insert(0, str(ROOT / 'app'))

MODEL = ROOT / 'models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf'
CHUNK = 1600  # 100 ms at 16 kHz, same cadence as production


def read_wav(path):
    import wave
    with wave.open(str(path), 'rb') as wav:
        if wav.getframerate() != 16000 or wav.getnchannels() != 1 or wav.getsampwidth() != 2:
            raise ValueError(f'{path}: need 16 kHz mono PCM16, got {wav.getframerate()} Hz '
                             f'{wav.getnchannels()} ch {wav.getsampwidth() * 8} bit')
        return np.frombuffer(wav.readframes(wav.getnframes()), dtype='<i2').astype(np.float32) / 32768


def run_stream(session, audio, language, quiet_boost=True):
    """Same flow as app/recognize.py: feed, read display text, enforce prefix."""
    from recognize import QuietBoost
    boost = QuietBoost()
    lang = None if language in (None, '', 'auto') else language
    stream = session.stream(timestamps='none', language=lang)
    last = ''
    updates, regressions = [], []
    first_progress = first_wall = None
    clock = time.perf_counter()
    try:
        for offset in range(0, len(audio), CHUNK):
            piece = audio[offset:offset + CHUNK]
            if not len(piece):
                break
            stream.feed(boost.process(piece) if quiet_boost else piece)
            text = stream.text().display.lstrip()
            if first_progress is None and text:
                first_progress = round(offset / 16, 1)   # audio progress in ms
                first_wall = round((time.perf_counter() - clock) * 1000, 1)
            if not text.startswith(last):
                regressions.append({'atMs': round(offset / 16, 1), 'before': last, 'after': text})
            last = text
            updates.append({'atMs': round(offset / 16, 1), 'text': text})
        stream.feed(np.zeros(5120, dtype=np.float32))
        stream.finalize()
        final = stream.text().display.lstrip()
        snap = stream.snapshot()
        detected = getattr(snap, 'language', '') or ''
    finally:
        stream.reset()
    return {
        'languageHint': lang or 'auto',
        'detectedLanguage': detected,
        'firstTextAudioMs': first_progress,
        'firstTextWallMs': first_wall,
        'final': final,
        'finalChangedAfterFirst': bool(first_progress is not None and final[:len(last)] != last),
        'regressions': regressions[:5],
        'regressionCount': len(regressions),
        'lastLive': last,
        'updateCount': len(updates),
        'audioMs': round(len(audio) / 16, 1),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--manifest', type=Path, default=ROOT / 'work/p0/manifest.json')
    parser.add_argument('--ids', default='')
    parser.add_argument('--wav', action='append', default=[])
    parser.add_argument('--language', default='auto', help='auto | zh-CN | en-US | ...')
    parser.add_argument('--repeat', type=int, default=1, help='repeat each clip on the same session')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--backend', default='cpu')
    args = parser.parse_args()

    rows = []
    if args.ids:
        manifest = json.loads(args.manifest.read_text(encoding='utf8'))
        wanted = [x for x in args.ids.split(',') if x]
        for item in manifest:
            if item['id'] in wanted:
                rows.append({'id': item['id'], 'expected': item.get('expected', ''),
                             'wav': item['wav']})
    for path in args.wav:
        rows.append({'id': Path(path).stem, 'expected': '', 'wav': path})
    if not rows:
        raise SystemExit('no clips selected')

    import transcribe_cpp
    results = []
    with transcribe_cpp.Model(MODEL, backend=args.backend) as model:
        with model.session(n_threads=4) as session:
            for row in rows:
                audio = read_wav(row['wav'])
                for turn in range(args.repeat):
                    record = {'id': row['id'], 'expected': row['expected'], 'turn': turn + 1,
                              'seconds': round(len(audio) / 16000, 2)}
                    record.update(run_stream(session, audio, args.language))
                    results.append(record)
    payload = {'model': str(MODEL), 'backend': args.backend, 'results': results}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding='utf8')


if __name__ == '__main__':
    main()
