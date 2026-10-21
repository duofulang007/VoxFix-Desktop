"""Paired probes separating leading silence, trailing context, and finalization."""
import json
import time
import numpy as np
import chinese_stress as h


class PaddedStream:
    def __init__(self, stream, samples=8000): self.stream, self.samples = stream, samples
    def feed(self, pcm): return self.stream.feed(pcm)
    def text(self): return self.stream.text()
    def reset(self): return self.stream.reset()
    def finalize(self):
        self.stream.feed(np.zeros(self.samples, dtype=np.float32))
        return self.stream.finalize()


class PaddedSession:
    def __init__(self, session, samples=8000): self.session, self.samples = session, samples
    def stream(self, **options): return PaddedStream(self.session.stream(**options), self.samples)


def main():
    h.OUT = h.ROOT / "work/chinese-tail-controls-20261020"
    h.OUT.mkdir(exist_ok=True)
    (h.OUT / "results.jsonl").write_text("", encoding="utf-8")
    chinese = [r for r in h.manifest if r["category"] != "cantonese" and h.re.search(r"[\u3400-\u9fff]", r["expected"])]
    core = [r for r in chinese if r["category"] in ("baseline", "mixed", "everyday", "negation", "number", "short")]
    began = time.perf_counter()
    with h.tc.Model(h.MODEL, backend="cpu") as model:
        with model.session(n_threads=4) as session:
            for group in ("lead-only800", "tail-only320", "tail-only500"):
                recognition = h.Recognition(session)
                for row in core:
                    audio = h.cache[row["id"]]
                    if group == "lead-only800": audio = np.concatenate([np.zeros(12800), audio])
                    else: audio = np.concatenate([audio, np.zeros(5120 if group.endswith("320") else 8000)])
                    h.run(recognition, row, audio, group)
            # Same production handler and input samples; only finalizer gains right-context padding.
            for samples, group in ((8000, "finalize-pad500"), (5120, "finalize-pad320")):
                padded = h.Recognition(PaddedSession(session, samples))
                for row in h.manifest:
                    h.run(padded, row, h.cache[row["id"]], group)
            for row in h.manifest:
                if row["category"] in ("word", "sentence"):
                    h.run(h.Recognition(h.LanguageSession(session)), row, h.cache[row["id"]], "zh-CN-english")
    summary = {"runs": len(h.rows), "elapsedSeconds": round(time.perf_counter()-began, 2),
               "groups": {g:h.score([r for r in h.rows if r["group"]==g]) for g in sorted(set(r["group"] for r in h.rows))}}
    (h.OUT / "summary.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps(summary,ensure_ascii=True),flush=True)


if __name__ == "__main__": main()
