"""Compare zh-CN vs en-US hypotheses on the same audio using token confidence."""
import argparse
import json
import os
from pathlib import Path
import sys

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
os.environ.setdefault('TRANSCRIBE_LIBRARY', str(
    ROOT / 'runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll'))
sys.path.insert(0, str(ROOT / 'app'))
from recognize import QuietBoost  # noqa: E402

MODEL = ROOT / 'models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf'


def read_wav(path):
    import wave
    with wave.open(str(path), 'rb') as wav:
        return np.frombuffer(wav.readframes(wav.getnframes()), dtype='<i2').astype(np.float32) / 32768


def decode(session, audio, language):
    boost = QuietBoost()
    stream = session.stream(timestamps='none', language=language)
    try:
        for offset in range(0, len(audio), 1600):
            stream.feed(boost.process(audio[offset:offset + 1600]))
        stream.feed(np.zeros(5120, dtype=np.float32))
        stream.finalize()
        snap = stream.snapshot()
        text = stream.text().display.lstrip()
        probs = [t.p for t in snap.tokens]
        return {
            'language': language, 'text': text, 'tokens': len(probs),
            'meanP': round(float(np.mean(probs)), 4) if probs else None,
            'minP': round(float(np.min(probs)), 4) if probs else None,
            'geoP': round(float(np.exp(np.mean(np.log(np.clip(probs, 1e-9, 1))))), 4) if probs else None,
        }
    finally:
        stream.reset()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--wav', action='append', required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    import transcribe_cpp as tc
    rows = []
    with tc.Model(MODEL, backend='cpu') as model:
        with model.session(n_threads=4) as session:
            for path in args.wav:
                audio = read_wav(path)
                row = {'id': Path(path).stem}
                for language in ('zh-CN', 'en-US'):
                    row[language] = decode(session, audio, language)
                rows.append(row)
    args.out.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf8')


if __name__ == '__main__':
    main()
