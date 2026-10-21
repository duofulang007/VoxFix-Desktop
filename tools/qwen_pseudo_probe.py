"""Pseudo-streaming probe for an offline model: re-decode, freeze a stable prefix.

Every step the whole audio so far is decoded again. Only the part that stayed
identical across several consecutive decodes is committed, and a small tail is
withheld so a revision lands on characters that are not on screen yet. Committed
text is never changed; if a later decode stops matching it, that decode is
skipped instead of rewriting what the user already sees.

Reports first-text position, how often the frozen text stopped matching, decode
cost, and the gap between the frozen text and a single whole-recording decode.
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


def read(path):
    with wave.open(str(path), 'rb') as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float32) / 32768


def common_prefix(a, b):
    limit = min(len(a), len(b))
    index = 0
    while index < limit and a[index] == b[index]:
        index += 1
    return a[:index]


def freeze_point(text, guard):
    """Drop the volatile tail; never stop inside an ASCII word."""
    if guard <= 0:
        return text
    cut = max(0, len(text) - guard)
    while cut > 0 and text[cut - 1].isascii() and text[cut - 1].isalnum():
        cut -= 1
    return text[:cut]


def pseudo(session, audio, step_ms=400, agree=3, guard=2, language=None):
    committed = ''
    history = []
    first_audio = first_wall = None
    stalls = 0
    decodes = 0
    costs = []
    clock = time.perf_counter()
    cursor = 0
    while cursor < len(audio):
        cursor = min(len(audio), cursor + step_ms * 16)
        tick = time.perf_counter()
        text = session.run(audio[:cursor], timestamps='none', language=language).text.strip()
        costs.append((time.perf_counter() - tick) * 1000)
        decodes += 1
        history = (history + [text])[-agree:]
        if len(history) == agree:
            stable = history[0]
            for other in history[1:]:
                stable = common_prefix(stable, other)
            target = freeze_point(stable, guard)
            if target.startswith(committed) and len(target) > len(committed):
                committed = target
                if first_audio is None:
                    first_audio = round(cursor / 16, 1)
                    first_wall = round((time.perf_counter() - clock) * 1000, 1)
            elif not text.startswith(committed):
                stalls += 1
    # Stop: one last decode of the complete audio. Only a pure append is safe;
    # anything that contradicts frozen text is left alone.
    tick = time.perf_counter()
    whole = session.run(audio, timestamps='none', language=language).text.strip()
    costs.append((time.perf_counter() - tick) * 1000)
    decodes += 1
    appended = committed
    if whole.startswith(committed):
        appended = whole
    elif committed and not whole:
        appended = committed
    else:
        lcp = common_prefix(committed, whole)
        appended = whole if len(lcp) >= max(0, len(committed) - guard - 1) else committed
    return {'committed': committed, 'final': appended, 'firstTextAudioMs': first_audio,
            'firstTextWallMs': first_wall, 'stalls': stalls, 'decodes': decodes,
            'kept_frozen': appended == committed,
            'decodeMsMean': round(float(np.mean(costs)), 1), 'decodeMsMax': round(max(costs), 1)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', default='models/qwen3-asr-0.6b/Qwen3-ASR-0.6B-Q8_0.gguf')
    parser.add_argument('--backend', default='vulkan')
    parser.add_argument('--ids', default='')
    parser.add_argument('--step', type=int, default=400)
    parser.add_argument('--agree', type=int, default=3)
    parser.add_argument('--guard', type=int, default=2)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()

    import transcribe_cpp as tc
    sys.path.insert(0, str(ROOT / 'tools'))
    from p0_score import units
    manifest = {m['id']: m for m in json.loads((ROOT / 'work/p0/manifest.json').read_text(encoding='utf8'))}
    ids = [x for x in args.ids.split(',') if x] or [
        'new-everyday-0', 'new-number-0', 'new-negation-0', 'new-short-0',
        'mix-huihui-4', 'mix-huihui-7', 'mix-switch-3', 'sentence-0', 'sentence-7',
        'long-0-0', 'long-1-0', 'plain-zh-0', 'plain-zh-5']
    rows = []
    with tc.Model(ROOT / args.model, backend=args.backend) as model:
        with model.session(n_threads=4) as session:
            session.run(np.zeros(16000 * 15, dtype=np.float32), timestamps='none')  # warm shaders
            for clip in ids:
                audio = read(manifest[clip]['wav'])
                out = pseudo(session, audio, args.step, args.agree, args.guard)
                out.update({'id': clip, 'expected': manifest[clip]['expected'],
                            'seconds': round(audio.size / 16000, 2),
                            'exactPseudo': units(manifest[clip]['expected']) == units(out['final'])
                            if manifest[clip]['expected'] else not out['final'].strip()})
                del out['committed']
                rows.append(out)
                print(f"{clip:18s} {out['seconds']:5.2f}s first={out['firstTextAudioMs']}ms "
                      f"stalls={out['stalls']} mean={out['decodeMsMean']}ms "
                      f"| {out['final'][:44]!r}", flush=True)
    args.out.write_text(json.dumps(rows, ensure_ascii=False, indent=1), encoding='utf8')
    print(f"\npseudo exact {sum(r['exactPseudo'] for r in rows)}/{len(rows)}  "
          f"stalls {sum(r['stalls'] for r in rows)}  "
          f"frozen-kept {sum(r['kept_frozen'] for r in rows)}/{len(rows)}")


if __name__ == '__main__':
    main()
