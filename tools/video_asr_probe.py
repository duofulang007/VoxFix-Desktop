"""Decode the supplied source video in memory; compare isolated streaming engines."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

import numpy as np
# Select the isolated build before realtime_probe imports sherpa_onnx.
if '--engine' in sys.argv and sys.argv[sys.argv.index('--engine') + 1] == 'beam':
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'runtime/sherpa-context-51aaac5'))
from realtime_probe import ROOT, create_engine, run


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('video', type=Path)
    parser.add_argument('--engine', choices=['current', 'native', 'beam', 'sensevoice', 'qwen'], default='current')
    parser.add_argument('--backend', choices=['cpu', 'vulkan'], default='cpu')
    args = parser.parse_args()
    ffmpeg = ROOT / '.tmp/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
    source = args.video.resolve(strict=True)
    result = subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', str(source),
                             '-vn', '-f', 's16le', '-ar', '16000', '-ac', '1', 'pipe:1'],
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True)
    assert len(result.stdout) % 2 == 0
    audio = np.frombuffer(result.stdout, dtype='<i2').astype(np.float32) / 32768
    assert 0 < len(audio) <= 180 * 16000, 'Probe covers videos up to three minutes'
    row = {'id': source.stem, 'category': 'source-video', 'expected': ''}
    began = time.perf_counter()
    if args.engine == 'current':
        with create_engine('nemotron-cpp') as session:
            output = run(session, row, audio, args.engine, 'original-digital')
    elif args.engine == 'native':
        from nemo_context_probe import NemoSession
        from benchmark_context import TERMS
        session = NemoSession(ROOT / 'models/nemo-official/nemotron-3.5-asr-streaming-0.6b.q8_0.gguf',
                              TERMS, boost=2, right=6)
        try:
            output = run(session, row, audio, args.engine, 'original-digital')
        finally:
            session.close()
    elif args.engine == 'beam':
        from sherpa_context_probe import create, encode
        session = create('modified_beam_search', 1.5, encode())
        output = run(session, row, audio, args.engine, 'original-digital')
    elif args.engine == 'sensevoice':
        import sherpa_onnx as sherpa
        base = Path('D:/Projects/VoxFix/models/sensevoice')
        recognizer = sherpa.OfflineRecognizer.from_sense_voice(
            model=str(base / 'model.int8.onnx'), tokens=str(base / 'tokens.txt'),
            num_threads=4, language='auto', use_itn=True)
        pieces = []
        for offset in range(0, len(audio), 20 * 16000):
            stream = recognizer.create_stream()
            stream.accept_waveform(16000, audio[offset:offset + 20 * 16000])
            recognizer.decode_stream(stream)
            pieces.append({'startSeconds': offset / 16000, 'text': stream.result.text})
        output = {'engine': args.engine, 'text': ''.join(p['text'] for p in pieces),
                  'pieces': pieces, 'seconds': len(audio) / 16000, 'streaming': False,
                  'warning': 'Fixed 20-second windows may split words; offline accuracy probe only'}
    else:
        os.environ['TRANSCRIBE_LIBRARY'] = str(ROOT / 'runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll')
        import transcribe_cpp as tc
        model_path = ROOT / 'models/qwen3-asr-0.6b/Qwen3-ASR-0.6B-Q8_0.gguf'
        pieces = []
        with tc.Model(model_path, backend=args.backend) as model:
            with model.session(n_threads=4) as session:
                for offset in range(0, len(audio), 20 * 16000):
                    tick = time.perf_counter()
                    text = session.run(audio[offset:offset + 20 * 16000], timestamps='none').text
                    piece = {'startSeconds': offset / 16000, 'text': text,
                             'decodeMs': (time.perf_counter() - tick) * 1000}
                    pieces.append(piece)
                    print(json.dumps(piece, ensure_ascii=False), flush=True)
        output = {'engine': args.engine, 'backend': args.backend, 'text': ''.join(p['text'] for p in pieces),
                  'pieces': pieces, 'seconds': len(audio) / 16000, 'streaming': False,
                  'warning': 'Offline fixed-window probe; not live first-text latency'}
    # No verified reference yet; do not publish an accuracy score against an empty string.
    for key in ['expected', 'exact', 'unitErrors', 'refUnits']:
        output.pop(key, None)
    output['source'] = str(source)
    output['elapsedSeconds'] = time.perf_counter() - began
    output['rms'] = float(np.sqrt(np.mean(audio ** 2)))
    destination = ROOT / 'work/source-video-20261020'
    destination.mkdir(exist_ok=True)
    label = args.engine + ('-' + args.backend if args.engine == 'qwen' else '')
    (destination / (label + '.json')).write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding='utf8')
    print(json.dumps({k: v for k, v in output.items() if k not in ['updateMs', 'partialBeforeStop']}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
