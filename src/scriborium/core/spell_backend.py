"""Hunspell (spylls) + запасной pyspellchecker. Словари в resources/hunspell/."""
from __future__ import annotations

import re
from pathlib import Path

from scriborium.core.paths import resources_root

# (папка в resources/hunspell, базовое имя файлов .aff/.dic)
_HUNSPELL_BUNDLED: dict[str, tuple[str, str]] = {
    "en": ("en", "index"),
    "ru": ("ru", "index"),
    "de": ("de", "index"),
    "es": ("es", "index"),
    "fr": ("fr", "index"),
}

# uk: если нет отдельного словаря — используем ru
_UK_FALLBACK = "ru"

_TOKEN_CLEAN = re.compile(r"[^A-Za-zА-Яа-яЁёÀ-ÿ'-]+")
_HAS_CYRILLIC = re.compile(r"[А-Яа-яЁё]")
_HAS_LATIN = re.compile(r"[A-Za-z]")


def normalize_lang(language_code: str) -> str:
    return (language_code or "ru").split("-")[0].lower()


def hunspell_dict_dir(language_code: str) -> Path | None:
    code, spec = _resolve_lang(language_code)
    if not spec:
        return None
    folder_name, _base = spec
    path = resources_root() / "hunspell" / folder_name
    aff = list(path.glob("*.aff"))
    dic = list(path.glob("*.dic"))
    if path.is_dir() and aff and dic:
        return path
    return None


def _resolve_lang(language_code: str) -> tuple[str, tuple[str, str] | None]:
    code = normalize_lang(language_code)
    spec = _HUNSPELL_BUNDLED.get(code)
    if spec is None and code == "uk":
        code = _UK_FALLBACK
        spec = _HUNSPELL_BUNDLED.get(code)
    return code, spec


def hunspell_base_name(language_code: str) -> str | None:
    _code, spec = _resolve_lang(language_code)
    return spec[1] if spec else None


def _word_forms(word: str) -> list[str]:
    """Варианты регистра для Hunspell (важно для русского и имён)."""
    w = clean_word(word)
    if not w:
        return []
    forms = [w, w.lower(), w.capitalize(), w.title()]
    if "-" in w:
        parts = w.split("-")
        forms.append("-".join(p.capitalize() for p in parts))
    seen: list[str] = []
    for f in forms:
        if f and f not in seen:
            seen.append(f)
    return seen


def _secondary_lang(primary: str, word: str) -> str | None:
    """Для заимствований: латиница при ru и наоборот."""
    if primary == "ru" and _HAS_LATIN.search(word) and not _HAS_CYRILLIC.search(word):
        return "en"
    if primary == "en" and _HAS_CYRILLIC.search(word):
        return "ru"
    return None


class _HunspellEngine:
    def __init__(self) -> None:
        self._loaded: dict[str, object] = {}

    def _get(self, language_code: str):
        from spylls.hunspell import Dictionary

        resolved, spec = _resolve_lang(language_code)
        if resolved in self._loaded:
            return self._loaded[resolved]

        if spec is None:
            self._loaded[resolved] = None
            return None
        folder_name, base = spec
        folder = resources_root() / "hunspell" / folder_name
        if folder is None or base is None:
            self._loaded[resolved] = None
            return None
        try:
            dic = Dictionary.from_files(str(folder / base))
            self._loaded[resolved] = dic
            return dic
        except Exception:
            self._loaded[resolved] = None
            return None

    def warmup(self, language_code: str) -> bool:
        return self._get(language_code) is not None

    def check(self, word: str, language_code: str) -> bool:
        dic = self._get(language_code)
        if dic is None:
            return True
        for form in _word_forms(word):
            try:
                if dic.lookup(form):
                    return True
            except Exception:
                continue
        return False

    def suggest(self, word: str, language_code: str, limit: int = 12) -> list[str]:
        dic = self._get(language_code)
        if dic is None:
            return []
        w = clean_word(word)
        if not w:
            return []
        merged: list[str] = []
        seen: set[str] = set()

        def add_many(raw) -> None:
            for item in raw:
                s = str(item).strip()
                if not s or s.lower() == w.lower():
                    continue
                key = s.lower()
                if key in seen:
                    continue
                seen.add(key)
                merged.append(s)
                if len(merged) >= limit:
                    return

        for form in (w.lower(), w, w.capitalize()):
            try:
                add_many(dic.suggest(form))
            except Exception:
                pass
            if len(merged) >= limit:
                break
        return merged[:limit]

    def invalidate(self, language_code: str | None = None) -> None:
        if language_code is None:
            self._loaded.clear()
            return
        self._loaded.pop(normalize_lang(language_code), None)


class _PySpellFallback:
    def __init__(self) -> None:
        self._checkers: dict[str, object] = {}

    def _checker(self, language_code: str):
        from spellchecker import SpellChecker

        code = normalize_lang(language_code)
        if code not in self._checkers:
            lang = code if code in {"ru", "en", "de", "es", "fr", "pt"} else "en"
            try:
                self._checkers[code] = SpellChecker(language=lang)
            except Exception:
                self._checkers[code] = SpellChecker(language="en")
        return self._checkers[code]

    def check(self, word: str, language_code: str) -> bool:
        checker = self._checker(language_code)
        return word.lower() not in checker.unknown([word.lower()])

    def suggest(self, word: str, language_code: str, limit: int = 12) -> list[str]:
        checker = self._checker(language_code)
        try:
            cands = checker.candidates(word.lower()) or []
            out: list[str] = []
            for c in cands:
                s = str(c)
                if s and s.lower() != word.lower():
                    out.append(s)
                if len(out) >= limit:
                    break
            return out
        except Exception:
            return []

    def invalidate(self, language_code: str | None = None) -> None:
        if language_code is None:
            self._checkers.clear()
        else:
            self._checkers.pop(normalize_lang(language_code), None)


_hunspell = _HunspellEngine()
_fallback = _PySpellFallback()


def clean_word(word: str) -> str:
    return _TOKEN_CLEAN.sub("", (word or "").strip())


def uses_hunspell(language_code: str) -> bool:
    return hunspell_dict_dir(language_code) is not None


def is_correct(word: str, language_code: str, custom_words: set[str]) -> bool:
    w = clean_word(word)
    if len(w) < 2:
        return True
    lower = w.lower()
    if lower in custom_words:
        return True

    primary = normalize_lang(language_code)
    if uses_hunspell(primary):
        if _hunspell.check(w, primary):
            return True
        alt = _secondary_lang(primary, w)
        if alt and uses_hunspell(alt) and _hunspell.check(w, alt):
            return True
        return False

    if _fallback.check(lower, primary):
        return True
    alt = _secondary_lang(primary, w)
    if alt:
        return _fallback.check(lower, alt)
    return False


def suggestions_for(word: str, language_code: str, custom_words: set[str], limit: int = 12) -> list[str]:
    w = clean_word(word)
    if not w:
        return []

    primary = normalize_lang(language_code)
    merged: list[str] = []
    seen: set[str] = set()

    def add_list(items: list[str]) -> None:
        for s in items:
            if not s or s.lower() == w.lower():
                continue
            key = s.lower()
            if key in seen:
                continue
            seen.add(key)
            merged.append(s)

    if uses_hunspell(primary):
        add_list(_hunspell.suggest(w, primary, limit=limit))
        alt = _secondary_lang(primary, w)
        if alt and uses_hunspell(alt) and len(merged) < limit:
            add_list(_hunspell.suggest(w, alt, limit=limit))

    if len(merged) < limit:
        add_list(_fallback.suggest(w, primary, limit=limit))
        if len(merged) < limit:
            alt = _secondary_lang(primary, w)
            if alt:
                add_list(_fallback.suggest(w, alt, limit=limit))

    if lower := w.lower():
        if lower in custom_words:
            return merged[:limit]
    return merged[:limit]


def invalidate_cache(language_code: str | None = None) -> None:
    _hunspell.invalidate(language_code)
    _fallback.invalidate(language_code)


def warmup_languages(*codes: str) -> None:
    """Предзагрузка словарей при старте (чтобы проверка сразу работала)."""
    for code in codes:
        c = normalize_lang(code)
        if uses_hunspell(c):
            _hunspell.warmup(c)
