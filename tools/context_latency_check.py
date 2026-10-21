"""Final paced comparison, run after bulk inference and builds have finished."""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'runtime/sherpa-context-51aaac5'))
from realtime_probe import MANIFEST, read, run, create_engine
from benchmark_context import OUT, TERMS
from nemo_context_probe import NemoSession
from sherpa_context_probe import create, encode
from tune_context import fixtures


def main():
    selected = [next(r for r in MANIFEST if r['id'] == identity) for identity in ['plain-zh-0', 'long-0-0']]
    selected.append(next(r for r in fixtures() if r['id'] == 'Microsoft-Huihui-term-0'))
    rows = []

    def measure(session, label):
        for row in selected:
            result = run(session, row, read(row), label, 'paced', paced=True)
            rows.append(result)
            print(label, row['id'], 'firstMs', result['firstMs'], 'exact', result['exact'],
                  'prefixChanged', result['nonPrefixFinal'], 'error', result['error'], flush=True)
        (OUT / 'final-paced.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf8')

    with create_engine('nemotron-cpp') as session:
        measure(session, 'current-production')
    session = NemoSession(ROOT / 'models/nemo-official/nemotron-3.5-asr-streaming-0.6b.q8_0.gguf', TERMS, 2, 6)
    try:
        measure(session, 'nemo-fast-boost2')
    finally:
        session.close()
    session = create('modified_beam_search', 1.5, encode())
    measure(session, 'sherpa-stable-beam')
    assert len(rows) == 9


if __name__ == '__main__':
    main()
