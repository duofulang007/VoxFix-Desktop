"""Opt-in, bounded local recording archive; never used as automatic labels."""
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import queue
import threading
import uuid
import wave

import numpy as np


class HistoryRecorder:
    LIMIT = 1024 * 1024 * 1024

    def __init__(self, root):
        self.folder = Path(root) / 'user-data' / 'dictation-history'
        self.settings = Path(root) / 'user-data' / 'history-settings.json'
        self.notice = ''
        self.enabled = False
        self.used = None
        self.usage_round = None
        self.jobs = queue.Queue(maxsize=24)
        self.thread = threading.Thread(target=self._loop, daemon=True)
        self.thread.start()

    def _report(self, message):
        self.notice = message
        try:
            path = self.settings.with_name('history-status.json')
            path.parent.mkdir(parents=True, exist_ok=True)
            temp = path.with_suffix('.tmp')
            temp.write_text(json.dumps({'notice': message, 'createdUtc': datetime.now(timezone.utc).isoformat()}, ensure_ascii=False), encoding='utf-8')
            os.replace(temp, path)
        except OSError:
            pass  # The protocol notice still reports a full or unwritable disk.

    def begin(self):
        self.enabled = False
        try:
            if self.settings.exists():
                settings = json.loads(self.settings.read_text(encoding='utf-8-sig'))
                self.enabled = settings.get('enabled') is True
            if self.enabled:
                self.notice = ''
        except (OSError, ValueError, AttributeError):
            self._report('听写记录设置读取失败，本轮未留档')
        return self.enabled

    def submit(self, audio, **fields):
        if not self.enabled:
            return
        try:
            self.jobs.put_nowait((audio.copy(), fields))
        except queue.Full:
            self._report('听写记录保存队列已满，部分片段未保存；听写继续')

    def _loop(self):
        while True:
            job = self.jobs.get()
            try:
                if job is None:
                    return
                self._save(*job)
            except Exception as error:
                self._report('听写记录未保存：' + str(error))
            finally:
                self.jobs.task_done()

    def _save(self, audio, fields):
        if not 0 < len(audio) <= 180 * 16000 or not np.isfinite(audio).all():
            raise ValueError('片段音频无效')
        self.folder.mkdir(parents=True, exist_ok=True)
        pcm = (np.clip(audio, -1, 32767 / 32768) * 32768).astype('<i2').tobytes()
        identity = uuid.uuid4().hex
        wav = self.folder / (identity + '.wav')
        metadata = self.folder / (identity + '.json')
        temporary = self.folder / (identity + '.tmp')
        data = dict(fields, id=identity, wav=wav.name, createdUtc=datetime.now(timezone.utc).isoformat(),
                    durationSeconds=len(audio) / 16000, sampleRate=16000, channels=1,
                    bitsPerSample=16, state='collected', reviewed=False,
                    selectedForTraining=False, ignored=False, correctText=fields['text'])
        metadata_text = json.dumps(data, ensure_ascii=False)
        if self.used is None or self.usage_round != fields.get('roundId'):
            self.used = sum(p.stat().st_size for p in self.folder.iterdir() if p.is_file())
            self.usage_round = fields.get('roundId')
        size = len(pcm) + 44 + len(metadata_text.encode('utf-8'))
        if self.used + size > self.LIMIT:
            raise OSError('已到 1 GB 上限，请整理记录；未删除任何原音，听写继续')
        try:
            with wave.open(str(temporary), 'wb') as output:
                output.setnchannels(1)
                output.setsampwidth(2)
                output.setframerate(16000)
                output.writeframes(pcm)
            temporary.rename(wav)
            temporary.write_text(metadata_text, encoding='utf-8')
            temporary.rename(metadata)
            self.used += size
        except Exception:
            self.used = None
            raise
        finally:
            if temporary.exists():
                temporary.unlink()
        # A completed WAV without JSON after a failure is retained for recovery.

    def close(self):
        self.jobs.put(None)
        self.thread.join()
