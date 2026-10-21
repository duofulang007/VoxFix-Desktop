"""Offline batch scoring for a non-streaming model (Qwen3-ASR).

Decodes each clip in one pass and reports exact matches per category with the
same unit rules as tools/p0_score.py. This is an offline accuracy probe: the
audio must be complete before decoding, so its timing is not first-text latency.

Usage:
  python tools/offline_batch.py --model models/qwen3-asr-0.6b/Qwen3-ASR-0.6B-Q8_0.gguf --backend vulkan --out work/x.json
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


def read(path):
    with wave.open(str(path), 'rb') as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float32) / 32768


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', required=True)
    parser.add_argument('--backend', default='vulkan')
    parser.add_argument('--language', default='', help='empty = model default (auto)')
    parser.add_argument('--ids', default='')
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()

    import transcribe_cpp as tc
    manifest = json.loads((ROOT / 'work/p0/manifest.json').read_text(encoding='utf8'))
    if args.ids:
        wanted = set(args.ids.split(','))
        manifest = [m for m in manifest if m['id'] in wanted]

    rows = []
    language = args.language or None
    with tc.Model(ROOT / args.model, backend=args.backend) as model:
        with model.session(n_threads=4) as session:
            for item in manifest:
                audio = read(item['wav'])
                tick = time.perf_counter()
                result = session.run(audio, timestamps='none', language=language)
                elapsed = (time.perf_counter() - tick) * 1000
                rows.append({'id': item['id'], 'category': item['category'],
                             'expected': item['expected'], 'text': result.text.strip(),
                             'language': getattr(result, 'language', ''),
                             'decodeMs': round(elapsed, 1),
                             'audioMs': round(len(audio) / 16, 1)})
                print(f"{item['id']:22s} {elapsed:7.0f}ms {result.text.strip()[:50]!r}", flush=True)

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
    payload = {'model': args.model, 'backend': args.backend, 'language': language,
               'exact': exact, 'scored': len(scored),
               'categories': {k: {'n': v[0], 'exact': v[1]} for k, v in by_cat.items()},
               'rows': rows}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(payload, ensure_ascii=False, indent=1), encoding='utf8')
    print(f'\n=== {args.model} {args.backend} lang={language} exact {exact}/{len(scored)} ===')
    for name, (n, ok) in sorted(by_cat.items(), key=lambda kv: -kv[1][0]):
        print(f'  {name:12s} {ok:3d}/{n}')


if __name__ == '__main__':
    main()
