"""Paired real-production-handler evaluation of official streaming phrase boosting."""
import json
import re
from pathlib import Path
from realtime_probe import MANIFEST, read, run
from nemo_context_probe import NemoSession, ROOT

TERMS = ['VoxFix', 'Blender', 'GLM', 'Kimi', 'GPT', 'Qwen', 'Python', 'GitHub',
         'WiFi', 'PDF', 'API', 'JSON', 'git status', 'git commit']
OUT = ROOT / 'work/context-20261020'


def summary(rows):
    chinese = [r for r in rows if r['category'] != 'cantonese' and re.search(r'[\u3400-\u9fff]', r['expected'])]
    mixed = [r for r in rows if r['category'] == 'mixed']
    return {'n': len(rows), 'exact': sum(r['exact'] for r in rows),
            'chineseExact': sum(r['exact'] for r in chinese), 'chineseN': len(chinese),
            'chineseErrors': sum(r['unitErrors'] for r in chinese),
            'mixedExact': sum(r['exact'] for r in mixed), 'mixedN': len(mixed),
            'protocolErrors': sum(bool(r['error']) for r in rows)}


def main():
    OUT.mkdir(exist_ok=True)
    results = []
    configurations = [(13, 0), (13, 2), (6, 0), (6, 2)]
    model = ROOT / 'models/nemo-official/nemotron-3.5-asr-streaming-0.6b.q8_0.gguf'
    for right, boost in configurations:
        label = f'right{right}-boost{boost}'
        session = NemoSession(model, TERMS if boost else (), boost=boost, right=right)
        try:
            group = []
            for row in MANIFEST:
                result = run(session, row, read(row), 'nemo-official', label)
                group.append(result)
                with (OUT / 'results.jsonl').open('a', encoding='utf8') as output:
                    output.write(json.dumps(result, ensure_ascii=False) + '\n')
            print(label, json.dumps(summary(group)), flush=True)
            assert len(group) == len(MANIFEST)
            results.extend(group)
            for identity in ['plain-zh-0', 'long-0-0']:
                row = next(r for r in MANIFEST if r['id'] == identity)
                result = run(session, row, read(row), 'nemo-official', label + '-paced', paced=True)
                results.append(result)
                print(label, identity, 'firstMs', result['firstMs'], 'stopMs', result['stopMs'], 'error', result['error'], flush=True)
        finally:
            session.close()
        (OUT / 'results.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf8')


if __name__ == '__main__':
    main()
