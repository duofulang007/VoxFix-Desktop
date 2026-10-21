"""Windowed re-decode probe: can an offline model drive append-only streaming?

Every step the last N seconds of audio are decoded again; only the prefix that
stayed identical across several consecutive decodes is committed. Reports first
text position, decode cost, conflicts (committed prefix rewritten) and the final
text, so the append-only contract can be judged on numbers.
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

PUNCT = ' ，。、？！；：“”‘’（）《》…—·,.!?;:'


def normalize(text):
    return ''.join(c for c in text.lower() if c not in PUNCT)


def read(path):
    with wave.open(str(path), 'rb') as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float32) / 32768


def common_prefix(a, b):
    limit = min(len(a), len(b))
    index = 0
    while index < limit and a[index] == b[index]:
        index += 1
    return a[:index]


def outline(text):
    """Normalized characters plus the original index each one came from."""
    chars, index = [], []
    for position, char in enumerate(text):
        if char.lower() not in PUNCT:
            chars.append(char.lower())
            index.append(position)
    return ''.join(chars), index


def overlap(suffix_text, new_text):
    """How much of new_text repeats the tail of committed (window slid in)."""
    left, left_index = outline(suffix_text)
    right, _ = outline(new_text)
    limit = min(len(left), len(right))
    for size in range(limit, 0, -1):
        if left[-size:] == right[:size]:
            return size, left_index[-size], right
    return 0, None, right


def stream(session, audio, window_ms=4000, step_ms=300, agree=1, language=None):
    window = window_ms * 16
    step = step_ms * 16
    committed = ''
    conflicts = []
    decodes = []
    first_audio = first_wall = None
    clock = time.perf_counter()
    offset = 0
    while offset < len(audio):
        offset = min(len(audio), offset + step)
        chunk = audio[max(0, offset - window):offset]
        if len(chunk) < 1600:
            continue
        tick = time.perf_counter()
        text = session.run(chunk, timestamps='none', language=language).text.strip()
        cost = (time.perf_counter() - tick) * 1000
        decodes.append({'atMs': round(offset / 16), 'decodeMs': round(cost, 1)})
        size, start_at, _ = overlap(committed, text)
        if size == 0:
            # Window content does not touch what was already written: real rewrite
            # (or a wholly new phrase after a long pause). Start a new piece only
            # when the previous text clearly ended.
            if committed and not committed.endswith((' ', '，', '。', '？', '！', ',', '.', '?', '!')):
                conflicts.append({'atMs': round(offset / 16), 'committed': committed, 'seen': text})
                break
            fresh = text.strip()
        else:
            fresh = text[start_at:]
            if committed and not committed[-1].isspace() and fresh[:1].isascii() and fresh[:1].isalnum() and committed[-1].isascii() and committed[-1].isalnum():
                fresh = ' ' + fresh
        if fresh and first_audio is None:
            first_audio = round(offset / 16, 1)
            first_wall = round((time.perf_counter() - clock) * 1000, 1)
        committed += fresh
    return {'committed': committed, 'firstTextAudioMs': first_audio, 'firstTextWallMs': first_wall,
            'conflicts': conflicts, 'decodeCount': len(decodes),
            'decodeMsMean': round(float(np.mean([d['decodeMs'] for d in decodes])), 1) if decodes else None,
            'decodeMsMax': round(max([d['decodeMs'] for d in decodes]), 1) if decodes else None}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', default='models/qwen3-asr-0.6b/Qwen3-ASR-0.6B-Q8_0.gguf')
    parser.add_argument('--backend', default='vulkan')
    parser.add_argument('--ids', default='')
    parser.add_argument('--wav', action='append', default=[])
    parser.add_argument('--window', type=int, default=4000)
    parser.add_argument('--step', type=int, default=300)
    parser.add_argument('--agree', type=int, default=3)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()

    import transcribe_cpp as tc
    manifest = json.loads((ROOT / 'work/p0/manifest.json').read_text(encoding='utf8'))
    rows = []
    if args.ids:
        wanted = set(args.ids.split(','))
        rows = [m for m in manifest if m['id'] in wanted]
    for path in args.wav:
        rows.append({'id': Path(path).stem, 'expected': '', 'wav': path})

    results = []
    with tc.Model(ROOT / args.model, backend=args.backend) as model:
        with model.session(n_threads=4) as session:
            for row in rows:
                out = stream(session, read(row['wav']), args.window, args.step, args.agree)
                out.update({'id': row['id'], 'expected': row.get('expected', '')})
                results.append(out)
                flag = 'CONFLICT' if out['conflicts'] else 'ok'
                print(f"{row['id']:22s} {flag:9s} first={out['firstTextAudioMs']}ms "
                      f"decodes={out['decodeCount']} {out['committed'][:40]!r}", flush=True)
    args.out.write_text(json.dumps(results, ensure_ascii=False, indent=1), encoding='utf8')
    ok = [r for r in results if not r['conflicts']]
    print(f'\nno-conflict {len(ok)}/{len(results)}')


if __name__ == '__main__':
    main()
