"""把安卓项目的 .f32 (16k 单声道 float32 裸 PCM) 转成 16-bit WAV，供桌面运行时使用。

用法: python tools/make_wav.py <src_dir> <dst_dir> [--only id1,id2]
"""
import argparse
import wave
from pathlib import Path

import numpy as np


def convert(src: Path, dst: Path):
    pcm = np.fromfile(src, dtype="<f4")
    pcm = np.clip(pcm, -1.0, 1.0)
    dst.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(dst), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(16000)
        w.writeframes((pcm * 32767.0).astype("<i2").tobytes())
    return pcm.size / 16000


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    only = {s for s in a.only.split(",") if s}
    src_dir, dst_dir = Path(a.src), Path(a.dst)
    rows = []
    for f in sorted(src_dir.glob("*.f32")):
        if only and f.stem not in only:
            continue
        sec = convert(f, dst_dir / (f.stem + ".wav"))
        rows.append((f.stem, round(sec, 2)))
    total = sum(r[1] for r in rows)
    print(f"{len(rows)} files, {total:.1f}s audio -> {dst_dir}")
    for name, sec in rows:
        print(f"  {name:24s} {sec:5.2f}s")


if __name__ == "__main__":
    main()
