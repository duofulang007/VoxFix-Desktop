"""Local agent handoff and fresh-audio acceptance. Never deploys a model."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import uuid
import wave


def now():
    return datetime.now(timezone.utc).isoformat()


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    path = Path(path)
    if path.stat().st_size > 16 * 1024 * 1024:
        raise ValueError('索引超过容量限制')
    return json.loads(path.read_text(encoding='utf-8-sig'))


def atomic(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    try:
        temp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)


def child(folder, relative):
    folder = Path(folder).resolve()
    result = (folder / relative).resolve()
    if not result.is_relative_to(folder):
        raise ValueError('归档内路径越界')
    return result


def wave_info(path):
    with wave.open(str(path), 'rb') as audio:
        if (audio.getnchannels(), audio.getsampwidth(), audio.getframerate(), audio.getcomptype()) != (1, 2, 16000, 'NONE'):
            raise ValueError('需要16kHz单声道PCM16录音')
        frames = audio.getnframes()
        pcm = audio.readframes(frames)
        if not 0 < frames <= 180 * 16000 or len(pcm) != frames * 2:
            raise ValueError('录音长度不完整或超出范围')
        return pcm, frames / 16000


def archive_state(folder):
    path = Path(folder) / 'lifecycle.json'
    if not path.exists():
        return 'archived'
    state = read(path)
    if not isinstance(state, dict) or state.get('schemaVersion') != 1 or state.get('status') not in ('archived', 'withdrawn'):
        raise ValueError('归档状态记录无效')
    return state['status']


def validate_archive(folder):
    folder = Path(folder).resolve()
    if archive_state(folder) != 'archived':
        raise ValueError('此批次已撤回，请先恢复归档')
    data = read(folder / 'manifest.json')
    if data.get('status') != 'prepared_not_trained' or not data.get('samples'):
        raise ValueError('不是已准备的训练归档')
    hashes, sources = [], []
    for sample in data['samples']:
        audio = child(folder, sample['audio'])
        wave_info(audio)
        if sha(audio) != sample['sha256']:
            raise ValueError('归档音频校验失败')
        meta = sample['metadata']
        if not meta.get('reviewed') or not meta.get('selectedForTraining') or meta.get('ignored') or not str(meta.get('correctText', '')).strip():
            raise ValueError('归档含未经确认的标签')
        hashes.append(sample['sha256'])
        sources.append(sample['sourceKind'] + ':' + sample['sourceId'])
    return {'path': str(folder), 'manifestSha256': sha(folder / 'manifest.json'), 'audioHashes': hashes, 'sources': sources}


def artifact(model, tokens):
    model, tokens = Path(model).resolve(), Path(tokens).resolve()
    if model.suffix.lower() != '.onnx' or model.stat().st_size == 0 or tokens.stat().st_size == 0:
        raise ValueError('候选模型或词表无效')
    return {'model': str(model), 'tokens': str(tokens), 'modelSha256': sha(model), 'tokensSha256': sha(tokens)}


class Reviews:
    def __init__(self, root):
        self.root = Path(root).resolve()
        self.folder = self.root / 'user-data' / 'training-review' / 'runs'

    def path(self, identity):
        if uuid.UUID(identity).hex != identity:
            raise ValueError('验收编号无效')
        return self.folder / identity / 'run.json'

    def load(self, identity):
        path = self.path(identity)
        run = read(path)
        if not isinstance(run, dict) or run.get('id') != identity or run.get('schemaVersion') != 1 or run.get('status') not in ('awaiting_retest', 'passed', 'rejected', 'deployed'):
            raise ValueError('验收索引无效')
        if not isinstance(run.get('name'), str) or not isinstance(run.get('createdUtc'), str) or not isinstance(run.get('cases'), list) or not isinstance(run.get('archives'), list):
            raise ValueError('验证索引字段不完整')
        for key in ('candidate', 'baseline'):
            if not isinstance(run.get(key), dict) or any(not isinstance(run[key].get(field), str) for field in ('model', 'tokens', 'modelSha256', 'tokensSha256')):
                raise ValueError('验证模型索引不完整')
        for archive in run['archives']:
            if not isinstance(archive, dict) or any(field not in archive for field in ('path', 'manifestSha256', 'audioHashes', 'sources')):
                raise ValueError('验证批次索引不完整')
        for case in run['cases']:
            if not isinstance(case, dict) or any(field not in case for field in ('id', 'audio', 'sha256', 'correctText', 'status')):
                raise ValueError('验证录音索引不完整')
        return run, sha(path)

    def save(self, run, expected):
        path = self.path(run['id'])
        if sha(path) != expected:
            raise ValueError('验收记录已被其他操作修改，请刷新')
        run['updatedUtc'] = now()
        atomic(path, run)

    def verify_models(self, run):
        for key in ('candidate', 'baseline'):
            item = run[key]
            if sha(item['model']) != item['modelSha256'] or sha(item['tokens']) != item['tokensSha256']:
                raise ValueError('候选或原模型已变化，需要重新登记验收版本')

    def verify_sources(self, run):
        report = run['trainingReport']
        if sha(report['path']) != report['sha256']:
            raise ValueError('训练报告已变化，请重新登记版本')
        for archive in run['archives']:
            if archive_state(archive['path']) != 'archived':
                raise ValueError('来源批次已撤回，请先恢复归档')
            if sha(Path(archive['path']) / 'manifest.json') != archive['manifestSha256']:
                raise ValueError('来源批次已变化，请重新登记版本')
            validate_archive(archive['path'])

    def register(self, name, archives, model, tokens, training_report):
        if not name.strip() or len(name) > 100 or not archives:
            raise ValueError('需要版本名称和训练来源归档')
        report = Path(training_report).resolve()
        if not report.is_file() or not report.stat().st_size:
            raise ValueError('需要真实训练报告文件')
        baseline = artifact(self.root / 'models/sensevoice/model.int8.onnx', self.root / 'models/sensevoice/tokens.txt')
        candidate = artifact(model, tokens)
        if candidate['modelSha256'] == baseline['modelSha256']:
            raise ValueError('候选与当前模型相同，不能登记为新微调版本')
        identity = uuid.uuid4().hex
        run = {'schemaVersion': 1, 'id': identity, 'name': name.strip(), 'createdUtc': now(),
               'status': 'awaiting_retest', 'candidate': candidate, 'baseline': baseline,
               'archives': [validate_archive(folder) for folder in archives],
               'trainingReport': {'path': str(report), 'sha256': sha(report)}, 'cases': [],
               'decision': None, 'deployment': {'status': 'not_deployed'}}
        atomic(self.path(identity), run)
        return run

    def list(self):
        runs, errors = [], []
        if self.folder.exists():
            for path in self.folder.glob('*/run.json'):
                try:
                    run, _ = self.load(path.parent.name)
                    run['displayStatus'] = {'awaiting_retest': '待复测', 'passed': '验证通过', 'rejected': '已打回', 'deployed': '已启用'}[run['status']]
                    run['sourceWithdrawn'] = any(archive_state(item['path']) == 'withdrawn' for item in run['archives'])
                    if run['sourceWithdrawn'] and run['status'] != 'deployed':
                        run['displayStatus'] = '来源已撤回'
                    if run['status'] == 'deployed':
                        live = self.root / 'models/sensevoice'
                        if sha(live / 'model.int8.onnx') != run['candidate']['modelSha256'] or sha(live / 'tokens.txt') != run['candidate']['tokensSha256']:
                            run['displayStatus'] = '已回退'
                    runs.append(run)
                except (OSError, ValueError, KeyError, TypeError) as error:
                    errors.append(path.parent.name + ': ' + str(error))
        runs.sort(key=lambda item: item['createdUtc'], reverse=True)
        return {'runs': runs, 'errors': errors}

    def add_case(self, identity, source, correct):
        run, expected = self.load(identity)
        if run['status'] in ('rejected', 'deployed'):
            raise ValueError('已打回或已启用的版本不追加复测，请由agent登记新版本')
        self.verify_sources(run)
        if not str(correct).strip() or len(correct) > 2000:
            raise ValueError('请确认新录音对应的完整正确文字')
        source = Path(source).resolve()
        meta = read(source.with_suffix('.json'))
        if datetime.fromisoformat(meta['createdUtc']) < datetime.fromisoformat(run['createdUtc']):
            raise ValueError('必须使用登记训练完成后录制的新音频')
        _, seconds = wave_info(source)
        digest = sha(source)
        training = {value for archive in run['archives'] for value in archive['audioHashes']}
        if digest in training or any(case['sha256'] == digest for case in run['cases']):
            raise ValueError('复测录音与训练材料或已有复测重复，请重新录制')
        case_id = uuid.uuid4().hex
        destination = self.path(identity).parent / 'audio' / (case_id + '.wav')
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        if sha(destination) != digest:
            raise ValueError('复测音频复制校验失败，原音保留')
        run['cases'].append({'id': case_id, 'createdUtc': now(), 'audio': 'audio/' + destination.name,
                             'sha256': digest, 'correctText': correct.strip(), 'durationSeconds': seconds,
                             'purpose': 'acceptance_only_not_for_training', 'status': 'pending'})
        self.remember_decision(run, '新增复测')
        run['status'], run['decision'] = 'awaiting_retest', None
        self.save(run, expected)
        return run

    def evaluate(self, identity, decoder=None, case_ids=None):
        run, expected = self.load(identity)
        if run['status'] != 'awaiting_retest':
            raise ValueError('只有待复测版本可运行识别对照')
        self.verify_models(run)
        self.verify_sources(run)
        chosen = None
        if case_ids is not None:
            if not isinstance(case_ids, list) or not case_ids or any(not isinstance(value, str) for value in case_ids):
                raise ValueError('请选择需要重试的复测记录')
            chosen = set(case_ids)
            if not chosen.issubset({case['id'] for case in run['cases']}):
                raise ValueError('选择的复测记录已变化，请刷新')
        if decoder is None:
            sys.path.insert(0, str(self.root / 'runtime/sherpa-onnx-1.13.8'))
            import numpy as np
            import sherpa_onnx as sherpa
            sessions = {key: sherpa.OfflineRecognizer.from_sense_voice(
                model=run[key]['model'], tokens=run[key]['tokens'], language='auto',
                use_itn=False, num_threads=4, provider='cpu') for key in ('baseline', 'candidate')}

            def decoder(key, pcm):
                stream = sessions[key].create_stream()
                stream.accept_waveform(16000, np.frombuffer(pcm, dtype='<i2').astype(np.float32) / 32768)
                sessions[key].decode_stream(stream)
                return stream.result.text
        for case in run['cases']:
            if case['status'] == 'evaluated' or (chosen is not None and case['id'] not in chosen):
                continue
            path = child(self.path(identity).parent, case['audio'])
            if sha(path) != case['sha256']:
                raise ValueError('复测音频已变化')
            pcm, _ = wave_info(path)
            baseline, candidate = decoder('baseline', pcm), decoder('candidate', pcm)
            if not isinstance(baseline, str) or not isinstance(candidate, str):
                raise ValueError('识别对照结果无效')
            case.update(baselineText=baseline, candidateText=candidate, status='evaluated',
                        evaluatedUtc=now(), candidateSha256=run['candidate']['modelSha256'],
                        baselineSha256=run['baseline']['modelSha256'], evaluationMode='raw_asr_no_dictionary_no_rescue')
        self.save(run, expected)
        return run

    def verify_cases(self, run):
        if not run['cases'] or any(case['status'] != 'evaluated' or case.get('candidateSha256') != run['candidate']['modelSha256'] or case.get('baselineSha256') != run['baseline']['modelSha256'] or not case.get('candidateText', '').strip() for case in run['cases']):
            raise ValueError('需先完成新录音的原模型/候选模型识别对照')
        for case in run['cases']:
            if sha(child(self.path(run['id']).parent, case['audio'])) != case['sha256']:
                raise ValueError('复测原音已变化，请撤回结论并重新核对')

    def decide(self, identity, decision, note):
        run, expected = self.load(identity)
        if run['status'] != 'awaiting_retest' or decision not in ('passed', 'rejected'):
            raise ValueError('版本状态不允许此验收操作')
        if len(note) > 2000 or (decision == 'rejected' and not note.strip()):
            raise ValueError('打回时请填写原因，备注最多2000字')
        if decision == 'passed':
            self.verify_models(run)
            self.verify_sources(run)
            self.verify_cases(run)
        run['status'] = decision
        run['decision'] = {'status': decision, 'note': note.strip(), 'createdUtc': now(),
                           'caseIds': [case['id'] for case in run['cases']], 'scope': 'personal_fresh_audio_only'}
        self.save(run, expected)
        return run

    @staticmethod
    def remember_decision(run, reason):
        if run.get('decision'):
            run.setdefault('decisionHistory', []).append({'decision': run['decision'], 'reason': reason, 'createdUtc': now()})

    def reopen(self, identity):
        run, expected = self.load(identity)
        if run['status'] not in ('passed', 'rejected'):
            raise ValueError('只有通过或打回结论可以撤回；实际启用需由agent回滚模型')
        self.remember_decision(run, '人工撤回结论')
        run['status'], run['decision'] = 'awaiting_retest', None
        self.save(run, expected)
        return run

    def deployed(self, identity, report):
        run, expected = self.load(identity)
        if run['status'] != 'passed':
            raise ValueError('必须先人工验收通过')
        self.verify_sources(run)
        self.verify_cases(run)
        report = Path(report).resolve()
        if not report.is_file() or not report.stat().st_size:
            raise ValueError('需要agent的正式启用与回滚验证报告')
        live = artifact(self.root / 'models/sensevoice/model.int8.onnx', self.root / 'models/sensevoice/tokens.txt')
        if any(live[key] != run['candidate'][key] for key in ('modelSha256', 'tokensSha256')):
            raise ValueError('生产模型尚未与候选一致，不能标为已启用')
        run['status'] = 'deployed'
        run['deployment'] = {'status': 'verified_deployed', 'createdUtc': now(), 'report': str(report), 'reportSha256': sha(report)}
        self.save(run, expected)
        return run


def dispatch(request):
    store = Reviews(request['root'])
    action = request['action']
    if action == 'register':
        return store.register(request['name'], request['archives'], request['model'], request['tokens'], request['report'])
    if action == 'capture':
        store.add_case(request['id'], request['audio'], request['correctText'])
        store.evaluate(request['id'])
    elif action == 'evaluate':
        store.evaluate(request['id'], case_ids=request.get('caseIds'))
    elif action == 'decide':
        store.decide(request['id'], request['decision'], request.get('note', ''))
    elif action == 'reopen':
        store.reopen(request['id'])
    elif action == 'deployed':
        store.deployed(request['id'], request['report'])
    elif action != 'list':
        raise ValueError('未知验收操作')
    return store.list()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--request', action='store_true', help='read one JSON request from stdin')
    parser.add_argument('--root', default=str(Path(__file__).resolve().parent.parent))
    parser.add_argument('action', nargs='?', choices=['list', 'register', 'evaluate', 'reopen', 'deployed'])
    parser.add_argument('--name')
    parser.add_argument('--archive', action='append', dest='archives')
    parser.add_argument('--model')
    parser.add_argument('--tokens')
    parser.add_argument('--report')
    parser.add_argument('--id')
    args = vars(parser.parse_args())
    try:
        request = json.load(sys.stdin) if args.pop('request') else args
        print(json.dumps({'ok': True, **({'run': dispatch(request)} if request['action'] == 'register' else dispatch(request))}, ensure_ascii=True))
        return 0
    except Exception as error:
        print(json.dumps({'ok': False, 'error': str(error)}, ensure_ascii=True))
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
