"""Text-only dictionary and preview checks; no audio, model or desktop."""
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'app'))
from dictionary_rules import (HOTWORD_LIMIT, apply_dictionary, compile_dictionary,
                              load_dictionary, load_hotwords, preview)


class DictionaryTests(unittest.TestCase):
    def test_english_chinese_and_mixed_with_legacy_language_guard(self):
        data = {'rules': [{'to': 'JSON', 'from': ['js on']},
                          {'to': 'GitHub', 'from': ['get up'], 'context': 'zh'},
                          {'to': '梯度下降', 'from': ['梯度下将']}]}
        for source, expected in [('js on', 'JSON'), ('use js on', 'use JSON'),
                                 ('用js on', '用JSON'), ('梯度下将', '梯度下降'),
                                 ('I get up at seven', 'I get up at seven')]:
            self.assertEqual(preview(data, source)['text'], expected)
        blocked = preview(data, 'get up')['hits']
        self.assertEqual(blocked, [{'word': 'GitHub', 'from': 'get up', 'applied': False}])
        rules = compile_dictionary(data)
        self.assertEqual(apply_dictionary('get up', rules, force=True), 'GitHub')

    def test_longest_spelling_wins_without_changing_stored_order(self):
        data = {'rules': [{'to': 'API', 'from': ['api', 'api i']}]}
        self.assertEqual(preview(data, 'use api i now')['text'], 'use API now')
        self.assertEqual(data['rules'][0]['from'], ['api', 'api i'])

    def test_edges_symbols_case_literal_replacement_and_chain(self):
        data = {'rules': [{'to': 'JSON', 'from': ['js on']},
                          {'to': '.NET', 'from': ['dot net']},
                          {'to': 'C++', 'from': ['c plus plus']}]}
        self.assertEqual(preview(data, 'JS ON dot net c plus plus')['text'], 'JSON .NET C++')
        self.assertEqual(preview(data, 'xjs onyx')['text'], 'xjs onyx')
        self.assertEqual(preview({'rules': [{'to': r'\1', 'from': ['old']}]}, 'old')['text'], r'\1')
        chain = {'rules': [{'to': 'First', 'from': ['old']},
                           {'to': 'Last', 'from': ['First']}]}
        self.assertEqual(preview(chain, 'old')['text'], 'Last')

    def test_malformed_file_and_hotword_limit_preserve_tail_rules(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'dictionary.json'
            path.write_text('{broken', encoding='utf-8')
            self.assertEqual(load_dictionary(path), ())
            self.assertEqual(load_hotwords(path), ())
            data = {'hotwords': ['Word' + str(i) for i in range(80)],
                    'rules': [{'to': 'Tail', 'from': ['tail typo']}]}
            path.write_text(json.dumps(data), encoding='utf-8-sig')
            self.assertEqual(len(load_hotwords(path)), HOTWORD_LIMIT)
            self.assertEqual(load_hotwords(path)[-1], 'Word63')
            self.assertEqual(apply_dictionary('tail typo', load_dictionary(path)), 'Tail')
            self.assertEqual(len(json.loads(path.read_text(encoding='utf-8-sig'))['hotwords']), 80)

    def test_preview_process_is_utf8_read_only_and_uses_production_function(self):
        data = {'rules': [{'to': 'JSON', 'from': ['js on']}]}
        request = {'dictionary': data, 'text': '这个js on'}
        child = subprocess.run([sys.executable, str(ROOT / 'app/dictionary_rules.py')],
                               input=json.dumps(request), text=True, encoding='utf-8',
                               capture_output=True, timeout=3, check=True)
        self.assertEqual(json.loads(child.stdout), preview(data, request['text']))
        from recognize import apply_dictionary as production
        self.assertIs(production, apply_dictionary)

    def test_native_limit_matches_python(self):
        source = (ROOT / 'app/Microphone.cs').read_text(encoding='utf-8-sig')
        match = re.search(r'const int HotwordLimit = (\d+);', source)
        self.assertIsNotNone(match)
        self.assertEqual(int(match[1]), HOTWORD_LIMIT)


if __name__ == '__main__':
    unittest.main()
