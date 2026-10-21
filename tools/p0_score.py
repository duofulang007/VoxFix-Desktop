"""P0 计分：新跑出来的 run JSON × 安卓 sherpa-onnx 桌面对照基线（同一批 174 条）。

口径完全照抄安卓那轮（字 CER / 词 WER / 混说统一单元错误率、忽略标点与大小写、
数字 ITN 差异单列不计错），这样两条运行时的数字可以并排放。

用法: python tools/p0_score.py work/p0/cpu-t4.json [more.json ...]
"""
import json
import re
import statistics
import sys
import unicodedata
from pathlib import Path

HOME = Path(__file__).resolve().parent.parent
BASELINE = Path("D:/Projects/VoxFix/.android/nemotron-20261020/desktop-160ms.json")
PUNCT = re.compile(r"[\s，。、？！；：“”‘’（）《》〈〉…—·,.!?;:\"'()\[\]{}<>~`]+")
DIGITS = str.maketrans("０１２３４５６７８９", "0123456789")
CJK = re.compile(r"[\u3400-\u9fff]")

LABELS = {
    "long": "长句", "mixed": "中英混说", "everyday": "简单句", "negation": "否定词",
    "quiet": "轻声/噪声", "sync": "冷启动", "noise15": "轻声/噪声", "fast": "轻声/噪声",
    "pause600": "600ms 停顿", "number": "金额/数字", "short": "短词", "cold": "冷启动",
    "baseline": "普通话简单句", "cantonese": "粤语（公开样本）", "sentence": "英文整句",
    "silence": "静音", "word": "英文单词",
}


def normalize(text):
    return PUNCT.sub("", (text or "").translate(DIGITS).lower())


def units(text):
    parts = re.findall(r"[a-z0-9]+(?:'[a-z0-9]+)*|[^\s]", (text or "").translate(DIGITS).lower())
    return [part for part in parts if any(unicodedata.category(char)[0] in "LMN" for char in part)]


def distance(ref, hyp):
    previous = list(range(len(hyp) + 1))
    for i, token in enumerate(ref, 1):
        row = [i]
        for j, candidate in enumerate(hyp, 1):
            row.append(min(row[-1] + 1, previous[j] + 1, previous[j - 1] + (token != candidate)))
        previous = row
    return previous[-1]


def exact(expected, text):
    return units(expected) == units(text) if units(expected) else not (text or "").strip()


def pct(values, q):
    values = sorted(v for v in values if v is not None)
    return round(values[min(len(values) - 1, int(round(q * (len(values) - 1))))], 1) if values else None


def buckets(rows, text_of):
    table = {}
    for row in rows:
        name = LABELS.get(row["category"], row["category"])
        expected, got = units(row["expected"]), units(text_of(row))
        entry = table.setdefault(name, {"n": 0, "exact": 0, "errors": 0, "units": 0, "cer": [], "wer": []})
        entry["n"] += 1
        entry["exact"] += exact(row["expected"], text_of(row))
        entry["errors"] += distance(expected, got)
        entry["units"] += max(len(expected), 1)
        ref_chars = [c for c in normalize(row["expected"]) if CJK.match(c)]
        if ref_chars:
            entry["cer"].append(distance(ref_chars, [c for c in normalize(text_of(row)) if CJK.match(c)]) / len(ref_chars))
        ref_words = [token for token in units(row["expected"]) if re.match(r"[a-z0-9]", token)]
        if ref_words:
            entry["wer"].append(distance(ref_words, [token for token in units(text_of(row)) if re.match(r"[a-z0-9]", token)]) / len(ref_words))
    out = []
    for name, entry in sorted(table.items(), key=lambda kv: -kv[1]["n"]):
        out.append({
            "bucket": name, "n": entry["n"], "exact": entry["exact"],
            "exactRate": round(entry["exact"] / entry["n"], 3),
            "unitErrorRate": round(entry["errors"] / entry["units"], 4),
            "cer": round(statistics.mean(entry["cer"]), 4) if entry["cer"] else None,
            "wer": round(statistics.mean(entry["wer"]), 4) if entry["wer"] else None,
        })
    return out


def overall(rows, text_of):
    errors = sum(distance(units(r["expected"]), units(text_of(r))) for r in rows)
    total = sum(len(units(r["expected"])) for r in rows)
    return {
        "n": len(rows),
        "exact": sum(exact(r["expected"], text_of(r)) for r in rows),
        "exactRate": round(sum(exact(r["expected"], text_of(r)) for r in rows) / len(rows), 3),
        "unitErrorRate": round(errors / max(1, total), 4),
    }


def main():
    report = {"runs": {}}
    base = {row["id"]: row for row in json.loads(BASELINE.read_text(encoding="utf-8"))}

    for arg in sys.argv[1:]:
        data = json.loads((HOME / arg).read_text(encoding="utf-8"))
        rows = data["rows"]
        rtfs = [r["decodeMs"] / r["audioMs"] for r in rows]
        wall_rtf = sum(r["wallMs"] for r in rows) / sum(r["audioMs"] for r in rows)
        entry = {
            "backend": data["backend"], "threads": data["threads"],
            "language": data.get("language"), "loadMs": data.get("loadMs"),
            "overall": overall(rows, lambda r: r["text"]),
            "buckets": buckets(rows, lambda r: r["text"]),
            "decodeRtf": {"mean": round(statistics.mean(rtfs), 3), "p50": pct(rtfs, 0.5),
                          "p95": pct(rtfs, 0.95), "max": round(max(rtfs), 3)},
            "wallRtf": round(wall_rtf, 3),
            "punct": sum(1 for r in rows if re.search(r"[，。？！]", r["text"])),
        }
        # 与安卓桌面对照逐条对齐（同 id 同音频）
        wins = losses = 0
        for r in rows:
            ref = base.get(r["id"])
            if not ref:
                continue
            mine = units(r["expected"]) == units(r["text"])
            sherpa = units(ref["expected"]) == units(ref["nemotron"]["text"])
            if mine and not sherpa:
                wins += 1
            if sherpa and not mine:
                losses += 1
        entry["vsSherpaNemotron"] = {"gains": wins, "losses": losses}
        report["runs"][arg] = entry

        print(f"\n=== {arg}  {data['backend']} t{data['threads']} lang={data.get('language')} ===")
        print(" overall", json.dumps(entry["overall"], ensure_ascii=False))
        print(" decodeRTF", json.dumps(entry["decodeRtf"]), "wallRTF", entry["wallRtf"],
              "punct", entry["punct"], "/", len(rows), "vsSherpa", entry["vsSherpaNemotron"])
        for row in entry["buckets"]:
            print("   ", json.dumps(row, ensure_ascii=False))

    print("\n=== 基线（安卓那轮桌面对照，同 174 条）===")
    base_rows = list(base.values())
    for name in ("zipformer", "nemotron"):
        print(name, json.dumps(overall(base_rows, lambda r, n=name: r[n]["text"]), ensure_ascii=False))
    out = HOME / "work/p0/report.json"
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("saved", out)


if __name__ == "__main__":
    main()
