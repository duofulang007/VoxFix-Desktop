"""Language safety for the SenseVoice pipeline; no model or audio device."""
import unittest

import test_sensevoice as fixtures
from recognize import Recognition, outside_product_scripts


class LanguageTests(unittest.TestCase):
    feed = fixtures.PipelineTests.feed
    def test_character_gate(self):
        for text in ['今天天气很好。', 'I use Blender and GitHub.', '25°C 的咖啡']:
            self.assertFalse(outside_product_scripts(text), text)
        for text in ['पल कि हाप', 'こんにちは', 'สวัสดี', 'مرحبا']:
            self.assertTrue(outside_product_scripts(text), text)

    def test_foreign_final_is_reported_and_prior_text_can_be_recovered(self):
        session = fixtures.Session()
        engine = Recognition(session, fixtures.Vad())
        try:
            engine.handle({'op': 'start', 'id': 'test'})
            engine.committed = '前一句。'
            session.text = 'पल कि हाप'
            self.feed(engine)
            with self.assertRaisesRegex(RuntimeError, '中文和英文之外'):
                engine.handle({'op': 'stop', 'id': 'test'})
            self.assertEqual(engine.committed, '前一句。')
        finally:
            engine.clear()


if __name__ == '__main__':
    unittest.main()
