"""Fresh paired full-clip/production evaluation; defaults to the legacy corpus."""
import argparse
import base64
from collections import defaultdict
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys
import time
import uuid
import wave

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'app'))
from recognize import Recognition, load_model
from p0_score import units, distance, exact


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def summarize(rows):
    groups = defaultdict(list)
    for row in rows:
        groups[row['category']].append(row)
    return {key: {'total': len(group), 'exact': sum(r['exact'] for r in group),
                  'fullExact': sum(r['fullExact'] for r in group),
                  'errors': sum(r['error'] is not None for r in group),
                  'unitErrors': sum(r['unitErrors'] for r in group),
                  'refUnits': sum(r['refUnits'] for r in group),
                  'unitErrorRate': sum(r['unitErrors'] for r in group) /
                      max(1, sum(r['refUnits'] for r in group))}
            for key, group in groups.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, default=ROOT / 'work/p0/manifest.json')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    run_id = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:8]
    output = args.output or ROOT / 'work/sensevoice-20261021' / ('corpus-' + run_id + '.json')
    journal = output.with_suffix('.jsonl')
    if output.exists() or journal.exists():
        raise FileExistsError('Choose a new output; existing results are never reused or overwritten')
    manifest = json.loads(args.manifest.read_text(encoding='utf-8'))
    assert manifest and len({r['id'] for r in manifest}) == len(manifest), 'Empty manifest or duplicate IDs'
    output.parent.mkdir(parents=True, exist_ok=True)
    metadata = {'runId': run_id, 'manifest': str(args.manifest.resolve()),
                'manifestSha256': sha256(args.manifest),
                'sourceSha256': sha256(ROOT / 'app/recognize.py'),
                'evaluatorSha256': sha256(__file__),
                'assetsSha256': {name: sha256(ROOT / 'models' / name) for name in
                    ['sensevoice/model.int8.onnx', 'sensevoice/tokens.txt',
                     'vad/silero_vad.onnx', 'punctuation/model.int8.onnx']},
                'delivery': 'unpaced 100ms packets; final text, not live latency or desktop insertion'}
    session, backend = load_model()
    rows = []
    with journal.open('x', encoding='utf-8') as progress:
        for index, row in enumerate(manifest):
            with wave.open(row['wav'], 'rb') as audio:
                assert (audio.getnchannels(), audio.getsampwidth(), audio.getframerate()) == (1, 2, 16000), row['id']
                pcm = audio.readframes(audio.getnframes())
            digest = hashlib.sha256(pcm).hexdigest()
            if row.get('pcmSha256') and row['pcmSha256'] != digest:
                raise ValueError('Audio checksum mismatch: ' + row['id'])
            full = session.run(np.frombuffer(pcm, dtype='<i2').astype(np.float32) / 32768).text
            engine = Recognition(session)
            identity = str(index) + '-' + run_id
            engine.handle({'op': 'start', 'id': identity})
            began = time.perf_counter()
            try:
                for offset in range(0, len(pcm), 3200):
                    engine.handle({'op': 'audio', 'id': identity, 'pcm': base64.b64encode(pcm[offset:offset + 3200]).decode()})
                text = engine.handle({'op': 'stop', 'id': identity})['text']
                error = None
            except Exception as exc:
                text, error = '', str(exc)
            finally:
                engine.clear()
            expected, actual = units(row['expected']), units(text)
            rows.append({'id': row['id'], 'category': row['category'], 'expected': row['expected'],
                         'text': text, 'fullClip': full, 'exact': exact(row['expected'], text),
                         'fullExact': exact(row['expected'], full), 'unitErrors': distance(expected, actual),
                         'refUnits': len(expected), 'error': error,
                         'pcmSha256': digest, 'seconds': len(pcm) / 32000,
                         'wallMs': round((time.perf_counter() - began) * 1000, 1)})
            progress.write(json.dumps(rows[-1], ensure_ascii=False) + '\n')
            progress.flush()
            if (index + 1) % 100 == 0 or index + 1 == len(manifest):
                print(json.dumps({'completed': index + 1, 'total': len(manifest),
                                  'categories': summarize(rows)}, ensure_ascii=True), flush=True)
    result = {'model': 'SenseVoiceSmall INT8', 'backend': backend, 'itn': False,
              'count': len(rows), 'exact': sum(r['exact'] for r in rows),
              'fullExact': sum(r['fullExact'] for r in rows),
              'categories': summarize(rows), 'metadata': metadata, 'rows': rows}
    with output.open('x', encoding='utf-8') as destination:
        json.dump(result, destination, ensure_ascii=False, indent=2)
    print(json.dumps({k: v for k, v in result.items() if k != 'rows'}, ensure_ascii=True))
    assert not any(row['error'] for row in rows), 'Rejected results or worker failures; see saved report'


if __name__ == '__main__':
    main()
