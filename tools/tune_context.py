"""Evaluate a small fixed tuning set before spending time on full-corpus runs."""
import json
from pathlib import Path
from benchmark_context import TERMS, OUT, summary
from realtime_probe import MANIFEST, read, run
from nemo_context_probe import NemoSession, ROOT


def fixtures():
    sentences = json.loads((OUT / 'sentences.json').read_text(encoding='utf8'))
    rows = []
    for voice in ['Microsoft-Huihui', 'Microsoft-Yaoyao']:
        for item in sentences:
            row = dict(item, id=voice + '-' + item['id'])
            row['wav'] = str(OUT / 'audio' / (row['id'] + '.wav'))
            rows.append(row)
    assert len(rows) == 28 and all(Path(r['wav']).is_file() for r in rows)
    return rows


def main():
    selected = []
    for category in ['baseline', 'everyday', 'short', 'negation', 'mixed', 'number']:
        selected.extend([r for r in MANIFEST if r['category'] == category][:6])
    rows = []
    for right, boost, language in [(6, 0, 'zh-CN'), (6, .5, 'zh-CN'), (13, 0, 'zh-CN'), (13, .5, 'zh-CN')]:
        label = f'right{right}-boost{boost}-{language}'
        session = NemoSession(ROOT / 'models/nemo-official/nemotron-3.5-asr-streaming-0.6b.q8_0.gguf', TERMS if boost else (), boost, right, language)
        try:
            group = [run(session, row, read(row), 'nemo-official', label) for row in selected]
            controlled = [run(session, row, read(row), 'nemo-official', label) for row in fixtures()]
            rows.extend(group + controlled)
            print(label, 'core', summary(group), 'terms', sum(r['exact'] for r in controlled if r['category'] == 'term'),
                  'controls', sum(r['exact'] for r in controlled if r['category'] == 'control'), flush=True)
        finally:
            session.close()
        (OUT / 'tuning.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf8')


if __name__ == '__main__':
    main()
