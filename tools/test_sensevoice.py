"""Deterministic production-pipeline tests; no microphone or desktop input."""
import base64
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'app'))
from recognize import (Dictionary, Recognition, QuietBoost, SenseVoiceSession,
                       apply_dictionary, drop_inserted_punctuation, has_chinese, join_text,
                       punctuation_only)


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
    def test_stable_session_skips_preview_decode_and_keeps_final(self):
        session = Session()
        engine = Recognition(session, Vad())
        try:
            for invalid in ('false', 0, None):
                with self.assertRaisesRegex(ValueError, '草稿选项'):
                    engine.handle({'op': 'start', 'id': 'test', 'preview': invalid})
            for invalid in (-1, True, 1.5, '0', 2**53):
                with self.assertRaisesRegex(ValueError, '录音位置'):
                    engine.handle({'op': 'start', 'id': 'test', 'sample_offset': invalid})
            engine.handle({'op': 'start', 'id': 'test', 'preview': False})
            self.feed(engine)
            self.assertFalse(session.called.wait(.05), 'stable mode must not decode unused drafts')
            self.assertEqual(engine.snapshot()['preview'], '')
            session.text = '金额1.5元，没有到账。'
            self.assertEqual(engine.handle({'op': 'stop', 'id': 'test'})['text'], session.text)
            session.called.clear()
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine)
            self.assertTrue(session.called.wait(1), 'legacy preview protocol must still work')
        finally:
            engine.clear()

    def test_four_second_forced_endpoint_preserves_carry_on_stop(self):
        class FourSecondVad(Vad):
            def accept_waveform(self, audio):
                super().accept_waveform(audio)
                if len(self.audio) == 64000:
                    self.completed.append(self.current_segment)
                    self.active = False
        engine = Recognition(Session(), FourSecondVad())
        try:
            engine.handle({'op': 'start', 'id': 'test', 'preview': False})
            for _ in range(4):
                self.feed(engine)
            cut = engine.last_end
            self.assertTrue(engine.carry)
            self.assertLess(cut, 64000)
            final = engine.handle({'op': 'stop', 'id': 'test'})
            self.assertEqual([(s['start'], s['end']) for s in final['segments']],
                             [(0, cut), (cut, 64000)])
        finally:
            engine.clear()

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

    def test_dictionary_rewrites_phrases_with_word_edges_and_context(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'dictionary.json'
            path.write_text(json.dumps({'rules': [
                {'to': 'GitHub', 'from': ['get up', 'git up'], 'context': 'zh'},
                {'to': 'JSON', 'from': ['js on'], 'context': 'zh'},
                {'to': 'PDF', 'from': ['p d f']},
            ]}, ensure_ascii=False), encoding='utf-8')
            words = Dictionary(path)
            rules = words.current()
            self.assertEqual(apply_dictionary('我说一下 get up 的用法', rules),
                             '我说一下 GitHub 的用法')
            self.assertEqual(apply_dictionary('说 Get Up 的人', rules), '说 GitHub 的人')
            self.assertEqual(apply_dictionary('git up 地址发你', rules), 'GitHub 地址发你')
            # "zh" rules leave pure-English dictation untouched.
            self.assertEqual(apply_dictionary('I get up at seven', rules), 'I get up at seven')
            # Mixed utterances decide "zh" per utterance: a segment without any
            # Chinese of its own still gets the rule when the rest of it had some.
            self.assertFalse(has_chinese('js on。'))
            self.assertTrue(has_chinese('这个API。返回的是。'))
            self.assertEqual(apply_dictionary('js on', rules), 'js on')
            self.assertEqual(apply_dictionary('js on', rules,
                                              force=has_chinese('这个API。返回的是。')), 'JSON')
            # Rules without "context" always apply, even in English text.
            self.assertEqual(apply_dictionary('export the p d f file', rules),
                             'export the PDF file')
            # Word edges: "get up" inside "forget upper" must not be rewritten.
            self.assertEqual(apply_dictionary('forget upper case', rules), 'forget upper case')

    def test_dictionary_watches_edits_and_survives_broken_files(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'dictionary.json'
            words = Dictionary(path)
            self.assertEqual(words.current(), ())
            path.write_text('{"rules": [{"to": "GitHub", "from": ["get up"]}]}', encoding='utf-8')
            os.utime(path, ns=(1_000_000, 1_000_000))
            self.assertEqual(apply_dictionary('get up', words.current()), 'GitHub')
            path.write_text('{broken json', encoding='utf-8')
            os.utime(path, ns=(2_000_000, 2_000_000))
            self.assertEqual(words.current(), ())
            self.assertEqual(apply_dictionary('get up', ()), 'get up')
            path.write_text('{"rules": [{"to": "GitHub", "from": ["git up"]}]}', encoding='utf-8')
            os.utime(path, ns=(3_000_000, 3_000_000))
            self.assertEqual(apply_dictionary('git up', words.current()), 'GitHub')
            path.unlink()
            self.assertEqual(words.current(), ())
            # Invalid entries are skipped instead of breaking the pipeline.
            path.write_text(json.dumps({'rules': [
                'junk', 7, {'to': ''}, {'from': ['x']}, {'to': 'Ok', 'from': ['ok!', '']}
            ]}), encoding='utf-8')
            os.utime(path, ns=(4_000_000, 4_000_000))
            self.assertEqual(apply_dictionary('it is ok! now', words.current()),
                             'it is Ok now')

    def test_dictionary_applies_to_preview_and_final_through_the_pipeline(self):
        session = Session()
        engine = Recognition(session, Vad())
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'dictionary.json'
            path.write_text('{"rules": [{"to": "GitHub", "from": ["get up"], "context": "zh"}]}',
                            encoding='utf-8')
            engine.dictionary = Dictionary(path)
            try:
                session.text = '把 get up 链接发我'
                engine.handle({'op': 'start', 'id': 'test'})
                self.feed(engine)
                self.wait_for(engine, 'preview', '把 GitHub 链接发我')
                session.text = 'I get up early'
                final = engine.handle({'op': 'stop', 'id': 'test'})
                self.assertEqual(final['text'], 'I get up early')
                self.assertEqual(final['segments'][-1]['text'], 'I get up early')
            finally:
                engine.clear()

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


class PunctuationRegressionTests(unittest.TestCase):
    """The punctuation model must not stack a second mark onto an existing one."""

    # These model outputs are verbatim from the shipped INT8 CT-Transformer:
    # it emits a mark at every position, so an already punctuated transcript
    # gains a neighbour ("。。", "，，", "，。").
    STACKED = [
        ('你好。', '你好。。', '你好。'),
        ('好的。', '好的，。。', '好的。'),
        ('你好，世界。', '你好，，世界。', '你好，世界。'),
        ('我们明天，如果天气好，就去公园', '我们明天，，如果天气好，就去公园。',
         '我们明天，如果天气好，就去公园。'),
    ]
    ADDED = [
        ('你好', '你好。', '你好。'),
        ('你好，世界', '你好，世界。', '你好，世界。'),
        ('我们明天如果天气好就去公园', '我们明天，如果天气好，就去公园。',
         '我们明天，如果天气好，就去公园。'),
    ]
    KEPT = [
        # Deliberate punctuation stays as the speaker wrote it.
        ('他说“好。”', '他说“好。”。', '他说“好。”。'),
        ('真的？！', '真的？！', '真的？！'),
        ('金额1.5元', '金额1.5元。', '金额1.5元。'),
    ]

    def punctuated(self, output):
        return SimpleNamespace(punctuation=SimpleNamespace(add_punctuation=lambda text: output))

    def test_marks_stacked_on_existing_punctuation_are_dropped(self):
        for original, output, expected in self.STACKED:
            with self.subTest(original=original):
                self.assertEqual(drop_inserted_punctuation(original, output), expected)
                self.assertEqual(SenseVoiceSession.punctuate(self.punctuated(output), original),
                                 expected)
                # Dropping the model's extra mark must leave every original
                # character — including the punctuation — in place.
                self.assertTrue(punctuation_only(original, expected))

    def test_marks_added_where_nothing_was_marked_survive(self):
        for original, output, expected in self.ADDED:
            with self.subTest(original=original):
                self.assertEqual(drop_inserted_punctuation(original, output), expected)
                self.assertEqual(SenseVoiceSession.punctuate(self.punctuated(output), original),
                                 expected)

    def test_dedupe_never_removes_the_speakers_own_punctuation(self):
        for original, output, expected in self.KEPT:
            with self.subTest(original=original):
                self.assertEqual(drop_inserted_punctuation(original, output), expected)
                self.assertEqual(SenseVoiceSession.punctuate(self.punctuated(output), original),
                                 expected)

    def test_model_output_that_rewrites_words_is_still_rejected(self):
        session = self.punctuated('使用NET。')
        self.assertEqual(SenseVoiceSession.punctuate(session, '使用.NET'), '使用.NET')

    def test_rejected_and_failed_model_keep_the_transcript(self):
        class Broken:
            def add_punctuation(self, text):
                raise RuntimeError('no model')
        self.assertEqual(SenseVoiceSession.punctuate(SimpleNamespace(punctuation=Broken()), '原文'),
                         '原文')


class PunctuationPipelineTests(unittest.TestCase):
    """Draft, revision and segment boundaries must not repeat punctuation."""

    class Fixed(Session):
        def __init__(self):
            super().__init__()
            self.punctuation = SimpleNamespace(add_punctuation=lambda text: text + '。')

        punctuate = SenseVoiceSession.punctuate

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

    def test_final_after_punctuated_draft_keeps_a_single_mark(self):
        session = self.Fixed()
        session.text = '第一句。'
        engine = Recognition(session, Vad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine)
            self.assertEqual(self.wait_for(engine, 'preview', '第一句。')['preview_segment'],
                             {'route': 0, 'start': 0, 'text': '第一句。'})
            final = engine.handle({'op': 'stop', 'id': 'test'})
            self.assertEqual(final['text'], '第一句。')
            self.assertEqual(final['segments'][-1]['text'], '第一句。')
        finally:
            engine.clear()

    def test_final_completes_an_unpunctuated_draft_exactly_once(self):
        session = self.Fixed()
        session.text = '第二句'
        engine = Recognition(session, Vad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine)
            self.wait_for(engine, 'preview', '第二句')
            final = engine.handle({'op': 'stop', 'id': 'test'})
            self.assertEqual(final['text'], '第二句。')
            self.assertEqual(final['segments'][-1]['text'], '第二句。')
        finally:
            engine.clear()

    def test_segment_boundary_and_earlier_segment_stay_clean(self):
        session = self.Fixed()
        session.text = '第一句。'
        engine = Recognition(session, Vad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine)
            self.wait_for(engine, 'preview', '第一句。')
            # Leaving and re-entering the input box closes the first segment.
            engine.handle({'op': 'route', 'id': 'test', 'route': 1})
            self.wait_for(engine, 'text', '第一句。')
            session.text = '第二句。'
            self.feed(engine)
            self.wait_for(engine, 'preview', '第二句。')
            final = engine.handle({'op': 'stop', 'id': 'test'})
            self.assertEqual(final['text'], '第一句。第二句。')
            self.assertNotIn('。。', final['text'])
            self.assertNotIn('，，', final['text'])
            self.assertEqual(final['segments'][0]['text'], '第一句。')
            self.assertEqual(final['segments'][-1]['text'], '第二句。')
        finally:
            engine.clear()

    def test_repeated_drafts_revise_without_stacking_marks(self):
        class Growing(Session):
            def __init__(self):
                super().__init__()
                self.punctuation = SimpleNamespace(add_punctuation=lambda text: text + '。')

            punctuate = SenseVoiceSession.punctuate

            def run(self, audio):
                # Same segment, longer audio: the draft is replaced, not queued.
                self.text = '持续说话' if len(audio) < 24000 else '持续说话，还没说完'
                return SimpleNamespace(text=self.text)

        engine = Recognition(Growing(), Vad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine)
            self.wait_for(engine, 'preview', '持续说话')
            self.feed(engine)
            self.wait_for(engine, 'preview', '持续说话，还没说完')
            final = engine.handle({'op': 'stop', 'id': 'test'})
            self.assertEqual(final['text'], '持续说话，还没说完。')
            self.assertNotIn('。。', final['text'])
        finally:
            engine.clear()


if __name__ == '__main__':
    unittest.main()
