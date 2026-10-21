"""Growing-audio prefix experiment; never inject text or change the daily model."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
os.environ['TRANSCRIBE_LIBRARY'] = str(ROOT / 'runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll')
import transcribe_cpp as tc


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('video', type=Path)
    args = parser.parse_args()
    ffmpeg = ROOT / '.tmp/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
    decoded = subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', str(args.video),
                              '-vn', '-f', 's16le', '-ar', '16000', '-ac', '1', 'pipe:1'],
                             check=True, capture_output=True).stdout
    audio = np.frombuffer(decoded, dtype='<i2').astype(np.float32) / 32768
    cases = [('opening', audio[:8 * 16000]), ('palace', audio[90 * 16000:105 * 16000]),
             ('blender', audio[108 * 16000:120 * 16000])]
    results = []
    with tc.Model(ROOT / 'models/qwen3-asr-0.6b/Qwen3-ASR-0.6B-Q8_0.gguf', backend='vulkan') as model:
        with model.session(n_threads=4) as session:
            # Keep shader compilation out of the warmed first-text measurement.
            session.run(cases[0][1][:16000], timestamps='none')
            for label, pcm in cases:
                history, committed, trace = [], '', []
                first, failure = None, None
                began = time.perf_counter()
                for end in range(6400, len(pcm) + 6400, 6400):
                    end = min(end, len(pcm))
                    time.sleep(max(0, began + end / 16000 - time.perf_counter()))
                    tick = time.perf_counter()
                    text = session.run(pcm[:end], timestamps='none').text
                    decode = (time.perf_counter() - tick) * 1000
                    history = (history + [text])[-2:]
                    candidate = os.path.commonprefix(history) if len(history) == 2 else ''
                    revised = not candidate.startswith(committed)
                    trace.append({'audioMs': end / 16, 'wallMs': (time.perf_counter() - began) * 1000,
                                  'decodeMs': decode, 'text': text, 'candidate': candidate,
                                  'confirmedPrefixChanged': revised})
                    if revised and failure is None:
                        failure = trace[-1]['audioMs']
                    if failure is None:
                        committed = candidate
                        if committed and first is None:
                            first = trace[-1]['wallMs']
                result = {'id': label, 'firstStableMs': first, 'firstPrefixFailureAudioMs': failure,
                          'committedBeforeFailure': committed, 'final': trace[-1]['text'], 'trace': trace}
                results.append(result)
                print(json.dumps({k: v for k, v in result.items() if k != 'trace'}, ensure_ascii=False), flush=True)
    target = ROOT / 'work/source-video-20261020/qwen-prefix-paced.json'
    target.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf8')


if __name__ == '__main__':
    main()
