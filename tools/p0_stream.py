"""P0 流式（真实节奏）测首字与追帧能力：160ms 分块、按 20ms 真实节奏投递。

和安卓那轮同一条纪律：不允许整段灌入（那样测出来的 RTF 没有意义）。
每一帧都按 wall clock 对齐音频进度；落后就记 lag，说明这条链路追不上实时。

用法:
  python tools/p0_stream.py --backend cpu --threads 4 --per-cat 2 --out work/p0/stream-cpu.json
"""
import argparse
import json
import os
import statistics
import time
import wave
from pathlib import Path

import numpy as np

HOME = Path(__file__).resolve().parent.parent
MANIFEST = HOME / "work/p0/manifest.json"
LIB = HOME / "runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll"
os.environ.setdefault("TRANSCRIBE_LIBRARY", str(LIB))

import transcribe_cpp  # noqa: E402

TICK_MS = 20
CHUNK_MS = 160


def read_wav(path):
    with wave.open(str(path), "rb") as handle:
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
    ap.add_argument("--per-cat", type=int, default=2)
    ap.add_argument("--chunk-ms", type=int, default=CHUNK_MS)
    ap.add_argument("--att-right", type=int, default=-1,
                    help="Parakeet cache-aware 右上下文；-1 = 运行时默认，0 = 不预留")
    ap.add_argument("--out", default="work/p0/stream.json")
    args = ap.parse_args()

    rows = json.loads(MANIFEST.read_text(encoding="utf-8"))
    picked, seen = [], {}
    for row in rows:
        if seen.get(row["category"], 0) >= args.per_cat:
            continue
        seen[row["category"]] = seen.get(row["category"], 0) + 1
        picked.append(row)

    model = transcribe_cpp.Model(HOME / args.model, backend=args.backend)
    session = model.session(n_threads=args.threads)
    family = (None if args.att_right < 0
              else transcribe_cpp.ParakeetStreamOptions(att_context_right=args.att_right))
    step = int(args.chunk_ms / 1000 * 16000)
    tick = int(TICK_MS / 1000 * 16000)
    out = []

    with session as sess:
        for index, row in enumerate(picked, 1):
            pcm = read_wav(Path(row["wav"]))
            audio_ms = pcm.size / 16.0
            first_ms, text, lag_ms, compute_ms = -1.0, "", 0.0, 0.0
            started = time.perf_counter()
            with sess.stream(language=args.language or None, timestamps="none",
                             family=family) as stream:
                fed = 0
                while fed < pcm.size:
                    end = min(fed + step, pcm.size)
                    batch = pcm[fed:end]
                    # 按真实节奏投递：拆成 20ms 小步，边等边判首字
                    offset = 0
                    while offset < batch.size:
                        sub = batch[offset:offset + tick]
                        t0 = time.perf_counter()
                        stream.feed(sub)
                        compute_ms += (time.perf_counter() - t0) * 1000
                        offset += sub.size
                        fed_so_far = fed + offset
                        target = started + fed_so_far / 16000
                        wait = target - time.perf_counter()
                        if wait > 0:
                            time.sleep(wait)
                        else:
                            lag_ms = max(lag_ms, -wait * 1000)
                        if first_ms < 0:
                            view = stream.text()
                            if (view.committed + view.tentative).strip():
                                first_ms = fed_so_far / 16.0
                    fed = end
                stream.finalize()
                text = stream.text().full.strip()
            wall_ms = (time.perf_counter() - started) * 1000
            out.append({"id": row["id"], "category": row["category"], "expected": row["expected"],
                        "text": text, "audioMs": round(audio_ms, 1),
                        "firstTokenMs": round(first_ms, 1), "wallMs": round(wall_ms, 1),
                        "computeMs": round(compute_ms, 1), "lagMs": round(lag_ms, 1),
                        "rtf": round(compute_ms / audio_ms, 3),
                        "lagRatio": round(lag_ms / audio_ms, 3)})
            print(f"{index}/{len(picked)} {row['id']:<26} first={first_ms:7.1f}ms "
                  f"rtf={compute_ms / audio_ms:.3f} lag={lag_ms:6.0f}ms", flush=True)

    path = HOME / args.out
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({"backend": args.backend, "threads": args.threads,
                                "chunkMs": args.chunk_ms, "attRight": args.att_right,
                                "rows": out},
                               ensure_ascii=False, indent=2), encoding="utf-8")
    firsts = [r["firstTokenMs"] for r in out if r["firstTokenMs"] > 0]
    print(json.dumps({
        "n": len(out),
        "firstP50": pct(firsts, 0.5), "firstP95": pct(firsts, 0.95), "firstMax": round(max(firsts), 1),
        "rtfP50": pct([r["rtf"] for r in out], 0.5), "rtfP95": pct([r["rtf"] for r in out], 0.95),
        "realtimeFrac": round(sum(1 for r in out if r["rtf"] < 1.0) / len(out), 3),
        "maxLagMs": round(max(r["lagMs"] for r in out), 0),
        "out": str(path),
    }, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
