"""Check whether the parakeet right-context window changes accuracy.

Decodes the same clips with the runtime default and with explicit right
context values, using the production chunk cadence, and prints exact matches.
"""
import json
import os
from pathlib import Path
import sys
import wave

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
os.environ.setdefault('TRANSCRIBE_LIBRARY', str(
    ROOT / 'runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll'))
sys.path.insert(0, str(ROOT / 'app'))
sys.path.insert(0, str(ROOT / 'tools'))
from recognize import QuietBoost  # noqa: E402
from p0_score import units  # noqa: E402

MODEL = ROOT / 'models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf'


def read(path):
    with wave.open(str(path), 'rb') as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float32) / 32768


def decode(session, audio, module, right):
    boost = QuietBoost()
    options = None if right is None else module.ParakeetStreamOptions(att_context_right=right)
    stream = session.stream(timestamps='none', family=options) if options else session.stream(timestamps='none')
    try:
        for offset in range(0, len(audio), 1600):
            stream.feed(boost.process(audio[offset:offset + 1600]))
        stream.feed(np.zeros(5120, dtype=np.float32))
        stream.finalize()
        return stream.text().display.lstrip()
    finally:
        stream.reset()


def main():
    import transcribe_cpp as tc
    manifest = {m['id']: m for m in json.loads((ROOT / 'work/p0/manifest.json').read_text(encoding='utf8'))}
    ids = [i for i, m in manifest.items() if m['category'] in ('sentence', 'word', 'mixed', 'everyday')]
    ids.sort()
    configs = [None, 6, 13]
    scores = {c: 0 for c in configs}
    texts = {}
    with tc.Model(MODEL, backend='cpu') as model:
        with model.session(n_threads=4) as session:
            for clip in ids:
                audio = read(manifest[clip]['wav'])
                for right in configs:
                    text = decode(session, audio, tc, right)
                    texts[(clip, right)] = text
                    if units(manifest[clip]['expected']) == units(text):
                        scores[right] += 1
                print(f'{clip:20s} ' + ' | '.join(repr(texts[(clip, r)][:38]) for r in configs), flush=True)
    print('\nexact:', {str(k): v for k, v in scores.items()}, 'of', len(ids))
    (ROOT / 'work/lang-fix-20261020/right-context.json').write_text(
        json.dumps({f'{k[0]}|{k[1]}': v for k, v in texts.items()}, ensure_ascii=False, indent=1), encoding='utf8')


if __name__ == '__main__':
    main()
