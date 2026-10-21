"""Drive the production Recognition class in pseudo-streaming mode and score it.

Same 100 ms chunk cadence the shell uses, real-time pacing optional. Prints the
text growth curve so the streaming feel can be judged, plus exact matches.
"""
import argparse
import base64
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
sys.path.insert(0, str(ROOT / 'tools'))
from p0_score import units  # noqa: E402


def read(path):
    with wave.open(str(path), 'rb') as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', default='models/qwen3-asr-0.6b/Qwen3-ASR-0.6B-Q8_0.gguf')
    parser.add_argument('--backend', default='vulkan')
    parser.add_argument('--ids', default='')
    parser.add_argument('--pace', action='store_true')
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()

    spec = importlib.util.spec_from_file_location('recognize_under_test', str(ROOT / 'app/recognize.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    import transcribe_cpp as tc

    manifest = json.loads((ROOT / 'work/p0/manifest.json').read_text(encoding='utf8'))
    ids = [x for x in args.ids.split(',') if x] or [m['id'] for m in manifest]
    wanted = {m['id']: m for m in manifest if m['id'] in ids}
    rows = []
    model = tc.Model(ROOT / args.model, backend=args.backend)
    with model.session(n_threads=4) as session:
        session.run(np.zeros(16000 * 15, dtype=np.float32), timestamps='none')
        for clip in [m['id'] for m in manifest if m['id'] in wanted]:
            pcm = read(wanted[clip]['wav'])
            module.STATS['decode_ms'].clear()
            module.STATS['skipped'] = module.STATS['commits'] = 0
            recognition = module.Recognition(session, model.capabilities.languages)
            recognition.handle({'op': 'start', 'id': clip})
            began = time.perf_counter()
            first = None
            marks = []
            for offset in range(0, len(pcm), 3200):
                part = pcm[offset:offset + 3200]
                reply = recognition.handle({'op': 'audio', 'id': clip,
                                            'pcm': base64.b64encode(part.tobytes()).decode()})
                if reply['text'] and first is None:
                    first = round((time.perf_counter() - began) * 1000)
                last = marks[-1][1] if marks else ''
                if reply['text'] != last:
                    marks.append((round(len(part) / 32 + offset / 32), reply['text']))
                if args.pace:
                    delay = began + (offset + len(part)) / 32000 - time.perf_counter()
                    if delay > 0:
                        time.sleep(delay)
            final = recognition.handle({'op': 'stop', 'id': clip})['text']
            exact = units(wanted[clip]['expected']) == units(final) if wanted[clip]['expected'] else not final.strip()
            rows.append({'id': clip, 'expected': wanted[clip]['expected'], 'final': final,
                         'firstTextMs': first, 'exact': exact, 'marks': len(marks),
                         'skipped': module.STATS['skipped'],
                         'decodeMs': list(module.STATS['decode_ms'])})
            print(f"{clip:20s} first={first} ms marks={len(marks)} skip={module.STATS['skipped']} "
                  f"{'OK ' if exact else 'BAD'} | {final[:60]!r}", flush=True)
    args.out.write_text(json.dumps(rows, ensure_ascii=False, indent=1), encoding='utf8')
    print(f"\nexact {sum(r['exact'] for r in rows)}/{len(rows)}")


if __name__ == '__main__':
    main()
