"""Compare right context against production using identical PCM and scoring."""
import json
from pathlib import Path
from realtime_probe import MANIFEST, create_engine, read, run
import numpy as np
import re


class ContextSession:
    def __init__(self, session, right):
        self.session, self.right = session, right

    def stream(self, **options):
        import transcribe_cpp as tc
        stream = self.session.stream(family=tc.ParakeetStreamOptions(att_context_right=6 if self.right == 6 else 13), **options)
        if self.right != 6:
            stream.feed(np.zeros(self.right, dtype=np.float32))
        return stream


def main():
    rows = []
    output = Path('work/realtime-probe-20261020/latency-candidates.json')
    with create_engine('nemotron-cpp') as session:
        for right in [5120, 10240, 6]:
            candidate = ContextSession(session, right)
            for row in MANIFEST:
                rows.append(run(candidate, row, read(row), 'context-' + str(right), 'base'))
            group = [r for r in rows if r['engine'] == 'context-' + str(right) and r['variant'] == 'base']
            assert len(group) == len(MANIFEST) and not any(r['error'] for r in group)
            chinese = [r for r in group if r['category'] != 'cantonese' and re.search(r'[\u3400-\u9fff]', r['expected'])]
            print(right, 'all', sum(r['exact'] for r in group), '/', len(group), 'core', sum(r['exact'] for r in chinese), '/', len(chinese), 'errors', sum(bool(r['error']) for r in group), flush=True)
            for identity in ['plain-zh-0', 'new-short-0', 'long-0-0']:
                row = next(r for r in MANIFEST if r['id'] == identity)
                result = run(candidate, row, read(row), 'context-' + str(right), 'paced', paced=True)
                rows.append(result)
                print(identity, result['firstMs'], result['text'], flush=True)
            output.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf8')


if __name__ == '__main__':
    main()
