"""VoxFix Desktop: deterministic local corpus, actual streaming handler, paired controls."""
import base64
from collections import Counter
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys
import time
import wave

import numpy as np
import psutil

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "work/chinese-stress-20261020"
OUT.mkdir(exist_ok=True)
sys.path.insert(0, str(ROOT / "app"))
from recognize import Recognition
from p0_score import units, distance

if "--baseline-snapshot" in sys.argv:
    snapshot = OUT / "VoxFix Desktop-recognize-before.py"
    specification = importlib.util.spec_from_file_location("stress_baseline", snapshot)
    baseline_module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(baseline_module)
    Recognition = baseline_module.Recognition

os.environ["TRANSCRIBE_LIBRARY"] = str(ROOT / "runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll")
import transcribe_cpp as tc

MODEL = ROOT / "models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf"
process = psutil.Process()
rows = []
manifest = json.loads((ROOT / "work/p0/manifest.json").read_text(encoding="utf-8"))
cache = {}
for row in manifest:
    with wave.open(row["wav"], "rb") as audio:
        assert (audio.getnchannels(), audio.getframerate(), audio.getsampwidth()) == (1, 16000, 2)
        cache[row["id"]] = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2").astype(np.float32) / 32768


def score(items):
    ref_count = sum(len(units(r["expected"])) for r in items)
    return {"n": len(items), "exact": sum(units(r["expected"]) == units(r["text"]) for r in items),
            "unitErrors": sum(distance(units(r["expected"]), units(r["text"])) for r in items),
            "referenceUnits": ref_count,
            "unitErrorRate": round(sum(distance(units(r["expected"]), units(r["text"])) for r in items) / max(1, ref_count), 5),
            "errors": sum(bool(r.get("error")) for r in items),
            "nonPrefixFinal": sum(bool(r.get("nonPrefixFinal")) for r in items),
            "sampleMismatch": sum(bool(r.get("sampleMismatch")) for r in items)}


def save(entry):
    rows.append(entry)
    with (OUT / "results.jsonl").open("a", encoding="utf-8") as output:
        output.write(json.dumps(entry, ensure_ascii=False) + "\n")
    if len(rows) % 25 == 0:
        print(json.dumps({"completed": len(rows), "group": entry["group"], "rssMB": round(process.memory_info().rss / 1e6)}, ensure_ascii=True), flush=True)


def run(recognition, row, audio, group, pattern=(1600,), paced=False):
    identity = "stress-" + str(len(rows))
    pcm = (np.clip(audio, -1, 1) * 32767).astype("<i2")
    entry = {"id": row["id"], "category": row["category"], "expected": row["expected"],
             "group": group, "seconds": round(len(pcm) / 16000, 4), "text": "", "updates": 0,
             "firstAudioMs": None, "firstWallMs": None, "paced": paced}
    began = time.perf_counter()
    partial, fed, step = "", 0, 0
    try:
        recognition.handle({"op": "start", "id": identity})
        while fed < len(pcm):
            end = min(fed + pattern[step % len(pattern)], len(pcm))
            if paced:
                time.sleep(max(0, began + end / 16000 - time.perf_counter()))
            result = recognition.handle({"op": "audio", "id": identity,
                                        "pcm": base64.b64encode(pcm[fed:end].tobytes()).decode("ascii")})
            if result["text"] != partial:
                entry["updates"] += 1
            partial = result["text"]
            if partial and entry["firstAudioMs"] is None:
                entry["firstAudioMs"] = round(end / 16, 1)
                if paced:
                    entry["firstWallMs"] = round((time.perf_counter() - began) * 1000, 1)
            fed = end
            step += 1
        before_stop = time.perf_counter()
        result = recognition.handle({"op": "stop", "id": identity})
        entry.update(text=result["text"], partialBeforeStop=partial,
                     nonPrefixFinal=not result["text"].startswith(partial),
                     sampleMismatch=result["samples"] != len(pcm),
                     stopMs=round((time.perf_counter() - before_stop) * 1000, 2))
        entry["deliveredText"] = result["text"] if not entry["nonPrefixFinal"] else partial
    except Exception as error:
        entry.update(error=str(error), text=partial, deliveredText=partial)
        recognition.reset()
    entry["wallMs"] = round((time.perf_counter() - began) * 1000, 2)
    save(entry)
    return entry


class LanguageSession:
    def __init__(self, session): self.session = session
    def stream(self, **options): return self.session.stream(language="zh-CN", **options)


def main():
    (OUT / "results.jsonl").write_text("", encoding="utf-8")
    corpus_hash = hashlib.sha256((ROOT / "work/p0/manifest.json").read_bytes()).hexdigest()
    chinese = [r for r in manifest if r["category"] != "cantonese" and re.search(r"[\u3400-\u9fff]", r["expected"])]
    core = [r for r in chinese if r["category"] in ("baseline", "mixed", "everyday", "negation", "number", "short")]
    print(json.dumps({"base": len(manifest), "mandarinRows": len(chinese), "core": len(core),
                      "distinctMandarinReferences": len(set(r["expected"] for r in chinese)),
                      "corpus": "fixed Microsoft synthetic Mandarin/English, separate public Cantonese"}), flush=True)
    rss_before = process.memory_info().rss
    started = time.perf_counter()
    controls = {"cancel": 0, "empty": 0, "invalid": 0, "failures": []}
    rng = np.random.default_rng(20261020)
    with tc.Model(MODEL, backend="cpu") as model:
        with model.session(n_threads=4) as session:
            production = Recognition(session)
            for row in manifest:
                run(production, row, cache[row["id"]], "baseline-stream")
            for group in ("chunk20", "chunk320", "jitter", "quiet", "noise20", "noise10", "speed120", "leading800", "midpause600", "clip250"):
                for row in core:
                    audio = cache[row["id"]]
                    pattern = (1600,)
                    if group == "chunk20": pattern = (320,)
                    elif group == "chunk320": pattern = (5120,)
                    elif group == "jitter": pattern = (320, 640, 1600, 3200, 960)
                    elif group == "quiet": audio = audio * 0.05
                    elif group.startswith("noise"):
                        snr = int(group[5:])
                        rms = np.sqrt(np.mean(audio ** 2))
                        audio = np.clip(audio + rng.normal(0, rms / 10 ** (snr / 20), len(audio)), -1, 1)
                    elif group == "speed120": audio = np.interp(np.arange(0, len(audio), 1.2), np.arange(len(audio)), audio)
                    elif group == "leading800": audio = np.concatenate([np.zeros(12800), audio, np.zeros(8000)])
                    elif group == "midpause600": audio = np.concatenate([audio[:len(audio)//2], np.zeros(9600), audio[len(audio)//2:]])
                    elif group == "clip250": audio = np.clip(audio * 2.5, -1, 1)
                    run(production, row, audio, group, pattern)
            for row in chinese:
                before = time.perf_counter()
                result = session.run(cache[row["id"]])
                save({"id": row["id"], "category": row["category"], "group": "offline-control", "expected": row["expected"], "text": result.text,
                      "wallMs": round((time.perf_counter() - before) * 1000, 2), "seconds": row["seconds"]})
            preferred = Recognition(LanguageSession(session))
            for row in chinese:
                run(preferred, row, cache[row["id"]], "zh-CN-control")
            for i in range(30):
                level = (0, 0.001, 0.01, 0.05, 0.1)[i % 5]
                run(production, {"id": "noise-only-" + str(i), "category": "silence", "expected": ""},
                    rng.normal(0, level, 32000), "non-speech")
            sentence = next(r for r in core if r["id"] == "plain-zh-0")
            source = cache[sentence["id"]]
            for duration in (30, 90, 179.9):
                pieces, references, count = [], [], 0
                while count + len(source) + 8000 < duration * 16000:
                    pieces.extend([source, np.zeros(8000)]); references.append(sentence["expected"]); count += len(source) + 8000
                audio = np.concatenate(pieces + [np.zeros(int(duration * 16000) - count)])
                run(production, {"id": "long-" + str(duration), "category": "long-repetition", "expected": "".join(references)}, audio, "long-session")
            for index in range(50):
                for operation in ("cancel", "stop"):
                    identity = "control-" + str(index)
                    try:
                        production.handle({"op": "start", "id": identity})
                        production.handle({"op": operation, "id": identity})
                        controls["cancel" if operation == "cancel" else "empty"] += 1
                    except Exception as error:
                        controls["failures"].append(str(error)); production.reset()
                production.handle({"op": "start", "id": "invalid"})
                try:
                    production.handle({"op": "audio", "id": "invalid", "pcm": "!bad!"})
                    controls["failures"].append("invalid PCM accepted")
                except ValueError: controls["invalid"] += 1
                finally: production.reset()
            for row in core[:8]:
                run(production, row, cache[row["id"]], "paced", paced=True)
    groups = {group: score([r for r in rows if r["group"] == group]) for group in sorted(set(r["group"] for r in rows))}
    baseline = [r for r in rows if r["group"] == "baseline-stream"]
    summary = {"model": str(MODEL), "manifestSHA256": corpus_hash,
               "recognitionRuns": len(rows), "controlRuns": sum(controls[k] for k in ("cancel", "empty", "invalid")),
               "uniqueBaseAudio": len(manifest), "distinctMandarinReferences": len(set(r["expected"] for r in chinese)),
               "groups": groups, "controls": controls,
               "baselineCategories": {category: score([r for r in baseline if r["category"] == category]) for category in sorted(set(r["category"] for r in baseline))},
               "mandarinBaseline": score([r for r in baseline if r["id"] in {x["id"] for x in chinese}]),
               "elapsedSeconds": round(time.perf_counter() - started, 2),
               "rssBeforeMB": round(rss_before / 1e6), "rssAfterModelCloseMB": round(process.memory_info().rss / 1e6)}
    (OUT / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=True), flush=True)


if __name__ == "__main__":
    main()
