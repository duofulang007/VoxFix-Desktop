"""Paired latency/attenuation probe; attenuation is not physical microphone distance."""
import base64
from contextlib import contextmanager
import gc
import json
import os
from pathlib import Path
import sys
import time
from types import SimpleNamespace
import wave

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "app"))
from recognize import Recognition
from p0_score import exact, units, distance

sys.path.append("D:/Projects/VoxFix/.android/nemotron-20261020/venv/Lib/site-packages")
import sherpa_onnx as sherpa

OUT = ROOT / "work/realtime-probe-20261020"
OUT.mkdir(exist_ok=True)
MANIFEST = json.loads((ROOT / "work/p0/manifest.json").read_text(encoding="utf-8"))


class LevelBoost:
    def __init__(self): self.level = 0.0
    def process(self, audio):
        rms = float(np.sqrt(np.mean(audio ** 2))) if len(audio) else 0
        self.level = max(rms, self.level * 0.96)
        gain = min(16.0, max(1.0, 0.035 / max(self.level, 1e-8)))
        return np.clip(audio * gain, -1, 1)


class OnnxStream:
    def __init__(self, engine): self.engine, self.handle = engine, engine.create_stream()
    def feed(self, pcm):
        self.handle.accept_waveform(16000, pcm)
        while self.engine.is_ready(self.handle): self.engine.decode_stream(self.handle)
    def text(self):
        text = self.engine.get_result(self.handle).lstrip()
        return SimpleNamespace(committed=text, display=text)
    def finalize(self):
        self.handle.input_finished()
        while self.engine.is_ready(self.handle): self.engine.decode_stream(self.handle)
    def reset(self): self.handle = None


class OnnxSession:
    def __init__(self, engine): self.engine = engine
    def stream(self, **options): return OnnxStream(self.engine)


@contextmanager
def create_engine(kind):
    if kind == "nemotron-cpp":
        os.environ["TRANSCRIBE_LIBRARY"] = str(ROOT / "runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll")
        import transcribe_cpp as tc
        with tc.Model(ROOT / "models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf", backend="cpu") as model:
            with model.session(n_threads=4) as session: yield session
    else:
        if kind == "nemotron-onnx":
            base = Path("D:/Projects/VoxFix/.android/nemotron-20261020/models/nemotron-3.5-160ms-int8")
            model = sherpa.OnlineRecognizer.from_transducer(encoder=str(base/"encoder.int8.onnx"),decoder=str(base/"decoder.int8.onnx"),joiner=str(base/"joiner.int8.onnx"),tokens=str(base/"tokens.txt"),num_threads=2,model_type="nemo",enable_endpoint_detection=False)
        else:
            base = Path("D:/Projects/VoxFix/models/streaming")
            model = sherpa.OnlineRecognizer.from_transducer(encoder=str(base/"encoder.int8.onnx"),decoder=str(base/"decoder.onnx"),joiner=str(base/"joiner.int8.onnx"),tokens=str(base/"tokens.txt"),num_threads=2,enable_endpoint_detection=False,modeling_unit="cjkchar+bpe",bpe_vocab=str(base/"bpe.vocab"))
        yield OnnxSession(model)
        del model
    gc.collect()


def read(row):
    with wave.open(row["wav"], "rb") as source:
        return np.frombuffer(source.readframes(source.getnframes()),dtype="<i2").astype(np.float32)/32768


def run(session, row, audio, engine, variant, paced=False, boost=False, frame_samples=1600):
    recognition=Recognition(session);identity="probe";level=LevelBoost()
    recognition.handle({"op":"start","id":identity})
    began=time.perf_counter();first=None;first_text="";updates=[];processing=0;last="";error=None
    try:
        for offset in range(0,len(audio),frame_samples):
            part=audio[offset:offset+frame_samples]
            if boost:part=level.process(part)
            part=(np.clip(part,-1,1)*32767).astype("<i2")
            if paced:time.sleep(max(0,began+(offset+len(part))/16000-time.perf_counter()))
            tick=time.perf_counter()
            reply=recognition.handle({"op":"audio","id":identity,"pcm":base64.b64encode(part.tobytes()).decode()})
            processing+=time.perf_counter()-tick
            if reply["text"]!=last:
                updates.append(round((time.perf_counter()-began)*1000,1) if paced else round((offset+len(part))/16,1));last=reply["text"]
            if last and first is None:first=updates[-1];first_text=last
        tick=time.perf_counter();reply=recognition.handle({"op":"stop","id":identity});stop=(time.perf_counter()-tick)*1000
        text=reply["text"]
    except Exception as exception:
        error=str(exception);text=last;stop=None;recognition.reset()
    return {"engine":engine,"variant":variant,"id":row["id"],"category":row["category"],"expected":row["expected"],"text":text,"error":error,
            "exact":exact(row["expected"],text),"unitErrors":distance(units(row["expected"]),units(text)),"refUnits":len(units(row["expected"])),
            "seconds":len(audio)/16000,"paced":paced,"firstMs":first,"firstText":first_text,
            "partialBeforeStop":last,"nonPrefixFinal":not text.startswith(last),
            "updateMs":updates,"stopMs":stop,"decodeRTF":processing/max(len(audio)/16000,0.001)}


def main():
    assert np.array_equal(LevelBoost().process(np.zeros(1600)),np.zeros(1600))
    assert np.max(LevelBoost().process(np.ones(1600)*0.0001))<=0.001601
    selected=[]
    for category in ["baseline","everyday","short","negation","mixed","number"]:
        selected.extend([r for r in MANIFEST if r["category"]==category][:6])
    rows=[];rng=np.random.default_rng(20261020)
    for engine in ["nemotron-cpp","nemotron-onnx","zipformer"]:
        with create_engine(engine) as session:
            for variant,scale,boost in [("near",1,False),("atten01",0.01,False),("atten001",0.001,False),("atten01-gain",0.01,True),("atten001-gain",0.001,True)]:
                for row in selected:rows.append(run(session,row,read(row)*scale,engine,variant,boost=boost))
                (OUT/"results.json").write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding="utf-8")
                group=[r for r in rows if r["engine"]==engine and r["variant"]==variant]
                print(engine,variant,"exact",sum(r["exact"] for r in group),"/",len(group),"blank",sum(not r["text"] for r in group),flush=True)
            for i in range(10):
                audio=np.zeros(32000) if i<2 else rng.normal(0,0.0005 if i<6 else 0.005,32000)
                rows.append(run(session,{"id":"noise-"+str(i),"category":"silence","expected":""},audio,engine,"noise-gain",boost=True))
            for identity in ["plain-zh-0","new-short-0","long-0-0"]:
                row=next(r for r in MANIFEST if r["id"]==identity)
                rows.append(run(session,row,read(row),engine,"paced",paced=True))
        (OUT/"results.json").write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding="utf-8")
    print("DONE",len(rows),flush=True)


if __name__=="__main__":main()
