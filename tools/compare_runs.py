"""Compare two recognition_probe result files clip by clip.

Reports exact-match counts, which clips changed, and how many were corrected
versus broken (same scoring rules as tools/p0_score.py).
"""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from p0_score import units  # noqa: E402


def load(path):
    data = json.loads(Path(path).read_text(encoding='utf8'))
    return {row['id']: row['liveFinal'] for row in data['results']}, data


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('baseline')
    parser.add_argument('candidate')
    parser.add_argument('--label', default='')
    args = parser.parse_args()
    base, base_data = load(args.baseline)
    cand, cand_data = load(args.candidate)
    expected = {row['id']: row['expected'] for row in base_data['results']}
    errors = {row['id']: row['error'] for row in cand_data['results']}

    same = corrected = broken = changed = 0
    for key in base:
        if key not in cand:
            continue
        if base[key] == cand[key]:
            same += 1
            continue
        changed += 1
        ref = units(expected.get(key, ''))
        was = ref == units(base[key]) if ref else not base[key].strip()
        now = ref == units(cand[key]) if ref else not cand[key].strip()
        if now and not was:
            corrected += 1
        elif was and not now:
            broken += 1
        print(f'DIFF {key}: {base[key]!r} -> {cand[key]!r}')

    scored = [k for k in base if expected.get(k)]
    exact_base = sum(1 for k in scored if units(expected[k]) == units(base[k]))
    exact_cand = sum(1 for k in scored if units(expected[k]) == units(cand[k]))
    print(f'\n[{args.label}] clips={len(base)} identical={same} changed={changed} '
          f'corrected={corrected} broken={broken}')
    print(f'[exact match] baseline={exact_base}/{len(scored)} candidate={exact_cand}/{len(scored)}')
    failures = {k: v for k, v in errors.items() if v}
    if failures:
        print(f'[errors] {len(failures)}: ' + '; '.join(f'{k}={v}' for k, v in list(failures.items())[:5]))


if __name__ == '__main__':
    main()
