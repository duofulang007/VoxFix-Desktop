"""Decode-time hotword rescue for SenseVoice (CTC greedy, int8 graph).

The production path stays sherpa-onnx. When its text contains a fragment that
sounds like one of the user's words (`pyython` / `gitthub` / `J SON`), this
module re-decodes just that segment with CTC contextual biasing and keeps the
result only if it actually contains more of the user's words.

Feature contract (verified against sherpa-onnx 1.13.8 sources, see
work/hotword-prototype/P0-report.md):
  samples x 32768 -> 80-bin hamming fbank (25/10 ms, snip_edges, dither 0)
  -> LFR(7,6) centred on i*6 with edge clamping -> (x + neg_mean) * inv_stddev
  -> int8 graph -> greedy CTC, blank 0, drop the first 4 tag tokens.

Everything is lazy: onnxruntime and kaldi_native_fbank are imported, and the
session is created, only the first time a rescue is actually needed. The
session is released after `IDLE_SECONDS` without use so a rescue does not pin
an extra ~290 MB for the whole session.
"""
import re
import threading
import time
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
MODEL = ROOT / 'models/sensevoice/model.int8.onnx'
TOKENS = ROOT / 'models/sensevoice/tokens.txt'
SAMPLE_RATE = 16000

BOOST = 3.0           # logit bonus for the next hotword token (P1 sweep: 3.0 fixes JSON, >5 overshoots)
TAG_TOKENS = 4        # <|lang|>, <|EMO|>, <|event|>, <|itn|>
IDLE_SECONDS = 180    # release the extra ONNX session after this much silence

_ALNUM = re.compile(r'[a-z0-9]+')


def preimport():
    """Warm the heavy imports (not the session) so the first rescue is not a ~1 s hitch."""
    try:
        import kaldi_native_fbank  # noqa: F401
        import onnxruntime  # noqa: F401
    except Exception:
        pass


def compact(text):
    """Lowercase alphanumerics only, so `J SON` and `json` compare equal."""
    return re.sub(r'[^a-z0-9]+', '', text.lower())


def edit_distance(a, b, limit):
    """Levenshtein distance, giving up as soon as it exceeds `limit`."""
    if abs(len(a) - len(b)) > limit:
        return limit + 1
    previous = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        row = [i]
        best = i
        for j, cb in enumerate(b, 1):
            value = min(row[-1] + 1, previous[j] + 1, previous[j - 1] + (ca != cb))
            row.append(value)
            best = min(best, value)
        if best > limit:
            return limit + 1
        previous = row
    return previous[-1]


def _candidates(text):
    """Word-like runs and adjacent pairs, so `J SON` is still one candidate."""
    runs = _ALNUM.findall(text.lower())
    if not runs:
        return []
    return runs + [runs[i] + runs[i + 1] for i in range(len(runs) - 1)]


_VOWELS = frozenset('aeiouy')


def _skeleton(text):
    """Consonant skeleton, so `Jason` and `json` both collapse to `jsn`.

    Vowel confusion is the most common way short English acronyms go wrong
    (JSON -> Jason, WiFi -> wiifi); plain edit distance misses both because
    two characters differ. Only used for words of four characters or more so
    the skeletons stay discriminating (`API` would collapse to `p`).
    """
    return ''.join(char for char in text if char not in _VOWELS)


def near_miss(text, words):
    """Words the text nearly spells: `pyython`/python, `gitthub`/github, `Jason`/json.

    Only whole alphanumeric runs are compared (plus two adjacent runs), so an
    English word inside a longer word no longer triggers a rescue. One edit is
    allowed for short words, two for long ones, plus a consonant-skeleton match
    for words of four characters or more.
    """
    low = text.lower()
    candidates = _candidates(text)
    if not candidates:
        return []
    short_text = len(compact(text))
    found = []
    for word in words:
        target = compact(word)
        if not target:
            continue
        if target in low:
            continue  # already there, ignoring case; nothing to rescue
        limit = 2 if len(target) >= 8 else 1
        if any(edit_distance(candidate, target, limit) <= limit for candidate in candidates):
            found.append(word)
            continue
        if len(target) >= 4:
            bones = _skeleton(target)
            if len(bones) >= 2 and any(_skeleton(candidate) == bones for candidate in candidates):
                found.append(word)
                continue
        # Dictating a bare acronym ("API" heard as "a"): only when the whole
        # utterance is that fragment, so an article "a" in a sentence stays put.
        if short_text <= len(target) + 2 and any(
                target.startswith(candidate) and len(candidate) >= 1
                for candidate in candidates):
            found.append(word)
    return found


def hotword_hits(text, words):
    flat = compact(text)
    return sum(1 for word in words if compact(word) and compact(word) in flat)


def strong_hits(text, words):
    """Words spelled out contiguously, so `J SON` does not count as `JSON`."""
    low = text.lower()
    return sum(1 for word in words if compact(word) and compact(word) in low)


def load_pieces():
    pieces = {}   # raw piece -> id, tags excluded
    strings = {}  # id -> piece with ▁ mapped to a space
    with open(TOKENS, encoding='utf-8') as handle:
        for line in handle:
            line = line.rstrip('\n')
            if not line:
                continue
            piece, index = line.rsplit(' ', 1)
            index = int(index)
            strings[index] = piece.replace('\u2581', ' ')
            if not (piece.startswith('<') and piece.endswith('>')):
                pieces[piece] = index
    return pieces, strings


def _segmentations(text, pieces, extra=1, cap=64):
    """Every vocab segmentation of `text` within (minimal tokens + extra)."""
    size = len(text)
    best = [None] * (size + 1)
    best[0] = 0
    for i in range(size):
        if best[i] is None:
            continue
        for j in range(size, i, -1):
            if text[i:j] in pieces:
                candidate = best[i] + 1
                if best[j] is None or candidate < best[j]:
                    best[j] = candidate
    if best[size] is None:
        return []
    limit = best[size] + extra
    paths = []

    def walk(index, path):
        if len(paths) >= cap:
            return
        if index == size:
            paths.append(list(path))
            return
        for j in range(size, index, -1):
            piece = text[index:j]
            if piece in pieces and len(path) < limit:
                path.append(pieces[piece])
                walk(j, path)
                path.pop()

    walk(0, [])
    return paths


class _Node:
    __slots__ = ('children', 'word')

    def __init__(self):
        self.children = {}
        self.word = None


def build_trie(words, pieces):
    root = _Node()
    for word in words:
        variants = {word, word.lower(), '▁' + word, '▁' + word.lower()}
        for variant in variants:
            for path in _segmentations(variant, pieces):
                node = root
                for token in path:
                    node = node.children.setdefault(token, _Node())
                node.word = word
    return root


class Decoder:
    """Greedy CTC over the production int8 graph, with optional trie biasing."""

    def __init__(self):
        import onnxruntime as ort  # noqa: F401  (imported for the side effect of failing loudly)

        options = ort.SessionOptions()
        options.intra_op_num_threads = 4
        options.inter_op_num_threads = 1
        options.log_severity_level = 3
        # BASIC matches sherpa-onnx's own numbers; ALL shifts low-margin frames.
        options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_BASIC
        self.session = ort.InferenceSession(str(MODEL), options, providers=['CPUExecutionProvider'])
        meta = self.session.get_modelmeta().custom_metadata_map
        self.m = int(meta['lfr_window_size'])
        self.n = int(meta['lfr_window_shift'])
        self.mean = np.array([float(v) for v in meta['neg_mean'].split(',')], dtype=np.float32)
        self.std = np.array([float(v) for v in meta['inv_stddev'].split(',')], dtype=np.float32)
        self.language = np.array([int(meta['lang_auto'])], dtype=np.int32)
        self.text_norm = np.array([int(meta['without_itn'])], dtype=np.int32)
        self.scale = 1.0 if int(meta.get('normalize_samples', '1')) else 32768.0
        self.pieces, self.strings = load_pieces()

    def close(self):
        self.session = None

    def features(self, samples):
        import kaldi_native_fbank as knf

        options = knf.FbankOptions()
        options.frame_opts.samp_freq = SAMPLE_RATE
        options.frame_opts.frame_length_ms = 25
        options.frame_opts.frame_shift_ms = 10
        options.frame_opts.dither = 0.0
        options.frame_opts.preemph_coeff = 0.97
        options.frame_opts.remove_dc_offset = True
        options.frame_opts.window_type = 'hamming'
        options.frame_opts.round_to_power_of_two = True
        options.frame_opts.snip_edges = True
        options.mel_opts.num_bins = 80
        options.mel_opts.low_freq = 20.0
        options.mel_opts.high_freq = 0.0
        options.use_energy = False
        options.use_log_fbank = True
        options.use_power = True

        extractor = knf.OnlineFbank(options)
        extractor.accept_waveform(SAMPLE_RATE, (samples * self.scale).astype(np.float32))
        extractor.input_finished()
        total = extractor.num_frames_ready
        if total == 0:
            return np.empty((0, 80 * self.m), dtype=np.float32)
        frames = np.stack([extractor.get_frame(i) for i in range(total)]).reshape(-1, 80)

        count = 1 + (frames.shape[0] - 1) // self.n
        left = (self.m - 1) // 2
        stacked = np.empty((count, 80 * self.m), dtype=np.float32)
        for i in range(count):
            centre = i * self.n
            for j in range(self.m):
                index = min(frames.shape[0] - 1, max(0, centre - left + j))
                stacked[i, j * 80:(j + 1) * 80] = frames[index]
        return (stacked + self.mean) * self.std

    def greedy(self, samples, trie=None, boost=BOOST):
        feats = self.features(samples)
        x = feats[None, ...]
        logits = self.session.run(None, {
            'x': x,
            'x_length': np.array([feats.shape[0]], dtype=np.int32),
            'language': self.language,
            'text_norm': self.text_norm,
        })[0][0]

        ids = []
        previous = -1
        active = [trie] if trie is not None else []
        for frame in range(logits.shape[0]):
            row = logits[frame]
            if frame < TAG_TOKENS or not active:
                token = int(row.argmax())
            else:
                biased = row.copy()
                for node in active:
                    for child in node.children:
                        biased[child] += boost
                token = int(biased.argmax())
            if token and token != previous:
                ids.append(token)
                if trie is not None:
                    # A branch dies silently, but matching from the root always
                    # restarts, so a word can begin at any frame and the state
                    # recovers after an unrelated token.
                    following = []
                    for node in active:
                        child = node.children.get(token)
                        if child is not None:
                            following.append(child)
                    start = trie.children.get(token)
                    if start is not None and start not in following:
                        following.append(start)
                    active = following
            previous = token
        return ''.join(self.strings[i] for i in ids[TAG_TOKENS:])


class HotwordRescue:
    """Re-decode a segment with hotword biasing, but only when it looks worth it."""

    def __init__(self):
        self.lock = threading.Lock()
        self.decoder = None
        self.trie = None
        self.words = ()
        self.pieces = None
        self.released = None
        self.timer = None
        self.stats = {'attempts': 0, 'rescued': 0, 'failed': 0}

    def _schedule_release(self):
        if self.timer is not None:
            self.timer.cancel()
        self.timer = threading.Timer(IDLE_SECONDS, self.release)
        self.timer.daemon = True
        self.timer.start()

    def release(self):
        with self.lock:
            if self.timer is not None:
                self.timer.cancel()
                self.timer = None
            if self.decoder is not None:
                try:
                    self.decoder.close()
                except Exception:
                    pass
                self.decoder = None
                self.trie = None

    def rescore(self, samples, text, words):
        """Return `text`, or the biased transcript when it gains a user word."""
        words = tuple(word for word in words if isinstance(word, str) and word.strip())
        if not words or not text:
            return text
        if not near_miss(text, words):
            return text
        self.stats['attempts'] += 1
        try:
            with self.lock:
                if not np.any(samples):
                    return text
                if self.decoder is None:
                    self.decoder = Decoder()
                    self.trie = None
                if self.trie is None or self.words != words:
                    self.pieces = self.pieces or self.decoder.pieces
                    self.trie = build_trie(words, self.pieces)
                    self.words = words
                biased = self.decoder.greedy(samples, self.trie)
                self.released = time.monotonic()
                self._schedule_release()
        except Exception:
            self.stats['failed'] += 1
            self.release()
            return text
        # Accept only if the rescue spells a word out properly ("J SON" -> "JSON")
        # or at least puts more of the word list into the text; never fewer.
        gained = strong_hits(biased, words) - strong_hits(text, words)
        if gained < 0:
            return text
        if gained == 0 and hotword_hits(biased, words) <= hotword_hits(text, words):
            return text
        if len(biased) < 0.6 * len(text):
            return text
        self.stats['rescued'] += 1
        return biased
