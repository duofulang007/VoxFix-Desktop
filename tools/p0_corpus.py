"""把安卓项目的 174 条固定语料（.f32 = 16k 单声道 float32 裸 PCM）转成 16-bit WAV。

语料清单以安卓那轮桌面对照实际跑过的 174 条为准（desktop-160ms.json 的 id 序列），
这样本轮 NeMo-Speech.cpp 的准确率可以直接和 sherpa-onnx 同一批音频对账。

用法: python tools/p0_corpus.py [--out work/p0/wav]
"""
import argparse
import json
import wave
from pathlib import Path

import numpy as np

HOME = Path(__file__).resolve().parent.parent
VOICE = Path("D:/Projects/VoxFix/.android/voice-quality-20261018")
REF = Path("D:/Projects/VoxFix/.android/nemotron-20261020/desktop-160ms.json")


def locate(case_id):
    for folder in (VOICE / "fixtures", VOICE / "mandarin-stress"):
        candidate = folder / (case_id + ".f32")
        if candidate.exists():
            return candidate
    return None


def to_wav(src: Path, dst: Path):
    pcm = np.clip(np.fromfile(src, dtype="<f4"), -1.0, 1.0)
    with wave.open(str(dst), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(16000)
        handle.writeframes((pcm * 32767.0).astype("<i2").tobytes())
    return pcm.size / 16000


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default="work/p0/wav")
    args = parser.parse_args()

    rows = json.loads(REF.read_text(encoding="utf-8"))
    out = HOME / args.out
    out.mkdir(parents=True, exist_ok=True)
    manifest, missing, total = [], [], 0.0
    for row in rows:
        src = locate(row["id"])
        if src is None:
            missing.append(row["id"])
            continue
        seconds = to_wav(src, out / (row["id"] + ".wav"))
        total += seconds
        manifest.append({"id": row["id"], "category": row["category"],
                         "expected": row["expected"], "seconds": round(seconds, 3),
                         "wav": str((out / (row["id"] + ".wav")).as_posix())})
    (HOME / "work/p0/manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"wav={len(manifest)} missing={len(missing)} audio={total:.1f}s")
    if missing:
        print("MISSING", missing)


if __name__ == "__main__":
    main()
