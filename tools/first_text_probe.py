"""Measure first-text latency under real-time pacing for different right contexts.

Production feeds 100 ms of audio per tick; this probe paces the feed to the audio
clock so the reported time is "audio start to first characters", not offline
decode speed. Also reports whether decoding keeps up (drain time per second of
audio).
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
sys.path.insert(0, str(ROOT / 'app'))
from recognize import QuietBoost  # noqa: E402

MODEL = ROOT / 'models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf'


def read(path):
    with wave.open(str(path), 'rb') as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float32) / 32768


def run(session, tc, audio, right, threads_note=''):
    boost = QuietBoost()
    family = None if right is None else tc.ParakeetStreamOptions(att_context_right=right)
    stream = session.stream(timestamps='none', family=family) if family else session.stream(timestamps='none')
    began = time.perf_counter()
    first = None
    lag = 0.0
    try:
        for offset in range(0, len(audio), 1600):
            piece = audio[offset:offset + 1600]
            tick = time.perf_counter()
            stream.feed(boost.process(piece))
            text = stream.text().display.lstrip()
            lag = max(lag, (time.perf_counter() - began) - (offset + len(piece)) / 16000)
            if text and first is None:
                first = round((time.perf_counter() - began) * 1000, 1)
            delay = began + (offset + 1600) / 16000 - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
        stream.feed(np.zeros(5120, dtype=np.float32))
        stream.finalize()
        return {'right': right, 'firstTextMs': first, 'maxLagMs': round(lag * 1000, 1),
                'text': stream.text().display.lstrip()}
    finally:
        stream.reset()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--ids', default='new-everyday-0,new-number-0,mix-huihui-4,sentence-7,long-0-0')
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    import transcribe_cpp as tc
    manifest = {m['id']: m for m in json.loads((ROOT / 'work/p0/manifest.json').read_text(encoding='utf8'))}
    rows = []
    with tc.Model(MODEL, backend='cpu') as model:
        with model.session(n_threads=4) as session:
            for clip in args.ids.split(','):
                audio = read(manifest[clip]['wav'])
                entry = {'id': clip, 'seconds': round(len(audio) / 16000, 2)}
                for right in (None, 0, 6, 13):
                    entry[str(right)] = run(session, tc, audio, right)
                rows.append(entry)
                print(clip, {k: v['firstTextMs'] for k, v in entry.items() if isinstance(v, dict)}, flush=True)
    args.out.write_text(json.dumps(rows, ensure_ascii=False, indent=1), encoding='utf8')


if __name__ == '__main__':
    main()
