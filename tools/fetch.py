"""并行分块下载 HF 大文件 + SHA-256 校验。

用法:
  python tools/fetch.py <repo> <filename> <out_dir> [--conn 8]

hf-mirror 单连接约 1.2MB/s，8 路 Range 并发通常到 6-9MB/s。
先写 .part 分块，全部完成再拼接，最后校验完整 SHA-256（来自 HF API 的 lfs.oid）。
"""
import argparse
import hashlib
import json
import os
import sys
import threading
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

MIRROR = os.environ.get("HF_ENDPOINT", "https://hf-mirror.com")
UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) curl/8.9.1"


def api_tree(repo):
    url = f"{MIRROR}/api/models/{repo}/tree/main"
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.load(r)


def fetch_range(url, start, end, path, attempts=4):
    for i in range(attempts):
        try:
            req = urllib.request.Request(url, headers={"Range": f"bytes={start}-{end}", "User-Agent": UA})
            with urllib.request.urlopen(req, timeout=120) as r, open(path, "wb") as f:
                while True:
                    buf = r.read(1 << 20)
                    if not buf:
                        break
                    f.write(buf)
            got = os.path.getsize(path)
            if got == end - start + 1:
                return got
            raise IOError(f"short read {got}/{end - start + 1}")
        except Exception as exc:  # noqa: BLE001
            if i == attempts - 1:
                raise
            time.sleep(1.5 * (i + 1))
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("repo")
    ap.add_argument("filename")
    ap.add_argument("out_dir")
    ap.add_argument("--conn", type=int, default=8)
    a = ap.parse_args()

    entry = next((f for f in api_tree(a.repo) if f["path"] == a.filename), None)
    if entry is None:
        sys.exit(f"not found: {a.filename}")
    size = entry["size"]
    sha = entry.get("lfs", {}).get("oid", "")
    out = Path(a.out_dir) / a.filename
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists() and out.stat().st_size == size:
        print(f"exists {out} {size}")
        return
    url = f"{MIRROR}/{a.repo}/resolve/main/{a.filename}"
    chunk = (size + a.conn - 1) // a.conn
    parts = [(i, i * chunk, min(size - 1, (i + 1) * chunk - 1)) for i in range(a.conn)]
    t0 = time.time()
    lock = threading.Lock()
    done = [0]

    def job(p):
        i, s, e = p
        got = fetch_range(url, s, e, f"{out}.part{i}")
        with lock:
            done[0] += got
            pct = done[0] / size * 100
            spd = done[0] / max(1e-6, time.time() - t0) / 1e6
            print(f"  {pct:5.1f}%  {spd:4.1f} MB/s", flush=True)
        return got

    with ThreadPoolExecutor(a.conn) as pool:
        total = sum(pool.map(job, parts))
    if total != size:
        sys.exit(f"size mismatch {total} != {size}")
    with open(out, "wb") as dst:
        for i, _, _ in parts:
            with open(f"{out}.part{i}", "rb") as src:
                while True:
                    buf = src.read(1 << 22)
                    if not buf:
                        break
                    dst.write(buf)
            os.remove(f"{out}.part{i}")
    if sha:
        h = hashlib.sha256()
        with open(out, "rb") as f:
            for buf in iter(lambda: f.read(1 << 22), b""):
                h.update(buf)
        ok = h.hexdigest() == sha.split(":")[-1]
        print(f"sha256 {'OK' if ok else 'MISMATCH'} {h.hexdigest()}")
        if not ok:
            sys.exit(1)
    print(f"done {out} {size/1e6:.0f}MB in {time.time()-t0:.0f}s")


if __name__ == "__main__":
    main()
