"""Paired full-clip vs production segmentation on the existing 174 fixed clips."""
import base64
from collections import defaultdict
import json
from pathlib import Path
import sys
import time
import wave

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'app'))
from recognize import Recognition, load_model
from p0_score import units, distance


def main():
    session, backend = load_model()
    manifest = json.loads((ROOT / 'work/p0/manifest.json').read_text(encoding='utf-8'))
    rows = []
    for row in manifest:
        with wave.open(row['wav'], 'rb') as audio:
            pcm = audio.readframes(audio.getnframes())
        full = session.run(np.frombuffer(pcm, dtype='<i2').astype(np.float32) / 32768).text
        engine = Recognition(session)
        engine.handle({'op': 'start', 'id': 'batch'})
        began = time.perf_counter()
        try:
            for offset in range(0, len(pcm), 3200):
                engine.handle({'op': 'audio', 'id': 'batch', 'pcm': base64.b64encode(pcm[offset:offset + 3200]).decode()})
            text = engine.handle({'op': 'stop', 'id': 'batch'})['text']
            error = None
        except Exception as exc:
            text, error = '', str(exc)
        finally:
            engine.clear()
        expected, actual = units(row['expected']), units(text)
        rows.append({'id': row['id'], 'category': row['category'], 'expected': row['expected'],
                     'text': text, 'fullClip': full, 'exact': actual == expected,
                     'fullExact': units(full) == expected, 'unitErrors': distance(expected, actual),
                     'refUnits': len(expected), 'error': error,
                     'wallMs': round((time.perf_counter() - began) * 1000, 1)})
    categories = defaultdict(lambda: {'total': 0, 'exact': 0, 'fullExact': 0, 'errors': 0})
    for row in rows:
        group = categories[row['category']]
        group['total'] += 1
        group['exact'] += int(row['exact'])
        group['fullExact'] += int(row['fullExact'])
        group['errors'] += int(row['error'] is not None)
    result = {'model': 'SenseVoiceSmall INT8', 'backend': backend, 'itn': False,
              'count': len(rows), 'exact': sum(r['exact'] for r in rows),
              'fullExact': sum(r['fullExact'] for r in rows),
              'categories': dict(categories), 'rows': rows}
    output = ROOT / 'work/sensevoice-20261021/corpus-final.json'
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in result.items() if k != 'rows'}, ensure_ascii=True))
    assert not any(row['error'] for row in rows), 'Protocol/worker failure in corpus'


if __name__ == '__main__':
    main()
