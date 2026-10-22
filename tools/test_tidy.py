"""Dictionary-driven tidy: repeated attempts collapse into one word; no model needed."""
import re
import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'app'))
from recognize import Dictionary, apply_dictionary, tidy_text

ROOT = Path(__file__).resolve().parent.parent
CODEX = ((re.compile(r'(?<![A-Za-z0-9_])col death(?![A-Za-z0-9_])|(?<![A-Za-z0-9_])koex(?![A-Za-z0-9_])'
                     r'|(?<![A-Za-z0-9_])colexs(?![A-Za-z0-9_])|(?<![A-Za-z0-9_])cols(?![A-Za-z0-9_])'
                     r'|(?<![A-Za-z0-9_])kos(?![A-Za-z0-9_])', re.IGNORECASE), 'Codex', True),)


class TidyTests(unittest.TestCase):
    def test_repeated_mishearings_collapse_to_one_word(self):
        heard = 'col death。koex。koex。colexs。cols。koex。koex。kos。koex。col death。cols。'
        text, dropped = tidy_text(heard, CODEX)
        self.assertEqual(text, 'Codex')
        self.assertEqual(dropped, 10)

    def test_zh_context_rules_apply_to_pure_english_text(self):
        # The zh guard exists for live dictation; tidy is a manual action.
        self.assertEqual(apply_dictionary('col death', CODEX), 'col death')
        self.assertEqual(tidy_text('col death', CODEX)[0], 'Codex')

    def test_duplicate_sentences_collapse_and_spacing_is_tidied(self):
        text, dropped = tidy_text('今天天气很好。今天天气很好。', ())
        self.assertEqual((text, dropped), ('今天天气很好', 1))
        self.assertEqual(tidy_text('  hello   world  ', ())[0], 'hello world')
        # Two different sentences keep their break instead of merging into one run.
        self.assertEqual(tidy_text('hello world. hello there', ())[0], 'hello world. hello there')

    def test_no_punctuation_is_needed(self):
        heard = 'col death koex koex colexs cols kos'
        self.assertEqual(tidy_text(heard, CODEX), ('Codex', 5))
        self.assertEqual(tidy_text('koex,koex,koex', CODEX), ('Codex', 2))
        self.assertEqual(tidy_text('koex   koex   koex', CODEX), ('Codex', 2))

    def test_words_outside_the_list_are_only_deduplicated(self):
        text, dropped = tidy_text('这个 api 返回 json 数据。api 又错了。', ())
        self.assertEqual(text, '这个 api 返回 json 数据。api 又错了')
        self.assertEqual(dropped, 0)

    def test_distinct_sentences_survive_in_order(self):
        text, dropped = tidy_text('把 col death 的配置发我。koex 装好了吗。', CODEX)
        self.assertEqual(text, '把 Codex 的配置发我。Codex 装好了吗')
        self.assertEqual(dropped, 0)

    def test_empty_input_is_empty_output(self):
        self.assertEqual(tidy_text('', CODEX), ('', 0))
        self.assertEqual(tidy_text('。；。', CODEX), ('', 0))

    def test_repository_dictionary_covers_the_reported_case(self):
        rules = Dictionary(ROOT / 'dictionary.json').current()
        text, dropped = tidy_text('col death。koex。koex。colexs。cols。koex。koex。kos。koex。col death。cols。', rules)
        self.assertEqual(text, 'Codex')
        self.assertGreaterEqual(dropped, 9)


if __name__ == '__main__':
    unittest.main()
