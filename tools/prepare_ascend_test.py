"""Prepare the verified ASCEND test split and controlled stress variants, never training data."""
import argparse
from collections import Counter
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import wave

import numpy as np

from p0_score import units

ROOT = Path(__file__).resolve().parent.parent
PARQUET_SHA256 = 'a4c81d2b5ed6124f052089a695972808c16e0ce0c365ec9773c5d1a8fcf043a7'
CONDITIONS = ('original', 'slow085', 'fast115', 'quiet30db', 'noise15db')


def pcm16(audio):
    return np.rint(np.clip(audio, -1, 32767 / 32768) * 32768).astype('<i2').tobytes()


def stress(pcm, condition, ffmpeg, seed):
    if condition == 'original':
        return pcm
    if condition in ('slow085', 'fast115'):
        speed = .85 if condition == 'slow085' else 1.15
        return subprocess.run([ffmpeg, '-hide_banner', '-loglevel', 'error', '-f', 's16le',
            '-ar', '16000', '-ac', '1', '-i', 'pipe:0', '-af', 'atempo=' + str(speed),
            '-f', 's16le', 'pipe:1'], input=pcm, capture_output=True, check=True,
            creationflags=subprocess.CREATE_NO_WINDOW).stdout
    audio = np.frombuffer(pcm, dtype='<i2').astype(np.float32) / 32768
    if condition == 'quiet30db':
        return pcm16(audio * 10 ** (-30 / 20))
    if condition != 'noise15db':
        raise ValueError(condition)
    noise = np.random.default_rng(seed).normal(size=audio.size)
    level = np.sqrt(np.mean(audio ** 2)) / 10 ** (15 / 20)
    mixed = audio + noise * level / max(1e-12, np.sqrt(np.mean(noise ** 2)))
    # Global headroom preserves SNR without clipping the added noise.
    return pcm16(mixed / max(1, float(np.max(np.abs(mixed))) / .99))


def write_wav(path, pcm):
    with wave.open(str(path), 'wb') as out:
        out.setparams((1, 2, 16000, 0, 'NONE', 'not compressed'))
        out.writeframes(pcm)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', type=Path, default=ROOT / 'work/baseline-20261022')
    args = parser.parse_args()
    directory = args.directory.resolve()
    sys.path.append(str(directory / 'deps'))
    import pyarrow.parquet as pq
    import imageio_ffmpeg

    source = json.loads((directory / 'source.json').read_text(encoding='utf-8'))
    parquet = directory / 'ascend-test.parquet'
    assert source['dataset'] == 'CAiRE/ASCEND' and source['split'] == 'test'
    assert hashlib.sha256(parquet.read_bytes()).hexdigest() == source['sha256'] == PARQUET_SHA256
    output = directory / 'manifest.json'
    if output.exists():
        raise FileExistsError('Choose a fresh directory; manifests are not overwritten')
    old = json.loads((ROOT / 'work/p0/manifest.json').read_text(encoding='utf-8'))
    old_text = {tuple(units(row['expected'])) for row in old}
    old_pcm = set()
    for row in old:
        with wave.open(row['wav'], 'rb') as wav:
            old_pcm.add(hashlib.sha256(wav.readframes(wav.getnframes())).hexdigest())
    seen, prepared, excluded = set(), [], []
    for row in pq.read_table(parquet).to_pylist():
        with wave.open(io.BytesIO(row['audio']['bytes']), 'rb') as wav:
            assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (1, 2, 16000)
            pcm = wav.readframes(wav.getnframes())
        digest = hashlib.sha256(pcm).hexdigest()
        reason = ('old_text' if tuple(units(row['transcription'])) in old_text else
                  'old_audio' if digest in old_pcm else 'duplicate_audio' if digest in seen else None)
        if reason:
            excluded.append({'id': row['id'], 'reason': reason})
            continue
        assert row['language'] in ('zh', 'en', 'mixed') and pcm
        seen.add(digest)
        prepared.append((row, pcm))
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    wav_dir = directory / 'wav'
    wav_dir.mkdir(exist_ok=True)
    manifest = []
    for condition in CONDITIONS:
        for index, (row, pcm) in enumerate(prepared):
            identity = 'ascend-test-' + row['id'] + '-' + condition
            path = wav_dir / (identity + '.wav')
            assert not path.exists(), path
            audio = stress(pcm, condition, ffmpeg, 20261022 + index)
            write_wav(path, audio)
            manifest.append({'id': identity, 'category': row['language'] + '/' + condition,
                'expected': row['transcription'], 'wav': str(path),
                'pcmSha256': hashlib.sha256(audio).hexdigest(), 'seconds': len(audio) / 32000,
                'sourceId': row['id'], 'speaker': row['original_speaker_id'],
                'sourceSplit': 'test', 'condition': condition})
        print(json.dumps({'prepared': condition, 'count': len(prepared)}, ensure_ascii=True), flush=True)
        if condition == 'original':
            (directory / 'manifest-original.json').write_text(
                json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    with output.open('x', encoding='utf-8') as out:
        json.dump(manifest, out, ensure_ascii=False, indent=2)
    receipt = {'source': source, 'rawRows': len(prepared) + len(excluded),
        'originalRecordings': len(prepared), 'uniqueReferenceTexts': len({tuple(units(r['expected']))
            for r in manifest if r['condition'] == 'original'}),
        'conditions': list(CONDITIONS), 'categories': dict(Counter(r['category'] for r in manifest)),
        'excluded': excluded, 'noiseSeed': 20261022,
        'note': 'Stress variants share source recordings; not independent samples. Test-only; never train on this split.'}
    (directory / 'preparation.json').write_text(json.dumps(receipt, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(receipt, ensure_ascii=True))


if __name__ == '__main__':
    main()
