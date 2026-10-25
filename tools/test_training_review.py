"""Agent handoff and fresh-audio acceptance without models or a microphone."""
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import sys
import tempfile
import unittest
import wave

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'app'))
from training_review import Reviews, atomic, child, sha, validate_archive


def wav(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), 'wb') as out:
        out.setparams((1, 2, 16000, 0, 'NONE', ''))
        out.writeframes(int(value).to_bytes(2, 'little', signed=True) * 16000)


class ReviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        live = self.root / 'models/sensevoice'
        live.mkdir(parents=True)
        (live / 'model.int8.onnx').write_bytes(b'baseline fixture, never inference')
        (live / 'tokens.txt').write_text('tokens', encoding='utf-8')
        self.model = self.root / 'candidate.onnx'
        self.model.write_bytes(b'candidate fixture, never inference')
        self.tokens = live / 'tokens.txt'
        self.report = self.root / 'training.md'
        self.report.write_text('isolated training evidence fixture', encoding='utf-8')
        self.archive = self.root / 'archive'
        wav(self.archive / 'audio/a.wav', 1000)
        atomic(self.archive / 'manifest.json', {'status': 'prepared_not_trained', 'samples': [{
            'sourceKind': 'dictation-history', 'sourceId': 'source-one', 'audio': 'audio/a.wav',
            'sha256': sha(self.archive / 'audio/a.wav'), 'metadata': {
                'reviewed': True, 'selectedForTraining': True, 'correctText': '完整正确文字', 'roundId': 'r1'}}]})
        self.store = Reviews(self.root)
        self.run = self.store.register('测试候选', [self.archive], self.model, self.tokens, self.report)

    def tearDown(self):
        self.temp.cleanup()

    def fresh(self, value=2000, old=False):
        path = self.root / ('fresh-' + str(value) + '.wav')
        wav(path, value)
        created = datetime.now(timezone.utc) - timedelta(days=1) if old else datetime.now(timezone.utc)
        atomic(path.with_suffix('.json'), {'createdUtc': created.isoformat(), 'correctText': '新的完整一句'})
        return path

    def add(self):
        return self.store.add_case(self.run['id'], self.fresh(), '新的完整一句')

    def evaluate(self):
        return self.store.evaluate(self.run['id'], lambda key, pcm: '旧识别' if key == 'baseline' else '新的完整一句')

    def test_register_records_sources_and_never_claims_deployed(self):
        self.assertEqual(self.run['status'], 'awaiting_retest')
        self.assertEqual(self.run['archives'][0]['sources'], ['dictation-history:source-one'])
        self.assertEqual(self.run['deployment']['status'], 'not_deployed')

    def test_archive_rejects_modified_audio_and_paths(self):
        with self.assertRaises(ValueError):
            child(self.archive, '../outside.wav')
        (self.archive / 'audio/a.wav').write_bytes(b'broken')
        with self.assertRaises((ValueError, wave.Error, EOFError)):
            validate_archive(self.archive)

    def test_training_and_old_audio_cannot_be_acceptance(self):
        copied = self.fresh(1000)
        with self.assertRaisesRegex(ValueError, '训练材料'):
            self.store.add_case(self.run['id'], copied, '完整正确文字')
        with self.assertRaisesRegex(ValueError, '新音频'):
            self.store.add_case(self.run['id'], self.fresh(2000, old=True), '新的完整一句')

    def test_fresh_audio_is_isolated_and_duplicates_blocked(self):
        run = self.add()
        self.assertEqual(run['cases'][0]['purpose'], 'acceptance_only_not_for_training')
        with self.assertRaisesRegex(ValueError, '重复'):
            self.store.add_case(self.run['id'], self.fresh(), '新的完整一句')
        self.assertFalse((self.root / 'user-data/voice-samples').exists())

    def test_pass_requires_evaluation_and_preserves_original_outputs(self):
        with self.assertRaises(ValueError):
            self.store.decide(self.run['id'], 'passed', '')
        self.add()
        with self.assertRaises(ValueError):
            self.store.decide(self.run['id'], 'passed', '')
        evaluated = self.evaluate()
        self.assertEqual(evaluated['cases'][0]['baselineText'], '旧识别')
        accepted = self.store.decide(self.run['id'], 'passed', '本人新录音确认')
        self.assertEqual(accepted['status'], 'passed')
        self.assertEqual(accepted['deployment']['status'], 'not_deployed')
        self.assertEqual(accepted['cases'][0]['candidateText'], '新的完整一句')

    def test_reject_requires_reason_and_new_candidate_for_retraining(self):
        with self.assertRaises(ValueError):
            self.store.decide(self.run['id'], 'rejected', '')
        rejected = self.store.decide(self.run['id'], 'rejected', '尾音不正确')
        self.assertEqual(rejected['decision']['note'], '尾音不正确')
        with self.assertRaises(ValueError):
            self.add()

    def test_model_and_audio_change_block_acceptance(self):
        self.add()
        self.evaluate()
        self.model.write_bytes(b'changed candidate')
        with self.assertRaisesRegex(ValueError, '模型已变化'):
            self.store.decide(self.run['id'], 'passed', '')

    def test_changed_sources_and_report_block_evaluation(self):
        self.add()
        self.report.write_text('modified report', encoding='utf-8')
        with self.assertRaisesRegex(ValueError, '训练报告已变化'):
            self.evaluate()

    def test_changed_archive_blocks_approval(self):
        self.add()
        self.evaluate()
        atomic(self.archive / 'manifest.json', {'changed': True})
        with self.assertRaisesRegex(ValueError, '来源批次已变化'):
            self.store.decide(self.run['id'], 'passed', '')

    def test_changed_archive_audio_blocks_evaluation_and_approval(self):
        self.add()
        self.evaluate()
        wav(self.archive / 'audio/a.wav', 4000)
        with self.assertRaisesRegex(ValueError, '音频校验失败'):
            self.evaluate()
        with self.assertRaisesRegex(ValueError, '音频校验失败'):
            self.store.decide(self.run['id'], 'passed', '')

    def test_bad_run_is_reported_without_hiding_good_runs(self):
        import uuid
        identity = uuid.uuid4().hex
        atomic(self.store.path(identity), {'schemaVersion': 1, 'id': identity, 'status': 'passed'})
        response = self.store.list()
        self.assertEqual(len(response['runs']), 1)
        self.assertEqual(len(response['errors']), 1)

    def test_concurrent_changes_not_overwritten(self):
        run, expected = self.store.load(self.run['id'])
        changed = dict(run, name='other editor')
        atomic(self.store.path(run['id']), changed)
        with self.assertRaisesRegex(ValueError, '其他操作'):
            self.store.save(run, expected)

    def test_deployment_requires_actual_production_hash_and_report(self):
        self.add()
        self.evaluate()
        self.store.decide(self.run['id'], 'passed', '')
        with self.assertRaisesRegex(ValueError, '生产模型尚未'):
            self.store.deployed(self.run['id'], self.report)
        live = self.root / 'models/sensevoice/model.int8.onnx'
        live.write_bytes(self.model.read_bytes())
        deployed = self.store.deployed(self.run['id'], self.report)
        self.assertEqual(deployed['status'], 'deployed')
        self.assertEqual(self.store.list()['runs'][0]['displayStatus'], '已启用')
        live.write_bytes(b'rolled back')
        self.assertEqual(self.store.list()['runs'][0]['displayStatus'], '已回退')

    def test_new_test_invalidates_previous_acceptance(self):
        self.add()
        self.evaluate()
        self.store.decide(self.run['id'], 'passed', '')
        run = self.store.add_case(self.run['id'], self.fresh(3000), '再录的新句子')
        self.assertEqual(run['status'], 'awaiting_retest')
        self.assertIsNone(run['decision'])

    def test_withdrawn_archive_blocks_new_training_and_acceptance(self):
        self.add()
        self.evaluate()
        original = sha(self.archive / 'manifest.json')
        atomic(self.archive / 'lifecycle.json', {'schemaVersion': 1, 'status': 'withdrawn', 'history': []})
        with self.assertRaisesRegex(ValueError, '已撤回'):
            validate_archive(self.archive)
        with self.assertRaisesRegex(ValueError, '已撤回'):
            self.store.decide(self.run['id'], 'passed', '')
        with self.assertRaisesRegex(ValueError, '已撤回'):
            self.store.add_case(self.run['id'], self.fresh(3000), '不应保存的新复测')
        self.assertEqual(len(self.store.load(self.run['id'])[0]['cases']), 1)
        self.assertEqual(self.store.list()['runs'][0]['displayStatus'], '来源已撤回')
        atomic(self.archive / 'lifecycle.json', {'schemaVersion': 1, 'status': 'archived', 'history': []})
        self.assertEqual(sha(self.archive / 'manifest.json'), original)
        self.assertEqual(self.store.decide(self.run['id'], 'passed', '')['status'], 'passed')

    def test_both_decisions_can_return_to_pending_without_losing_cases(self):
        self.add()
        self.evaluate()
        for decision in ('passed', 'rejected'):
            self.store.decide(self.run['id'], decision, '原结论说明')
            run = self.store.reopen(self.run['id'])
            self.assertEqual(run['status'], 'awaiting_retest')
            self.assertIsNone(run['decision'])
            self.assertEqual(run['cases'][0]['status'], 'evaluated')
            self.assertEqual(run['decisionHistory'][-1]['decision']['note'], '原结论说明')
            self.assertEqual(run['decisionHistory'][-1]['decision']['status'], decision)

    def test_deployed_state_cannot_be_reversed_by_metadata_only(self):
        self.add()
        self.evaluate()
        self.store.decide(self.run['id'], 'passed', '')
        (self.root / 'models/sensevoice/model.int8.onnx').write_bytes(self.model.read_bytes())
        self.store.deployed(self.run['id'], self.report)
        with self.assertRaisesRegex(ValueError, '实际启用'):
            self.store.reopen(self.run['id'])

    def test_withdrawn_sources_block_deployment_record(self):
        self.add()
        self.evaluate()
        self.store.decide(self.run['id'], 'passed', '')
        atomic(self.archive / 'lifecycle.json', {'schemaVersion': 1, 'status': 'withdrawn'})
        with self.assertRaisesRegex(ValueError, '已撤回'):
            self.store.deployed(self.run['id'], self.report)

    def test_selected_retry_does_not_evaluate_other_cases(self):
        first = self.add()['cases'][0]['id']
        self.store.add_case(self.run['id'], self.fresh(3000), '另一句')
        run = self.store.evaluate(self.run['id'], lambda key, pcm: '识别结果', case_ids=[first])
        self.assertEqual([case['status'] for case in run['cases']], ['evaluated', 'pending'])
        with self.assertRaises(ValueError):
            self.store.decide(self.run['id'], 'passed', '')

    def test_invalid_retry_selection_cannot_process_whole_run(self):
        self.add()
        for selection in ([], ['missing-case'], 'not-a-list'):
            with self.assertRaises(ValueError):
                self.store.evaluate(self.run['id'], lambda key, pcm: '结果', case_ids=selection)
        run, _ = self.store.load(self.run['id'])
        self.assertEqual(run['cases'][0]['status'], 'pending')

    def test_failed_decode_keeps_original_audio_and_pending_case(self):
        self.add()
        def failing(key, pcm):
            raise RuntimeError('decode failure')
        with self.assertRaises(RuntimeError):
            self.store.evaluate(self.run['id'], failing)
        run, _ = self.store.load(self.run['id'])
        self.assertEqual(run['cases'][0]['status'], 'pending')
        self.assertTrue(child(self.store.path(run['id']).parent, run['cases'][0]['audio']).exists())


if __name__ == '__main__':
    unittest.main()
