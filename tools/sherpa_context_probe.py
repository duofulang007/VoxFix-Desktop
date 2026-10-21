"""Compare newly built streaming beam search using correctly pretokenized phrases."""
import json
from pathlib import Path
import sys
import os
import re

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'runtime/sherpa-context-51aaac5'))
sys.path.insert(0, str(ROOT / '.tmp/hw-tokenizer'))
import sherpa_onnx as sherpa
from tokenizers import Tokenizer
from realtime_probe import MANIFEST, OnnxSession, read, run
from benchmark_context import TERMS, OUT, summary
from tune_context import fixtures

MODEL = Path('D:/Projects/VoxFix/.android/nemotron-20261020/models/nemotron-3.5-160ms-int8')


class StablePrefix:
    def __init__(self):
        self.history, self.committed = [], ''

    def accept(self, text):
        self.history = (self.history + [text])[-3:]
        if len(self.history) < 3:
            return self.committed
        candidate = os.path.commonprefix(self.history)
        candidate = re.sub(r"[A-Za-z0-9_'-]+$", '', candidate)
        if not candidate.startswith(self.committed):
            raise ValueError('Confirmed beam prefix changed')
        self.committed = candidate
        return candidate


class StableStream:
    def __init__(self, engine):
        self.engine, self.handle = engine, engine.create_stream()
        self.prefix, self.latest, self.final = StablePrefix(), '', False

    def decode(self):
        while self.engine.is_ready(self.handle):
            self.engine.decode_stream(self.handle)
            text = self.engine.get_result(self.handle).lstrip()
            self.latest = self.prefix.accept(text)

    def feed(self, audio):
        self.handle.accept_waveform(16000, audio)
        self.decode()

    def text(self):
        from types import SimpleNamespace
        return SimpleNamespace(display=self.latest)

    def finalize(self):
        self.handle.input_finished()
        self.decode()
        text = self.engine.get_result(self.handle).lstrip()
        if not text.startswith(self.prefix.committed):
            raise ValueError('Final beam text changed confirmed prefix')
        self.latest = text

    def reset(self):
        self.handle = None


class StableSession:
    def __init__(self, engine): self.engine = engine
    def stream(self, **unused): return StableStream(self.engine)


def encode():
    tokenizer = Tokenizer.from_file(str(OUT / 'tokenizer.json'))
    table = {int(line.rsplit(' ', 1)[1]): line.rsplit(' ', 1)[0]
             for line in (MODEL / 'tokens.txt').read_text(encoding='utf8').splitlines()}
    encoded = []
    for phrase in TERMS:
        ids = tokenizer.encode(phrase, add_special_tokens=False).ids
        assert ids and all(i in table and i != 0 for i in ids), phrase
        assert tokenizer.decode(ids).strip() == phrase, phrase
        encoded.append(' '.join(table[i] for i in ids))
    path = OUT / 'nemotron-hotword-tokens.txt'
    path.write_text('\n'.join(encoded) + '\n', encoding='utf8')
    return path


def create(method, score, hotwords, kind='nemotron'):
    if kind == 'zipformer':
        base = Path('D:/Projects/VoxFix/models/streaming')
        return StableSession(sherpa.OnlineRecognizer.from_transducer(
            encoder=str(base / 'encoder.int8.onnx'), decoder=str(base / 'decoder.onnx'),
            joiner=str(base / 'joiner.int8.onnx'), tokens=str(base / 'tokens.txt'),
            num_threads=4, feature_dim=80, decoding_method=method, max_active_paths=4,
            modeling_unit='cjkchar+bpe', bpe_vocab=str(base / 'bpe.vocab'),
            enable_endpoint_detection=False, hotwords_file=str(hotwords) if score else '', hotwords_score=score or 1))
    return StableSession(sherpa.OnlineRecognizer.from_transducer(
        encoder=str(MODEL / 'encoder.int8.onnx'), decoder=str(MODEL / 'decoder.int8.onnx'),
        joiner=str(MODEL / 'joiner.int8.onnx'), tokens=str(MODEL / 'tokens.txt'),
        num_threads=4, model_type='nemo', feature_dim=128, decoding_method=method,
        max_active_paths=4, modeling_unit='tokens', enable_endpoint_detection=False,
        hotwords_file=str(hotwords) if score else '', hotwords_score=score or 1))


def main():
    assert 'context' in sherpa.__version__, 'Must use the isolated tested build'
    kind = 'zipformer' if '--zipformer' in sys.argv else 'nemotron'
    if kind == 'zipformer':
        hotwords = OUT / 'zipformer-hotwords.txt'
        hotwords.write_text('\n'.join(t.upper() for t in TERMS) + '\n', encoding='utf8')
    else:
        hotwords = encode()
    selected = []
    for category in ['baseline', 'everyday', 'short', 'negation', 'mixed', 'number']:
        selected.extend([r for r in MANIFEST if r['category'] == category][:6])
    rows = []
    stable = StablePrefix()
    assert stable.accept('API') == '' and stable.accept('API返回') == ''
    assert stable.accept('API返回数据') == ''
    assert stable.accept('API返回数据了') == 'API返回'
    try:
        stable.accept('其他内容')
    except ValueError:
        pass
    else:
        raise AssertionError('Confirmed-prefix revision must stop input')
    for method, score in [('modified_beam_search', 0), ('modified_beam_search', .5), ('modified_beam_search', 1.5)]:
        label = method + '-' + str(score)
        session = create(method, score, hotwords, kind)
        group = [run(session, row, read(row), 'sherpa-context', label) for row in selected]
        controlled = [run(session, row, read(row), 'sherpa-context', label) for row in fixtures()]
        rows.extend(group + controlled)
        print(label, 'core', summary(group), 'terms', sum(r['exact'] for r in controlled if r['category'] == 'term'),
              'controls', sum(r['exact'] for r in controlled if r['category'] == 'control'), flush=True)
        (OUT / (kind + '-stable-tuning.json')).write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf8')
        del session


if __name__ == '__main__':
    main()
