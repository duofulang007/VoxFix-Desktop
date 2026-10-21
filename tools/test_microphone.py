"""Real SenseVoice JSON protocol, paced previews, endpoints and repeated sessions.

No microphone or desktop input. Fixed recordings only. Drafts may change;
only endpoint-confirmed text must be append-only.
"""
import base64
import json
from pathlib import Path
import queue
import subprocess
import sys
import threading
import time
import wave

import numpy as np

ROOT = Path(__file__).resolve().parent.parent


def read_pcm(name):
    with wave.open(str(ROOT / 'work/p0/wav' / (name + '.wav')), 'rb') as audio:
        assert (audio.getnchannels(), audio.getsampwidth(), audio.getframerate()) == (1, 2, 16000)
        return audio.readframes(audio.getnframes())


def main():
    process = subprocess.Popen([sys.executable, '-u', str(ROOT / 'app/recognize.py')],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        text=True, encoding='utf-8', cwd=ROOT, creationflags=subprocess.CREATE_NO_WINDOW)
    replies, errors = queue.Queue(), []

    def collect():
        for line in process.stdout:
            replies.put(json.loads(line))
        replies.put({'error': 'worker exited'})

    threading.Thread(target=collect, daemon=True).start()
    threading.Thread(target=lambda: errors.extend(process.stderr.readlines()), daemon=True).start()

    def request(value):
        process.stdin.write(json.dumps(value) + '\n')
        process.stdin.flush()
        result = replies.get(timeout=30)
        assert 'error' not in result, result
        return result

    def start():
        assert request({'op': 'start', 'id': 'test'})['event'] == 'started'

    def feed(pcm, pace=0):
        previous, preview = '', ''
        first_preview = first_commit = None
        changes = []
        began = time.perf_counter()
        for offset in range(0, len(pcm), 3200):
            part = pcm[offset:offset + 3200]
            if pace:
                time.sleep(max(0, began + (offset + len(part)) / 32000 / pace - time.perf_counter()))
            result = request({'op': 'audio', 'id': 'test', 'pcm': base64.b64encode(part).decode()})
            assert result['text'].startswith(previous), ('committed text regressed', result)
            if result['preview'] and first_preview is None:
                first_preview = round((time.perf_counter() - began) * 1000)
            if result['text'] != previous:
                changes.append((round((offset + len(part)) / 32000, 2), len(result['text'])))
                if first_commit is None:
                    first_commit = round((time.perf_counter() - began) * 1000)
            previous, preview = result['text'], result['preview']
        return {'text': previous, 'preview': preview, 'firstPreviewMs': first_preview,
                'firstCommitMs': first_commit, 'committedChanges': changes}

    def stop():
        began = time.perf_counter()
        result = request({'op': 'stop', 'id': 'test'})
        assert result['event'] == 'final' and result['preview'] == '', result
        return result, round((time.perf_counter() - began) * 1000, 1)

    report = {}
    try:
        ready = replies.get(timeout=30)
        assert ready.get('ready') and ready.get('segmented') and ready.get('streaming') is False, ready
        assert ready['model'] == 'SenseVoiceSmall INT8', ready
        report['ready'] = ready
        for packet in (32000, 3200):
            start()
            request({'op': 'route', 'id': 'test', 'route': 1})
            batch_pcm = read_pcm('new-short-0') + bytes(48000)
            for offset in range(0, len(batch_pcm), packet):
                request({'op': 'audio', 'id': 'test', 'route': 1,
                         'pcm': base64.b64encode(batch_pcm[offset:offset + packet]).decode()})
            deadline = time.perf_counter() + 3
            while time.perf_counter() < deadline:
                result = request({'op': 'poll', 'id': 'test'})
                if '\u4f60\u597d' in result['text']:
                    break
                time.sleep(.02)
            assert '\u4f60\u597d' in result['text'], ('fallback must finish without a focus-return flush', packet, result)
            stop()
        report['packetBatching'] = 'passed: fallback text before focus return for 100ms and 1s packets'
        start()
        pcm = read_pcm('long-0-0')
        live = feed(pcm, pace=1)
        final, stop_ms = stop()
        assert live['firstPreviewMs'] is not None and live['firstPreviewMs'] < 1800, live
        assert live['text'] and live['firstCommitMs'] < 12000, live
        assert final['text'].startswith(live['text']) and '修复问题' in final['text'], final
        assert '改天再去' in final['text'], final
        assert final['samples'] == len(pcm) // 2
        report['pacedLong'] = dict(live, stopMs=stop_ms)

        for clip, expected in [('new-short-0', '你好'), ('new-short-2', '好的'),
                               ('new-negation-6', '这笔钱没有到账'), ('plain-zh-0', '今天天气很好')]:
            start()
            feed(read_pcm(clip))
            result, _ = stop()
            assert expected in result['text'], (clip, result)

        start()
        quiet = (np.frombuffer(read_pcm('plain-zh-0'), dtype='<i2').astype(np.float32) * .001).astype('<i2').tobytes()
        feed(quiet)
        result, _ = stop()
        assert '散步' in result['text'], result
        report['quiet'] = 'passed: PCM16 attenuation, not physical microphone distance'

        for pcm in [bytes(64000), b'']:
            start()
            feed(pcm)
            assert stop()[0]['text'] == ''

        start()
        phrase = read_pcm('plain-zh-0') + bytes(16000)
        long_pcm = (phrase * 50)[:175 * 32000]
        live = feed(long_pcm, pace=10)
        final, stop_ms = stop()
        assert final['samples'] == len(long_pcm) // 2
        assert final['text'].count('今天天气很好') >= 30, len(final['text'])
        assert any(at > 30 for at, _ in live['committedChanges']), live
        report['longSession'] = dict(live, stopMs=stop_ms, seconds=len(long_pcm) / 32000,
                                     note='10x delivery validates continuity, not live latency')

        start()
        feed(read_pcm('plain-zh-0')[:3200])
        assert request({'op': 'cancel', 'id': 'test'})['event'] == 'cancelled'
        start()
        assert stop()[0]['text'] == ''
        for invalid in ['not base64!', 'AA==', '']:
            start()
            process.stdin.write(json.dumps({'op': 'audio', 'id': 'test', 'pcm': invalid}) + '\n')
            process.stdin.flush()
            assert 'error' in replies.get(timeout=5)

        report['passed'] = ['paced revisable drafts', 'endpoint commits', 'quiet speech', 'tail samples',
                            '175-second continuity', 'short words', 'negation', 'silence', 'cancel', 'bad audio recovery']
        destination = ROOT / 'work/sensevoice-20261021/protocol.json'
        destination.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps({k: v for k, v in report.items() if k != 'longSession'}, ensure_ascii=True), flush=True)
        print('PASS: ' + ' / '.join(report['passed']))
    finally:
        process.stdin.close()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            subprocess.run(['taskkill.exe', '/PID', str(process.pid), '/T', '/F'], capture_output=True)
            raise
    assert process.returncode == 0, errors[-5:]


if __name__ == '__main__':
    main()
