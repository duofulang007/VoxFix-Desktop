"""Score segment-level delivery: VAD boundaries with a hard maximum length.

Simulates the daily pipeline (100 ms chunks in, growing text out) for an offline
model: a segment is decoded as soon as the speaker pauses or the maximum length
is reached, and its text is appended. Reports exact matches per category plus
the audio position where the first characters appear.

Usage:
  python tools/segment_batch.py --max-ms 4000 --out work/x.json
"""
import argparse
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
sys.path.insert(0, str(ROOT / 'tools'))
from p0_score import units, LABELS  # noqa: E402
from segment_probe import Segmenter  # noqa: E402


def read(path):
    with wave.open(str(path), 'rb') as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float32) / 32768


def run_clip(session, audio, max_ms, silence_ms=320):
    segmenter = Segmenter(silence_ms=silence_ms, max_ms=max_ms)
    pieces = []
    first_audio = None
    decode_total = 0.0
    frame = 320  # 20 ms
    for index in range(len(audio) // frame):
        found = segmenter.push(audio[index * frame:(index + 1) * frame], index)
        if not found:
            continue
        start, end = found
        chunk = audio[start * frame:end * frame]
        if len(chunk) < 3200:
            continue
        tick = time.perf_counter()
        text = session.run(chunk, timestamps='none').text.strip()
        decode_total += (time.perf_counter() - tick) * 1000
        if text and first_audio is None:
            first_audio = round(end * frame / 16, 1)
        pieces.append(text)
    if segmenter.speaking:
        chunk = audio[segmenter.start * frame:]
        if len(chunk) >= 3200:
            text = session.run(chunk, timestamps='none').text.strip()
            if text and first_audio is None:
                first_audio = round(len(audio) / 16, 1)
            pieces.append(text)
    text = ''.join(pieces)
    return text, first_audio, round(decode_total, 1)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', default='models/qwen3-asr-0.6b/Qwen3-ASR-0.6B-Q8_0.gguf')
    parser.add_argument('--backend', default='vulkan')
    parser.add_argument('--max-ms', type=int, default=4000)
    parser.add_argument('--silence-ms', type=int, default=320)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()

    import transcribe_cpp as tc
    manifest = json.loads((ROOT / 'work/p0/manifest.json').read_text(encoding='utf8'))
    rows = []
    with tc.Model(ROOT / args.model, backend=args.backend) as model:
        with model.session(n_threads=4) as session:
            # Warm up once so the first clip is not paying the model's first-call cost.
            session.run(np.zeros(16000, dtype=np.float32), timestamps='none')
            for item in manifest:
                audio = read(item['wav'])
                text, first, cost = run_clip(session, audio, args.max_ms, args.silence_ms)
                rows.append({'id': item['id'], 'category': item['category'], 'expected': item['expected'],
                             'text': text, 'firstTextAudioMs': first, 'decodeMs': cost,
                             'audioMs': round(len(audio) / 16, 1)})

    by_cat = {}
    for row in rows:
        ref = units(row['expected'])
        ok = ref == units(row['text']) if ref else not row['text'].strip()
        name = LABELS.get(row['category'], row['category'])
        entry = by_cat.setdefault(name, [0, 0])
        entry[0] += 1
        entry[1] += ok
    scored = [r for r in rows if r['expected']]
    exact = sum(1 for r in scored if units(r['expected']) == units(r['text']))
    latency = [r['firstTextAudioMs'] for r in rows if r['firstTextAudioMs']]
    payload = {'model': args.model, 'backend': args.backend, 'maxMs': args.max_ms,
               'silenceMs': args.silence_ms, 'exact': exact, 'scored': len(scored),
               'firstTextAudioMs': {'p50': float(np.median(latency)) if latency else None,
                                    'max': max(latency) if latency else None,
                                    'n': len(latency)},
               'categories': {k: {'n': v[0], 'exact': v[1]} for k, v in by_cat.items()},
               'rows': rows}
    args.out.write_text(json.dumps(payload, ensure_ascii=False, indent=1), encoding='utf8')
    print(f'=== max {args.max_ms}ms silence {args.silence_ms}ms: exact {exact}/{len(scored)} '
          f'first-text p50 {payload["firstTextAudioMs"]["p50"]}ms max {payload["firstTextAudioMs"]["max"]}ms ===')
    for name, (n, ok) in sorted(by_cat.items(), key=lambda kv: -kv[1][0]):
        print(f'  {name:12s} {ok:3d}/{n}')


if __name__ == '__main__':
    main()
