"""Same local audio, existing public models only; no phone or model files modified."""
import gc
import json
from pathlib import Path
import time
import wave

import numpy as np
import sherpa_onnx as sherpa

ROOT = Path(__file__).resolve().parent.parent
ANDROID = Path("D:/Projects/VoxFix")
OUT = ROOT / "work/chinese-local-controls-20261020.json"


def main():
    manifest = json.loads((ROOT / "work/p0/manifest.json").read_text(encoding="utf-8"))
    results = []
    for kind in ("zipformer", "sensevoice"):
        if kind == "zipformer":
            base = ANDROID / "models/streaming"
            model = sherpa.OnlineRecognizer.from_transducer(
                encoder=str(base/"encoder.int8.onnx"), decoder=str(base/"decoder.onnx"),
                joiner=str(base/"joiner.int8.onnx"), tokens=str(base/"tokens.txt"),
                num_threads=2, sample_rate=16000, feature_dim=80, decoding_method="greedy_search",
                enable_endpoint_detection=False, modeling_unit="cjkchar+bpe", bpe_vocab=str(base/"bpe.vocab"))
        else:
            base = ANDROID / "models/sensevoice"
            model = sherpa.OfflineRecognizer.from_sense_voice(model=str(base/"model.int8.onnx"), tokens=str(base/"tokens.txt"), language="auto", use_itn=True, num_threads=4)
        for row in manifest:
            with wave.open(row["wav"], "rb") as audio:
                pcm=np.frombuffer(audio.readframes(audio.getnframes()),dtype="<i2").astype(np.float32)/32768
            began=time.perf_counter(); stream=model.create_stream()
            if kind == "zipformer":
                for offset in range(0,len(pcm),1600):
                    stream.accept_waveform(16000,pcm[offset:offset+1600])
                    while model.is_ready(stream): model.decode_stream(stream)
                stream.accept_waveform(16000,np.zeros(8000,dtype=np.float32));stream.input_finished()
                while model.is_ready(stream): model.decode_stream(stream)
                text=model.get_result(stream)
            else:
                stream.accept_waveform(16000,pcm);model.decode_stream(stream);text=stream.result.text
            results.append({"id":row["id"],"category":row["category"],"expected":row["expected"],"text":text,"group":kind,"seconds":row["seconds"],"wallMs":round((time.perf_counter()-began)*1000,2)})
        del model; gc.collect()
        OUT.write_text(json.dumps({"sherpaVersion":sherpa.__version__,"rows":results},ensure_ascii=False,indent=2),encoding="utf-8")
        print(kind,"done",len(results),flush=True)


if __name__ == "__main__": main()
