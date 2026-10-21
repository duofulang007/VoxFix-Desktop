"""Search acoustic degradations that make the automatic language choice drift.

Runs the production-like stream on generated variants and reports any output
that leaves the Chinese/Latin script range (the reported failure class).
"""
import importlib.util
import os
from pathlib import Path
import sys
import wave

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
os.environ.setdefault('TRANSCRIBE_LIBRARY', str(
    ROOT / 'runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll'))
sys.path.insert(0, str(ROOT / 'app'))
from recognize import QuietBoost, outside_product_scripts  # noqa: E402

MODEL = ROOT / 'models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf'


def read(path):
    with wave.open(str(path), 'rb') as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float32) / 32768


def resample(audio, factor):
    return np.interp(np.arange(0, len(audio) - 1, factor), np.arange(len(audio)), audio).astype(np.float32)


def lowpass(audio, cutoff):
    alpha = 1 - np.exp(-2 * np.pi * cutoff / 16000)
    out = np.empty_like(audio)
    acc = 0.0
    for i, value in enumerate(audio):
        acc += alpha * (value - acc)
        out[i] = acc
    return out


def reverb(audio, ms, mix, seed):
    rng = np.random.default_rng(seed)
    ir = rng.normal(0, 1, int(ms * 16)).astype(np.float32)
    ir *= np.exp(-np.arange(len(ir)) / (ms * 16 / 4)).astype(np.float32)
    ir /= np.sqrt(np.sum(ir ** 2))
    return (1 - mix) * audio + mix * np.convolve(audio, ir)[:len(audio)]


def noise(audio, snr_db, seed):
    rng = np.random.default_rng(seed)
    power = np.mean(audio ** 2)
    return audio + rng.normal(0, np.sqrt(power / (10 ** (snr_db / 10))), audio.size).astype(np.float32)


def variants(audio):
    for speed in (1.0, 1.1, 1.2, 1.3):
        for cut, ms, mix, snr in ((3000, 500, 0.6, 12), (4000, 400, 0.5, 15), (3500, 700, 0.7, 10)):
            value = resample(audio, speed) if speed != 1.0 else audio.copy()
            value = lowpass(reverb(value, ms, mix, 21), cut)
            value = noise(value, snr, 22)
            peak = np.max(np.abs(value)) or 1
            yield f's{speed}-lp{cut}-rv{ms}/{mix}-snr{snr}', value / peak * 0.8


def run(session, audio, language=None):
    boost = QuietBoost()
    stream = session.stream(timestamps='none', language=language)
    try:
        first = ''
        for offset in range(0, len(audio), 1600):
            stream.feed(boost.process(audio[offset:offset + 1600]))
            text = stream.text().display.lstrip()
            if text:
                first = text
                break
        stream.feed(np.zeros(5120, dtype=np.float32))
        stream.finalize()
        return first, stream.text().display.lstrip()
    finally:
        stream.reset()


def main():
    import transcribe_cpp as tc
    clips = [ROOT / 'work/p0/wav/sentence-7.wav', ROOT / 'work/p0/wav/sentence-0.wav',
             ROOT / 'work/p0/wav/word-13.wav']
    with tc.Model(MODEL, backend='cpu') as model:
        with model.session(n_threads=4) as session:
            for clip in clips:
                audio = read(clip)
                for name, value in variants(audio):
                    first, final = run(session, value)
                    flag = 'FOREIGN' if outside_product_scripts(first) or outside_product_scripts(final) else ''
                    print(f'{clip.stem:12s} {name:34s} {flag:8s} first={first[:40]!r} final={final[:70]!r}',
                          flush=True)


if __name__ == '__main__':
    main()
