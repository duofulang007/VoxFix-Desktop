"""桌面端「首字」横向对照：Zipformer vs Nemotron(int8/sherpa) vs Nemotron(GGUF/transcribe.cpp)。

同一批 8 条（与安卓那轮 desktop_realtime.py 完全相同的 id），20ms 真实节奏投递，
chunk 0.16s，测「开口 → 第一段非空文本」的时间。这是「能不能当首遍」的唯一判据。

用法: python tools/p0_firsttoken.py            # 用 .venv（transcribe.cpp 段）
      python tools/p0_firsttoken.py --sherpa   # 用装着 sherpa-onnx 1.13.8 的 venv 跑前两段
"""
import argparse
import json
import statistics
import time
import wave
from pathlib import Path

import numpy as np

HOME = Path(__file__).resolve().parent.parent
ANDROID = Path("D:/Projects/VoxFix")
IDS = ["plain-zh-0", "plain-zh-8", "new-short-0", "mix-huihui-5",
       "sentence-0", "public-yue-0", "long-0-0", "new-negation-3"]
CHUNK = 0.16
TICK = 0.02


def read_wav(case_id):
    path = HOME / f"work/p0/wav/{case_id}.wav"
    with wave.open(str(path), "rb") as handle:
        frames = handle.readframes(handle.getnframes())
    return np.frombuffer(frames, dtype="<i2").astype(np.float32) / 32768.0


def summarise(name, rows):
    firsts = [r["firstMs"] for r in rows if r["firstMs"] > 0]
    rtf = statistics.mean(r["decodeMs"] / r["audioMs"] for r in rows)
    return {"engine": name, "n": len(rows),
            "firstP50": round(statistics.median(firsts), 0) if firsts else None,
            "firstMin": round(min(firsts), 0) if firsts else None,
            "firstMax": round(max(firsts), 0) if firsts else None,
            "rtf": round(rtf, 3), "rows": rows}


def run_sherpa():
    import sherpa_onnx

    def build(kind, threads):
        if kind == "zipformer":
            base = ANDROID / "models/streaming"
            return sherpa_onnx.OnlineRecognizer.from_transducer(
                encoder=str(base / "encoder.int8.onnx"), decoder=str(base / "decoder.onnx"),
                joiner=str(base / "joiner.int8.onnx"), tokens=str(base / "tokens.txt"),
                num_threads=threads, sample_rate=16000, feature_dim=80, decoding_method="greedy_search",
                max_active_paths=2, enable_endpoint_detection=False, rule_fsts="",
                hotwords_file="", hotwords_score=1.5, modeling_unit="cjkchar+bpe",
                bpe_vocab=str(base / "bpe.vocab"))
        base = ANDROID / ".android/nemotron-20261020/models/nemotron-3.5-160ms-int8"
        return sherpa_onnx.OnlineRecognizer.from_transducer(
            encoder=str(base / "encoder.int8.onnx"), decoder=str(base / "decoder.int8.onnx"),
            joiner=str(base / "joiner.int8.onnx"), tokens=str(base / "tokens.txt"),
            num_threads=threads, sample_rate=16000, feature_dim=80, decoding_method="greedy_search",
            max_active_paths=2, enable_endpoint_detection=False, model_type="nemo")

    results = []
    for kind in ("zipformer", "nemotron"):
        for threads in (2, 4):
            engine = build(kind, threads)
            rows = []
            for case_id in IDS:
                pcm = read_wav(case_id)
                stream = engine.create_stream()
                first, decode_ms = -1.0, 0.0
                fed, started = 0, time.perf_counter()
                step = int(CHUNK * 16000)
                while fed < pcm.size:
                    end = min(fed + step, pcm.size)
                    batch = pcm[fed:end]
                    offset = 0
                    tick = int(TICK * 16000)
                    while offset < batch.size:
                        t0 = time.perf_counter()
                        stream.accept_waveform(16000, batch[offset:offset + tick])
                        while engine.is_ready(stream):
                            engine.decode_stream(stream)
                        decode_ms += (time.perf_counter() - t0) * 1000
                        offset += tick
                        if first < 0 and engine.get_result(stream).strip():
                            first = (fed + offset) / 16.0
                        wait = started + (fed + offset) / 16000 - time.perf_counter()
                        if wait > 0:
                            time.sleep(wait)
                    fed = end
                stream.accept_waveform(16000, np.zeros(8000, dtype=np.float32))
                stream.input_finished()
                while engine.is_ready(stream):
                    engine.decode_stream(stream)
                rows.append({"id": case_id, "firstMs": round(first, 0),
                             "audioMs": round(pcm.size / 16.0, 1), "decodeMs": round(decode_ms, 1),
                             "text": engine.get_result(stream).strip()})
            results.append(summarise(f"{kind}-t{threads}", rows))
    return results


def run_gguf():
    import os
    os.environ.setdefault("TRANSCRIBE_LIBRARY", str(
        HOME / "runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll"))
    import transcribe_cpp as t

    model = t.Model(HOME / "models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf", backend="cpu")
    session = model.session(n_threads=4)
    rows = []
    with session as sess:
        for case_id in IDS:
            pcm = read_wav(case_id)
            first, decode_ms = -1.0, 0.0
            started = time.perf_counter()
            with sess.stream(timestamps="none") as stream:
                step, tick = int(CHUNK * 16000), int(TICK * 16000)
                fed = 0
                while fed < pcm.size:
                    end = min(fed + step, pcm.size)
                    batch = pcm[fed:end]
                    offset = 0
                    while offset < batch.size:
                        t0 = time.perf_counter()
                        stream.feed(batch[offset:offset + tick])
                        decode_ms += (time.perf_counter() - t0) * 1000
                        offset += tick
                        if first < 0:
                            view = stream.text()
                            if (view.committed + view.tentative).strip():
                                first = (fed + offset) / 16.0
                        wait = started + (fed + offset) / 16000 - time.perf_counter()
                        if wait > 0:
                            time.sleep(wait)
                    fed = end
                stream.finalize()
                text = stream.text().full.strip()
            rows.append({"id": case_id, "firstMs": round(first, 0),
                         "audioMs": round(pcm.size / 16.0, 1), "decodeMs": round(decode_ms, 1),
                         "text": text})
    model.close()
    return [summarise("nemotron-gguf-t4", rows)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sherpa", action="store_true")
    ap.add_argument("--out", default="")
    args = ap.parse_args()
    results = run_sherpa() if args.sherpa else run_gguf()
    for entry in results:
        print(json.dumps({k: v for k, v in entry.items() if k != "rows"}, ensure_ascii=False), flush=True)
    if args.out:
        path = HOME / args.out
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
        print("saved", path)


if __name__ == "__main__":
    main()
