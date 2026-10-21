"""Isolated official C ABI adapter; no microphone, injection, or saved user audio."""
import ctypes as c
import os
from pathlib import Path
from types import SimpleNamespace
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
RUNTIME = ROOT / 'runtime/nemo-speech-0.2.0-windows-x86_64-cpu/bin'


class Backend(c.Structure):
    _fields_ = [('size', c.c_size_t), ('gpu', c.c_int32)]


class ModelConfig(c.Structure):
    _fields_ = [('size', c.c_size_t), ('path', c.c_char_p), ('name', c.c_char_p)]


class Streaming(c.Structure):
    _fields_ = [('size', c.c_size_t), ('chunk', c.c_float), ('left', c.c_float), ('right', c.c_float), ('context', c.c_int32)]


class Config(c.Structure):
    _fields_ = [('size', c.c_size_t), ('backend', c.POINTER(Backend)), ('model', c.POINTER(ModelConfig)),
                ('streaming', c.POINTER(Streaming))] + [(name, c.c_void_p) for name in ['decoder', 'vad', 'endpoint', 'postproc', 'diar', 'batching']]


class Context(c.Structure):
    _fields_ = [('size', c.c_size_t), ('phrases', c.POINTER(c.c_char_p)), ('count', c.c_size_t), ('boost', c.c_float)]


class Options(c.Structure):
    _fields_ = [('size', c.c_size_t), ('request', c.c_char_p), ('language', c.c_char_p),
                ('interim', c.c_bool), ('word_times', c.c_bool), ('punctuation', c.c_bool), ('verbatim', c.c_bool),
                ('profanity', c.c_bool), ('stop_ms', c.c_int32), ('contexts', c.POINTER(Context)),
                ('count', c.c_size_t), ('alternatives', c.c_int32), ('diarize', c.c_bool), ('speakers', c.c_int32)]


class NemoSession:
    def __init__(self, model, terms=(), boost=2, right=13, language=None):
        self.directory = os.add_dll_directory(str(RUNTIME))
        self.lib = c.CDLL(str(RUNTIME / 'nemo_speech_asr_c.dll'))
        self.handle = c.c_void_p()
        signatures = {
            'last_error': ([], c.c_char_p),
            'recognition_options_default': ([], Options),
            'create': ([c.POINTER(Config), c.POINTER(c.c_void_p)], c.c_int),
            'destroy': ([c.c_void_p], None),
            'streaming_recognize': ([c.c_void_p, c.POINTER(Options), c.POINTER(c.c_void_p)], c.c_int),
            'stream_push_f32': ([c.c_void_p, c.POINTER(c.c_float), c.c_size_t, c.c_int32], c.c_int),
            'stream_next': ([c.c_void_p, c.POINTER(c.c_void_p)], c.c_int),
            'stream_finish': ([c.c_void_p], c.c_int),
            'stream_close': ([c.c_void_p], None),
            'result_transcript': ([c.c_void_p, c.c_size_t], c.c_char_p),
            'result_destroy': ([c.c_void_p], None),
        }
        for name, (args, result) in signatures.items():
            method = getattr(self.lib, 'nemo_speech_asr_' + name)
            method.argtypes, method.restype = args, result
            setattr(self, name, method)
        self.terms, self.boost = tuple(terms), boost
        self.language = language.encode('utf8') if language else None
        backend = Backend(c.sizeof(Backend), -1)
        config_model = ModelConfig(c.sizeof(ModelConfig), str(Path(model).resolve()).encode('utf8'), None)
        streaming = Streaming(c.sizeof(Streaming), .1, 0, 0, right)
        config = Config(size=c.sizeof(Config), backend=c.pointer(backend), model=c.pointer(config_model), streaming=c.pointer(streaming))
        self.check(self.create(c.byref(config), c.byref(self.handle)))

    def check(self, status):
        if status:
            raise RuntimeError((self.last_error() or b'Unknown native error').decode('utf8', errors='replace'))

    def stream(self, **unused):
        return NemoStream(self)

    def close(self):
        if self.handle:
            self.destroy(self.handle)
            self.handle = c.c_void_p()
        self.directory.close()


class NemoStream:
    def __init__(self, session):
        self.session, self.handle, self.latest = session, c.c_void_p(), ''
        self.phrases = (c.c_char_p * len(session.terms))(*(term.encode('utf8') for term in session.terms))
        self.context = Context(c.sizeof(Context), self.phrases, len(session.terms), session.boost)
        self.options = session.recognition_options_default()
        self.options.interim = True
        self.options.language = session.language
        if session.terms:
            self.options.contexts, self.options.count = c.pointer(self.context), 1
        session.check(session.streaming_recognize(session.handle, c.byref(self.options), c.byref(self.handle)))

    def drain(self):
        session = self.session
        while True:
            result = c.c_void_p()
            session.check(session.stream_next(self.handle, c.byref(result)))
            if not result:
                break
            try:
                self.latest = (session.result_transcript(result, 0) or b'').decode('utf8')
            finally:
                session.result_destroy(result)

    def feed(self, audio):
        audio = np.ascontiguousarray(audio, dtype=np.float32)
        self.session.check(self.session.stream_push_f32(self.handle, audio.ctypes.data_as(c.POINTER(c.c_float)), len(audio), 16000))
        self.drain()

    def text(self):
        return SimpleNamespace(display=self.latest)

    def finalize(self):
        self.session.check(self.session.stream_finish(self.handle))
        self.drain()

    def reset(self):
        if self.handle:
            self.session.stream_close(self.handle)
            self.handle = c.c_void_p()


if __name__ == '__main__':
    import sys
    assert c.sizeof(Options) == 72 and c.sizeof(Context) == 32
    model = ROOT / 'models/nemo-official/nemotron-3.5-asr-streaming-0.6b.q8_0.gguf'
    session = NemoSession(model)
    try:
        stream = session.stream()
        stream.feed(np.zeros(16000, dtype=np.float32))
        stream.finalize()
        assert stream.text().display == '', 'Digital silence hallucination'
        stream.reset()
        print('PASS: official model load / streaming C ABI / exact silence / close')
    finally:
        session.close()
