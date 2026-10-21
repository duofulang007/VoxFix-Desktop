"""P0 桌面对照：Nemotron 3.5 (GGUF Q8_0) 在这台 9700X 上的离线准确率与解码耗时。

同一批 174 条固定音频（work/p0/wav，由 tools/p0_corpus.py 从安卓语料转出），
口径与安卓那轮 sherpa-onnx 桌面对照完全一致，便于直接 A/B。

用法:
  python tools/p0_eval.py --backend cpu --threads 4 --out work/p0/cpu-t4.json
  python tools/p0_eval.py --backend vulkan --threads 4 --out work/p0/vk-t4.json
"""
import argparse
import json
import os
import statistics
import sys
import time
import wave
from pathlib import Path

import numpy as np

HOME = Path(__file__).resolve().parent.parent
MANIFEST = HOME / "work/p0/manifest.json"
LIB = HOME / "runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll"
os.environ.setdefault("TRANSCRIBE_LIBRARY", str(LIB))

import transcribe_cpp  # noqa: E402


def read_wav(path):
    with wave.open(str(path), "rb") as handle:
        assert handle.getframerate() == 16000 and handle.getnchannels() == 1
        frames = handle.readframes(handle.getnframes())
    return np.frombuffer(frames, dtype="<i2").astype(np.float32) / 32768.0


def pct(values, q):
    values = sorted(v for v in values if v is not None)
    if not values:
        return None
    return round(values[min(len(values) - 1, int(round(q * (len(values) - 1))))], 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf")
    ap.add_argument("--backend", default="cpu")
    ap.add_argument("--threads", type=int, default=4)
    ap.add_argument("--language", default="")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--out", default="work/p0/run.json")
    args = ap.parse_args()

    rows = json.loads(MANIFEST.read_text(encoding="utf-8"))
    if args.limit:
        rows = rows[: args.limit]

    load_started = time.perf_counter()
    model = transcribe_cpp.Model(HOME / args.model, backend=args.backend)
    load_ms = (time.perf_counter() - load_started) * 1000
    caps = model.capabilities
    print(f"load {load_ms:.0f}ms backend={model.backend} variant={model.variant} "
          f"streaming={caps.supports_streaming} langs={len(caps.languages)}", flush=True)

    session = model.session(n_threads=args.threads)
    out = []
    with session as sess:
        for index, row in enumerate(rows, 1):
            pcm = read_wav(Path(row["wav"]))
            audio_ms = pcm.size / 16.0
            started = time.perf_counter()
            result = sess.run(pcm, language=args.language or None)
            wall_ms = (time.perf_counter() - started) * 1000
            out.append({
                "id": row["id"], "category": row["category"], "expected": row["expected"],
                "seconds": row["seconds"], "text": result.text, "rawText": result.raw_text,
                "language": result.language,
                "loadMs": round(load_ms), "wallMs": round(wall_ms, 1),
                "melMs": round(result.timings.mel_ms, 1),
                "encodeMs": round(result.timings.encode_ms, 1),
                "decodeMs": round(result.timings.decode_ms, 1),
                "audioMs": round(audio_ms, 1),
            })
            if index % 10 == 0 or index == len(rows):
                tail = out[-10:]
                rtf = sum(r["decodeMs"] for r in tail) / sum(r["audioMs"] for r in tail)
                print(f"{index}/{len(rows)} rtf(recent10)={rtf:.3f} {row['id']}", flush=True)

    path = HOME / args.out
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "backend": args.backend, "threads": args.threads, "language": args.language or "auto",
        "model": args.model, "loadMs": round(load_ms), "rows": out,
    }
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")

    rtfs = [r["decodeMs"] / r["audioMs"] for r in out]
    print(json.dumps({
        "rows": len(out),
        "rtfMean": round(statistics.mean(rtfs), 3),
        "rtfP50": pct(rtfs, 0.5), "rtfP95": pct(rtfs, 0.95), "rtfMax": round(max(rtfs), 3),
        "decodeMsMean": round(statistics.mean(r["decodeMs"] for r in out), 1),
        "totalAudioSec": round(sum(r["audioMs"] for r in out) / 1000, 1),
        "totalWallSec": round(sum(r["wallMs"] for r in out) / 1000, 1),
        "out": str(path),
    }, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    sys.exit(main())
