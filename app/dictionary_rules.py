"""Shared text-only dictionary rules and the editor's read-only preview."""
import json
import re
import sys

HOTWORD_LIMIT = 64


def dictionary_pattern(variant):
    if not isinstance(variant, str) or not variant.strip():
        return None
    variant = variant.strip()
    escaped = re.escape(variant)
    if all(char.isascii() and (char.isalnum() or char.isspace() or char == '_') for char in variant):
        # Word edges keep "get up" from matching inside "forget upper".
        escaped = r'(?<![A-Za-z0-9_])' + escaped + r'(?![A-Za-z0-9_])'
    return escaped


def compile_dictionary(data):
    rules = data.get('rules') if isinstance(data, dict) else data
    if not isinstance(rules, list):
        return ()
    compiled = []
    for rule in rules:
        if not isinstance(rule, dict):
            continue
        canonical, variants = rule.get('to'), rule.get('from')
        if not isinstance(canonical, str) or not canonical or not isinstance(variants, list):
            continue
        # Prefer the complete phrase when one saved spelling prefixes another.
        variants = sorted((v for v in variants if isinstance(v, str)),
                          key=lambda v: len(v.strip()), reverse=True)
        patterns = [pattern for pattern in map(dictionary_pattern, variants) if pattern]
        if patterns:
            compiled.append((re.compile('|'.join(patterns), re.IGNORECASE), canonical,
                             rule.get('context') == 'zh'))
    return tuple(compiled)


def load_dictionary(path):
    """A missing or broken file disables the replacement layer."""
    try:
        return compile_dictionary(json.loads(path.read_text(encoding='utf-8-sig')))
    except (OSError, ValueError):
        return ()


def has_chinese(text):
    return any('\u4e00' <= char <= '\u9fff' for char in text)


def apply_dictionary(text, rules, force=False, hits=None):
    for pattern, canonical, zh_only in rules:
        allowed = not zh_only or force or has_chinese(text)
        if not allowed:
            if hits is not None:
                for match in pattern.finditer(text):
                    hits.append({'word': canonical, 'from': match.group(), 'applied': False})
            continue

        def replace(match):
            if hits is not None:
                hits.append({'word': canonical, 'from': match.group(), 'applied': True})
            return canonical

        text = pattern.sub(replace, text)
    return text


def load_hotwords(path):
    """Only an explicit hotwords field enables the rescue path."""
    try:
        data = json.loads(path.read_text(encoding='utf-8-sig'))
    except (OSError, ValueError):
        return ()
    words = data.get('hotwords') if isinstance(data, dict) else None
    if not isinstance(words, list):
        return ()
    return tuple(word.strip() for word in words
                 if isinstance(word, str) and word.strip())[:HOTWORD_LIMIT]


def preview(data, text):
    hits = []
    result = apply_dictionary(text, compile_dictionary(data), force=has_chinese(text), hits=hits)
    return {'text': result, 'hits': hits}


if __name__ == '__main__':
    # This short-lived process never imports the model or writes the dictionary.
    sys.stdin.reconfigure(encoding='utf-8')
    sys.stdout.reconfigure(encoding='utf-8')
    request = json.load(sys.stdin)
    if not isinstance(request, dict) or not isinstance(request.get('text'), str):
        raise ValueError('Expected a dictionary and preview text')
    print(json.dumps(preview(request.get('dictionary'), request['text']), ensure_ascii=False))
