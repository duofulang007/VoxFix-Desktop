"""Hotword rescue: triggers, guards and pipeline wiring; no model or audio device."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'app'))
import hotwords
from hotwords import HotwordRescue, compact, hotword_hits, near_miss, strong_hits
import test_sensevoice as fixtures
from recognize import Dictionary, Recognition, load_hotwords


class FakeDecoder:
    """Stands in for the ONNX decoder: returns whatever text the test asks for."""

    def __init__(self, text):
        self.text = text
        self.calls = 0
        self.pieces = {}
        self.closed = False

    def greedy(self, samples, trie=None):
        self.calls += 1
        return self.text

    def close(self):
        self.closed = True


class TriggerTests(unittest.TestCase):
    def test_compact_drops_everything_but_lowercase_alnum(self):
        self.assertEqual(compact('这个app返回的是J SON数据'), 'appjson')
        self.assertEqual(compact('请把代码提交到'), '')

    def test_near_miss_accepts_spelling_and_spacing_variants(self):
        for text, word in [('我今天用pyython写了一个脚本', 'Python'),
                           ('所以我上学期也学了pyython', 'Python'),
                           ('请把代码提交到gitthub', 'GitHub'),
                           ('返回的是 J SON 数据', 'JSON'),
                           ('这个app返回的是数据', 'API'),
                           ('a', 'API')]:
            self.assertIn(word, near_miss(text, [word]), text)

    def test_near_miss_ignores_correct_and_unrelated_text(self):
        for text, word in [('这个app返回的是JSON数据', 'JSON'),
                           ('请把代码提交到 gitHub', 'GitHub'),
                           ('填 pdf 格式', 'PDF'),
                           ('桌子上那杯水是我的', 'GitHub'),
                           ('今天天气很好', 'Python'),
                           ('I use Kotlin and Python', 'Kotlin'),
                           ('I build an android application', 'API'),
                           ('the main for engine system', 'Android'),
                           ('I use a lot of tools', 'API')]:
            self.assertEqual(near_miss(text, [word]), [], text)

    def test_hotword_hits_counts_words_present(self):
        self.assertEqual(hotword_hits('这个app返回的是JSON数据', ['JSON', 'API']), 1)
        self.assertEqual(hotword_hits('把文件保存成PDF格式', ['PDF', 'GitHub']), 1)
        self.assertEqual(hotword_hits('普通句子', ['PDF']), 0)

    def test_strong_hits_requires_the_word_spelled_out(self):
        # "J SON" compaction equals JSON, but only one of them is spelled out.
        self.assertEqual(strong_hits('返回的是 J SON 数据', ['JSON']), 0)
        self.assertEqual(strong_hits('返回的是 JSON 数据', ['JSON']), 1)
        self.assertEqual(hotword_hits('返回的是 J SON 数据', ['JSON']), 1)


class RescueTests(unittest.TestCase):
    def setUp(self):
        self.original = hotwords.Decoder
        self.audio = np.ones(16000, dtype=np.float32) * 0.01

    def tearDown(self):
        hotwords.Decoder = self.original

    def test_no_words_and_no_trigger_create_no_decoder(self):
        created = []

        def factory():
            created.append(1)
            return FakeDecoder('unused')

        hotwords.Decoder = factory
        rescue = HotwordRescue()
        self.assertEqual(rescue.rescore(self.audio, '我今天用pyython写了一个脚本', []),
                         '我今天用pyython写了一个脚本')
        self.assertEqual(rescue.rescore(self.audio, '桌子上那杯水是我的', ['GitHub']),
                         '桌子上那杯水是我的')
        self.assertEqual(created, [])

    def test_near_miss_with_improvement_is_accepted(self):
        fake = FakeDecoder('请把代码提交到github')
        hotwords.Decoder = lambda: fake
        rescue = HotwordRescue()
        self.assertEqual(rescue.rescore(self.audio, '请把代码提交到gitthub', ['GitHub']),
                         '请把代码提交到github')
        self.assertEqual(fake.calls, 1)
        self.assertEqual(rescue.stats['rescued'], 1)

    def test_no_gain_keeps_the_original_text(self):
        hotwords.Decoder = lambda: FakeDecoder('请把代码提交到gitthub')
        rescue = HotwordRescue()
        self.assertEqual(rescue.rescore(self.audio, '请把代码提交到gitthub', ['GitHub']),
                         '请把代码提交到gitthub')
        self.assertEqual(rescue.stats['rescued'], 0)

    def test_spacing_variant_is_accepted_even_though_compaction_matches(self):
        # "J SON" already compacts to "json", so the old guard refused the fix.
        hotwords.Decoder = lambda: FakeDecoder('这个app返回的是JSON数据')
        rescue = HotwordRescue()
        self.assertEqual(rescue.rescore(self.audio, '这个app返回的是J SON数据', ['JSON']),
                         '这个app返回的是JSON数据')
        self.assertEqual(rescue.stats['rescued'], 1)

    def test_truncated_or_failing_decode_keeps_the_original_text(self):
        hotwords.Decoder = lambda: FakeDecoder('github')
        rescue = HotwordRescue()
        self.assertEqual(rescue.rescore(self.audio, '请把很长的一句话提交到gitthub上面去',
                                        ['GitHub']),
                         '请把很长的一句话提交到gitthub上面去')

        def boom():
            raise RuntimeError('model missing')

        hotwords.Decoder = boom
        rescue = HotwordRescue()
        self.assertEqual(rescue.rescore(self.audio, '请把代码提交到gitthub', ['GitHub']),
                         '请把代码提交到gitthub')
        self.assertEqual(rescue.stats['failed'], 1)

    def test_release_drops_the_session_and_rebuilds_it_on_demand(self):
        fake = FakeDecoder('请把代码提交到github')
        hotwords.Decoder = lambda: fake
        rescue = HotwordRescue()
        rescue.rescore(self.audio, '请把代码提交到gitthub', ['GitHub'])
        self.assertIs(rescue.decoder, fake)
        rescue.release()
        self.assertIsNone(rescue.decoder)
        self.assertIsNone(rescue.trie)
        self.assertTrue(fake.closed)
        hotwords.Decoder = lambda: FakeDecoder('请把代码提交到github')
        self.assertEqual(rescue.rescore(self.audio, '请把代码提交到gitthub', ['GitHub']),
                         '请把代码提交到github')

    def test_explicit_release_cancels_the_idle_timer(self):
        fake = FakeDecoder('请把代码提交到github')
        hotwords.Decoder = lambda: fake
        rescue = HotwordRescue()
        rescue.rescore(self.audio, '请把代码提交到gitthub', ['GitHub'])
        timer = rescue.timer
        try:
            rescue.release()
            timer.join(1)
            self.assertFalse(timer.is_alive(), 'discarded evaluation clips must not retain a 180-second timer')
            self.assertIsNone(rescue.timer)
            self.assertTrue(fake.closed)
        finally:
            timer.cancel()

    def test_decode_failure_closes_a_created_session(self):
        fake = FakeDecoder('unused')
        hotwords.Decoder = lambda: fake
        rescue = HotwordRescue()
        with patch.object(fake, 'greedy', side_effect=RuntimeError('inference failure')):
            text = '请把代码提交到gitthub'
            self.assertEqual(rescue.rescore(self.audio, text, ['GitHub']), text)
        self.assertTrue(fake.closed)
        self.assertIsNone(rescue.decoder)
        self.assertIsNone(rescue.timer)


class WiringTests(unittest.TestCase):
    feed = fixtures.PipelineTests.feed
    wait_for = fixtures.PipelineTests.wait_for

    def test_hotwords_field_is_optional_and_sane(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'dictionary.json'
            self.assertEqual(load_hotwords(path), ())
            path.write_text('{"rules": []}', encoding='utf-8')
            self.assertEqual(load_hotwords(path), ())
            path.write_text('{"hotwords": ["GitHub", 7, "", "  API  "]}', encoding='utf-8')
            self.assertEqual(load_hotwords(path), ('GitHub', 'API'))
            path.write_text('{broken', encoding='utf-8')
            self.assertEqual(load_hotwords(path), ())
            words = Dictionary(path)
            self.assertEqual(words.hotwords(), ())

    def test_final_text_is_rescued_but_preview_is_not(self):
        session = fixtures.Session()
        engine = Recognition(session, fixtures.Vad())
        original = hotwords.Decoder
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'dictionary.json'
            path.write_text(json.dumps({'hotwords': ['GitHub']}), encoding='utf-8')
            engine.dictionary = Dictionary(path)
            engine.rescue = HotwordRescue()
            fake = FakeDecoder('请把代码提交到github')
            hotwords.Decoder = lambda: fake
            try:
                session.text = '请把代码提交到gitthub'
                engine.handle({'op': 'start', 'id': 'test'})
                self.feed(engine)
                self.wait_for(engine, 'preview', '请把代码提交到gitthub')
                self.assertEqual(fake.calls, 0, 'previews must not pay for a rescue')
                final = engine.handle({'op': 'stop', 'id': 'test'})
                self.assertEqual(final['text'], '请把代码提交到github')
                self.assertEqual(final['segments'][-1]['text'], '请把代码提交到github')
            finally:
                hotwords.Decoder = original
                engine.clear()

    def test_pipeline_without_hotwords_never_touches_the_decoder(self):
        session = fixtures.Session()

        def boom():
            raise AssertionError('rescue must stay dormant')

        hotwords.Decoder = boom
        original = hotwords.Decoder
        try:
            engine = Recognition(session, fixtures.Vad())
            # No dictionary spelling in this sentence: the assertion is about the
            # rescue staying dormant, not about post-decode substitution.
            session.text = '请把代码提交到仓库'
            engine.handle({'op': 'start', 'id': 'test'})
            self.feed(engine)
            final = engine.handle({'op': 'stop', 'id': 'test'})
            self.assertEqual(final['text'], '请把代码提交到仓库')
        finally:
            hotwords.Decoder = original
            engine.clear()


if __name__ == '__main__':
    unittest.main()
