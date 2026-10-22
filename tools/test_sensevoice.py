"""Deterministic production-pipeline tests; no microphone or desktop input."""
import base64
from pathlib import Path
import sys
import threading
import time
from types import SimpleNamespace
import unittest

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'app'))
from recognize import Recognition, QuietBoost, SenseVoiceSession, join_text, punctuation_only


class Vad:
    def reset(self):
        self.audio = np.zeros(0, dtype=np.float32)
        self.completed = []
        self.active = False

    def __init__(self):
        self.reset()

    def accept_waveform(self, audio):
        self.audio = np.concatenate((self.audio, audio))
        self.active = True

    def is_speech_detected(self):
        return self.active

    @property
    def current_segment(self):
        return SimpleNamespace(start=0, samples=self.audio)

    @property
    def front(self):
        return self.completed[0]

    def empty(self):
        return not self.completed

    def pop(self):
        self.completed.pop(0)

    def flush(self):
        if self.active:
            self.completed.append(self.current_segment)
            self.active = False


class Session:
    def __init__(self):
        self.text = '暂时识别'
        self.called = threading.Event()

    def run(self, audio):
        self.called.set()
        return SimpleNamespace(text=self.text)


class PipelineTests(unittest.TestCase):
    def test_forced_tail_is_flushed_once_before_stop_or_route_change(self):
        class ForcedVad(Vad):
            def accept_waveform(self, audio):
                super().accept_waveform(audio)
                if len(self.audio) == 160000:
                    self.completed.append(self.current_segment)
                    self.active = False

        for operation in ('stop', 'route'):
            with self.subTest(operation=operation):
                engine = Recognition(Session(), ForcedVad())
                try:
                    engine.handle({'op': 'start', 'id': 'test'})
                    for _ in range(10):
                        self.feed(engine)
                    cut = engine.last_end
                    self.assertTrue(engine.carry)
                    self.assertLess(cut, engine.samples)
                    if operation == 'route':
                        engine.handle({'op': 'route', 'id': 'test', 'route': 1})
                        self.feed(engine, .2)
                    result = engine.handle({'op': 'stop', 'id': 'test'})
                    spans = [(s['route'], s['start'], s['end']) for s in result['segments']]
                    expected = [(0, 0, cut), (0, cut, 160000)]
                    if operation == 'route':
                        expected.append((1, 160000, 163200))
                    self.assertEqual(spans, expected)
                finally:
                    engine.clear()

    def test_copy_waits_for_inflight_final_after_speech_ends(self):
        class Blocked(Session):
            def __init__(self):
                super().__init__()
                self.release = threading.Event()

            def run(self, audio):
                self.called.set()
                if not self.release.wait(2):
                    raise RuntimeError('Test decode timed out')
                return SimpleNamespace(text='全部完成。')

        session = Blocked()
        engine = Recognition(session, Vad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine, .2)
            engine.handle({'op': 'route', 'id': 'test', 'route': 1})
            self.assertTrue(session.called.wait(1))
            result = engine.snapshot()
            self.assertFalse(result['speaking'])
            self.assertTrue(result['transcribing'])
            session.release.set()
            result = self.wait_for(engine, 'transcribing', False)
            self.assertEqual(result['text'], '全部完成。')
        finally:
            session.release.set()
            engine.clear()

    def test_listening_indicator_uses_speech_not_boosted_noise(self):
        stream = Recognition(None, Vad())
        stream.level = .35
        self.assertIs(stream.snapshot()['speaking'], False)
        stream.vad.active = True
        self.assertIs(stream.snapshot()['speaking'], True)
        stream.vad.active = False
        self.assertIs(stream.snapshot()['speaking'], False)

    def feed(self, engine, seconds=1):
        pcm = np.full(int(16000 * seconds), 100, dtype='<i2').tobytes()
        return engine.handle({'op': 'audio', 'id': 'test', 'pcm': base64.b64encode(pcm).decode()})

    def wait_for(self, engine, key, value):
        limit = time.monotonic() + 2
        while time.monotonic() < limit:
            result = engine.handle({'op': 'poll', 'id': 'test'})
            if result[key] == value:
                return result
            time.sleep(.005)
        self.fail((key, value, result))

    def test_packet_batching_does_not_change_vad_frame_sizes(self):
        class TracedVad(Vad):
            def __init__(self):
                super().__init__()
                self.frames = []

            def accept_waveform(self, audio):
                self.frames.append(len(audio))
                super().accept_waveform(audio)

        vad = TracedVad()
        engine = Recognition(Session(), vad)
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine, .65)
            engine.handle({'op': 'route', 'id': 'test', 'route': 1})
            self.assertEqual(vad.frames, [1600] * 6 + [800])
            self.assertEqual(engine.samples, 10400)
        finally:
            engine.clear()

    def test_preview_can_change_without_freezing_committed_sentences(self):
        session = Session()
        engine = Recognition(session, Vad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine)
            result = self.wait_for(engine, 'preview', '暂时识别')
            self.assertEqual(result['text'], '')
            self.assertEqual(result['preview_segment'], {'route': 0, 'start': 0, 'text': '暂时识别'})
            session.text = '最终识别。'
            final = engine.handle({'op': 'stop', 'id': 'test'})
            self.assertEqual(final['text'], '最终识别。')
            self.assertIsNone(final['preview_segment'])
            self.assertEqual(final['samples'], 16000)
            self.assertIsNone(engine.worker)
            engine.handle({'op': 'start', 'id': 'test'})
            session.text = '继续说话。'
            self.feed(engine)
            self.assertEqual(engine.handle({'op': 'stop', 'id': 'test'})['text'], '继续说话。')
        finally:
            engine.clear()

    def test_audio_after_25_seconds_is_still_recognized(self):
        session = Session()
        engine = Recognition(session, Vad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            for _ in range(26):
                self.feed(engine)
            self.wait_for(engine, 'preview', session.text)
            session.text = '二十五秒后仍在识别'
            self.feed(engine)
            self.wait_for(engine, 'preview', session.text)
            self.assertEqual(engine.handle({'op': 'stop', 'id': 'test'})['text'], session.text)
        finally:
            engine.clear()

    def test_preview_identity_clears_on_route_change_and_final_keeps_punctuation(self):
        session = Session()
        session.punctuate = lambda text: text + '。'
        engine = Recognition(session, Vad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine)
            self.wait_for(engine, 'preview', session.text)
            routed = engine.handle({'op': 'route', 'id': 'test', 'route': 1})
            self.assertIsNone(routed['preview_segment'])
            session.text = '新的话'
            self.feed(engine)
            result = self.wait_for(engine, 'preview', session.text)
            self.assertEqual(result['preview_segment'], {'route': 1, 'start': 16000, 'text': session.text})
            final = engine.handle({'op': 'stop', 'id': 'test'})
            self.assertEqual(final['segments'][-1]['text'], '新的话。')
        finally:
            engine.clear()

    def test_initial_silence_keeps_preview_identity_when_stop_expands_audio(self):
        class LateVad(Vad):
            @property
            def current_segment(self):
                return SimpleNamespace(start=32000, samples=self.audio[32000:])

        engine = Recognition(Session(), LateVad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            for _ in range(3):
                self.feed(engine)
            preview = self.wait_for(engine, 'preview', '暂时识别')['preview_segment']
            final = engine.handle({'op': 'stop', 'id': 'test'})['segments'][0]
            self.assertEqual(final['start'], 0)
            self.assertEqual(final['preview_start'], preview['start'])
            self.assertNotEqual(final['start'], preview['start'])
        finally:
            engine.clear()

    def test_delayed_old_audio_keeps_its_route_after_focus_returns(self):
        class Delayed:
            entered = threading.Event()
            release = threading.Event()

            def run(self, audio):
                if len(audio) == 3200:
                    self.entered.set()
                    if not self.release.wait(2):
                        raise RuntimeError('test decode timeout')
                    return SimpleNamespace(text='离开输入框时的话')
                return SimpleNamespace(text='回来之后的新话')

        session = Delayed()
        engine = Recognition(session, Vad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine, .2)
            engine.handle({'op': 'route', 'id': 'test', 'route': 1})
            self.assertTrue(session.entered.wait(1))
            self.feed(engine, .3)
            session.release.set()
            result = engine.handle({'op': 'stop', 'id': 'test'})
            self.assertEqual([(x['route'], x['start'], x['end']) for x in result['segments']],
                             [(0, 0, 3200), (1, 3200, 8000)])
            self.assertEqual(result['segments'][0]['text'], '离开输入框时的话')
            self.assertEqual(result['segments'][1]['text'], '回来之后的新话')
        finally:
            session.release.set()
            engine.clear()

    def test_worker_failure_is_reported_and_next_session_can_start(self):
        class Broken:
            def run(self, audio):
                raise RuntimeError('decoder failed')
        engine = Recognition(Broken(), Vad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine)
            deadline = time.monotonic() + 2
            while not engine.error and time.monotonic() < deadline:
                time.sleep(.005)
            with self.assertRaisesRegex(RuntimeError, 'decoder failed'):
                engine.handle({'op': 'poll', 'id': 'test'})
        finally:
            engine.clear()
        engine.session = Session()
        engine.handle({'op': 'start', 'id': 'test'})
        self.feed(engine)
        self.assertTrue(engine.handle({'op': 'stop', 'id': 'test'})['text'])

    def test_gain_and_word_boundaries(self):
        self.assertFalse(QuietBoost().process(np.zeros(1600, dtype=np.float32)).any())
        quiet = np.full(1600, .0001, dtype=np.float32)
        self.assertGreater(float(QuietBoost().process(quiet).mean()), .001)
        loud = np.array([-.9, .9], dtype=np.float32)
        self.assertLessEqual(float(abs(QuietBoost().process(loud)).max()), .95)
        self.assertEqual(join_text('hello', 'world'), 'hello world')
        self.assertEqual(join_text('你好，', '世界。'), '你好，世界。')
        self.assertEqual(join_text('Python。', '继续'), 'Python。继续')

    def test_punctuation_cannot_change_numbers_negation_or_word_boundaries(self):
        self.assertTrue(punctuation_only('金额是一点五元', '金额是一点五元。'))
        self.assertFalse(punctuation_only('金额1.5元', '金额15元。'))
        self.assertFalse(punctuation_only('没有到账', '有到账。'))
        self.assertFalse(punctuation_only('use GitHub', 'use Git Hub.'))

    def test_punctuation_preserves_existing_punctuation(self):
        for original, rewritten in [('不，删除文件', '不删除文件。'),
                                     ('使用.NET', '使用NET。'),
                                     ('你好！', '你好。')]:
            with self.subTest(original=original):
                self.assertFalse(punctuation_only(original, rewritten))
                session = SimpleNamespace(punctuation=SimpleNamespace(
                    add_punctuation=lambda text: rewritten))
                self.assertEqual(SenseVoiceSession.punctuate(session, original), original)
        self.assertTrue(punctuation_only('你好，世界', '你好，世界。'))

    def test_asr_preserves_relative_volume_inside_a_segment(self):
        class Spy(Session):
            def run(self, audio):
                self.audio = audio.copy()
                return super().run(audio)
        session = Spy()
        engine = Recognition(session, Vad())
        engine.handle({'op': 'start', 'id': 'test'})
        raw = np.concatenate((np.full(1600, 33), np.full(14400, 3277))).astype('<i2')
        engine.handle({'op': 'audio', 'id': 'test', 'pcm': base64.b64encode(raw.tobytes()).decode()})
        engine.handle({'op': 'stop', 'id': 'test'})
        self.assertTrue(np.allclose(session.audio, raw.astype(np.float32) / 32768))


if __name__ == '__main__':
    unittest.main()
