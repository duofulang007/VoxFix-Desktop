"""Private history persistence and production integration, without microphone/model."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
import wave

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'app'))
from dictation_history import HistoryRecorder
from recognize import Recognition
from test_sensevoice import Session, Vad


class HistoryTests(unittest.TestCase):
    def settings(self, root, enabled):
        path = Path(root) / 'user-data/history-settings.json'
        path.parent.mkdir(exist_ok=True)
        path.write_text(json.dumps({'enabled': enabled}), encoding='utf-8')

    def test_opt_in_original_audio_and_no_automatic_selection(self):
        with tempfile.TemporaryDirectory() as root:
            recorder = HistoryRecorder(root)
            try:
                audio = np.linspace(-.2, .3, 16000, dtype=np.float32)
                self.assertFalse(recorder.begin())
                recorder.submit(audio, text='never saved')
                self.assertFalse(recorder.folder.exists())
                self.settings(root, True)
                self.assertTrue(recorder.begin())
                recorder.submit(audio, text='显示文本', firstText='初稿', rawText='裸识别', roundId='r1', start=0, end=16000)
                recorder.jobs.join()
                data = json.loads(next(recorder.folder.glob('*.json')).read_text(encoding='utf-8'))
                self.assertFalse(data['selectedForTraining'])
                self.assertFalse(data['reviewed'])
                self.assertEqual(data['correctText'], '显示文本')
                self.assertEqual(data['rawText'], '裸识别')
                with wave.open(str(recorder.folder / data['wav'])) as wav:
                    self.assertEqual(wav.getparams()[:4], (1, 2, 16000, 16000))
                    np.testing.assert_array_equal(np.frombuffer(wav.readframes(16000), dtype='<i2'), (audio * 32768).astype('<i2'))
                self.settings(root, False)
                self.assertFalse(recorder.begin())
                recorder.submit(audio, text='disabled')
                self.assertEqual(len(list(recorder.folder.glob('*.json'))), 1)
            finally:
                recorder.close()

    def test_capacity_and_bad_settings_fail_closed_without_deletion(self):
        with tempfile.TemporaryDirectory() as root:
            recorder = HistoryRecorder(root)
            try:
                self.settings(root, True)
                recorder.begin()
                recorder.folder.mkdir()
                kept = recorder.folder / 'unknown.wav'
                kept.write_bytes(b'keep')
                recorder.LIMIT = 4
                recorder.submit(np.ones(16000, dtype=np.float32), text='test')
                recorder.jobs.join()
                self.assertIn('上限', recorder.notice)
                self.assertEqual(kept.read_bytes(), b'keep')
                self.assertEqual(len(list(recorder.folder.iterdir())), 1)
                recorder.settings.write_text('{broken', encoding='utf-8')
                self.assertFalse(recorder.begin())
                self.assertIn('读取失败', recorder.notice)
            finally:
                recorder.close()

    def test_production_final_segment_records_original_without_changing_output(self):
        with tempfile.TemporaryDirectory() as root:
            self.settings(root, True)
            history = HistoryRecorder(root)
            session = Session()
            session.text = '个人声音'
            engine = Recognition(session, Vad(), history=history)
            try:
                engine.handle({'op': 'start', 'id': 'round'})
                audio = np.full(16000, .015, dtype=np.float32)
                with engine.condition:
                    engine.audio[:16000] = audio
                    engine.samples = 16000
                    engine.first_previews[(0, 0)] = '首次草稿'
                    engine.queue_segment(0, 16000, False)
                    engine.condition.notify_all()
                result = engine.handle({'op': 'stop', 'id': 'round'})
                self.assertEqual(result['text'], '个人声音')
                history.jobs.join()
                data = json.loads(next(history.folder.glob('*.json')).read_text(encoding='utf-8'))
                self.assertEqual((data['firstText'], data['rawText'], data['text']), ('首次草稿', '个人声音', '个人声音'))
                self.assertEqual((data['roundId'], data['start'], data['end']), ('round', 0, 16000))
                self.assertEqual(data['model'], 'R5 INT8')
                with wave.open(str(history.folder / data['wav'])) as wav:
                    self.assertEqual(np.frombuffer(wav.readframes(16000), dtype='<i2')[0], int(.015 * 32768))
            finally:
                engine.clear()
                history.close()

    def test_failed_archive_does_not_fail_recognition(self):
        with tempfile.TemporaryDirectory() as root:
            self.settings(root, True)
            history = HistoryRecorder(root)
            history._save = lambda *args: (_ for _ in ()).throw(OSError('disk unavailable'))
            engine = Recognition(Session(), Vad(), history=history)
            try:
                engine.handle({'op': 'start', 'id': 'round'})
                with engine.condition:
                    engine.audio[:16000] = .1
                    engine.samples = 16000
                    engine.queue_segment(0, 16000, False)
                    engine.condition.notify_all()
                self.assertEqual(engine.handle({'op': 'stop', 'id': 'round'})['text'], '暂时识别')
                history.jobs.join()
                self.assertIn('disk unavailable', history.notice)
                self.assertIn('disk unavailable', history.settings.with_name('history-status.json').read_text(encoding='utf-8'))
            finally:
                engine.clear()
                history.close()

    def test_rejected_foreign_final_is_available_for_manual_correction(self):
        with tempfile.TemporaryDirectory() as root:
            self.settings(root, True)
            history = HistoryRecorder(root)
            session = Session()
            session.text = 'こんにちは'
            engine = Recognition(session, Vad(), history=history)
            try:
                engine.handle({'op': 'start', 'id': 'round'})
                with engine.condition:
                    engine.audio[:16000] = .1
                    engine.samples = 16000
                    engine.queue_segment(0, 16000, False)
                    engine.condition.notify_all()
                with self.assertRaises(RuntimeError):
                    engine.handle({'op': 'stop', 'id': 'round'})
                history.jobs.join()
                data = json.loads(next(history.folder.glob('*.json')).read_text(encoding='utf-8'))
                self.assertTrue(data['recognitionRejected'])
                self.assertEqual(data['rawText'], 'こんにちは')
                self.assertFalse(data['selectedForTraining'])
                self.assertEqual(engine.committed, '')
            finally:
                engine.clear()
                history.close()


if __name__ == '__main__':
    unittest.main()
