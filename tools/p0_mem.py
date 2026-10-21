"""P0 常驻成本：模型加载后 / 跑完长音频后的进程工作集（Windows 实测）。

用法: python tools/p0_mem.py --backend cpu --threads 4
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import json
import os
import wave
from pathlib import Path

import numpy as np

HOME = Path(__file__).resolve().parent.parent
LIB = HOME / "runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll"
os.environ.setdefault("TRANSCRIBE_LIBRARY", str(LIB))

import transcribe_cpp  # noqa: E402


class COUNTERS(ctypes.Structure):
    _fields_ = [("cb", wt.DWORD), ("PageFaultCount", wt.DWORD),
                ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
                ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t), ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t)]


def mem():
    counters = COUNTERS()
    counters.cb = ctypes.sizeof(counters)
    ctypes.windll.psapi.GetProcessMemoryInfo(
        ctypes.windll.kernel32.GetCurrentProcess(), ctypes.byref(counters), counters.cb)
    return {"rssMB": round(counters.WorkingSetSize / 2**20, 1),
            "peakRssMB": round(counters.PeakWorkingSetSize / 2**20, 1),
            "commitMB": round(counters.PagefileUsage / 2**20, 1)}


def read_wav(path):
    with wave.open(str(path), "rb") as handle:
        frames = handle.readframes(handle.getnframes())
    return np.frombuffer(frames, dtype="<i2").astype(np.float32) / 32768.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf")
    ap.add_argument("--backend", default="cpu")
    ap.add_argument("--threads", type=int, default=4)
    args = ap.parse_args()

    steps = {"baseline": mem()}
    model = transcribe_cpp.Model(HOME / args.model, backend=args.backend)
    steps["afterLoad"] = mem()
    session = model.session(n_threads=args.threads)
    with session as sess:
        for name in ("long-2-600", "long-1-600", "plain-zh-0"):
            sess.run(read_wav(HOME / f"work/p0/wav/{name}.wav"))
            steps["after_" + name] = mem()
        busiest = mem()
    session.close()
    model.close()
    steps["afterClose"] = mem()
    print(json.dumps({"backend": args.backend, "steps": steps, "steadyPeak": busiest},
                     ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
