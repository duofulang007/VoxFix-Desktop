"""Shared ASR scoring: Chinese characters, English words, and silence protection."""

import re
import unicodedata


DIGITS = str.maketrans("０１２３４５６７８９", "0123456789")


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


def overall(rows, text_of):
    errors = sum(distance(units(r["expected"]), units(text_of(r))) for r in rows)
    total = sum(len(units(r["expected"])) for r in rows)
    return {
        "n": len(rows),
        "exact": sum(exact(r["expected"], text_of(r)) for r in rows),
        "exactRate": round(sum(exact(r["expected"], text_of(r)) for r in rows) / len(rows), 3),
        "unitErrorRate": round(errors / max(1, total), 4),
    }
