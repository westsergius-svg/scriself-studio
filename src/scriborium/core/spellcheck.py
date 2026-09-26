from __future__ import annotations

import json
import re
from dataclasses import dataclass

from scriborium.core import spell_backend as sb
from scriborium.core.paths import app_data_dir

_TOKEN_RE = re.compile(r"[A-Za-zА-Яа-яЁёÀ-ÿ'-]{2,}")


@dataclass(slots=True)
class SpellIssue:
    word: str
    count: int
    suggestions: list[str]


class SpellcheckService:
    """Орфография: Hunspell из resources (из коробки), запасной pyspellchecker."""

    def __init__(self) -> None:
        self.path = app_data_dir() / "custom_dictionary.json"
        self._custom_cache: dict[str, set[str]] = {}

    def _lang(self, language_code: str) -> str:
        return sb.normalize_lang(language_code)

    def _read_custom(self) -> dict[str, list[str]]:
        if not self.path.exists():
            return {"global": []}
        try:
            raw = json.loads(self.path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return {"global": []}
        if not isinstance(raw, dict):
            return {"global": []}
        out: dict[str, list[str]] = {}
        for key, value in raw.items():
            if not isinstance(value, list):
                continue
            words = [sb.clean_word(str(x)).lower() for x in value if sb.clean_word(str(x))]
            out[str(key)] = sorted(set(words))
        if "global" not in out:
            out["global"] = []
        return out

    def _write_custom(self, payload: dict[str, list[str]]) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        self._custom_cache.clear()

    def get_custom_words(self, language_code: str) -> list[str]:
        return sorted(self._custom_set(language_code))

    def _custom_set(self, language_code: str) -> set[str]:
        code = self._lang(language_code)
        if code in self._custom_cache:
            return self._custom_cache[code]
        data = self._read_custom()
        merged = set(data.get("global", [])) | set(data.get(code, []))
        self._custom_cache[code] = merged
        return merged

    def add_custom_word(self, language_code: str, word: str) -> None:
        clean = sb.clean_word(word).lower()
        if not clean:
            return
        code = self._lang(language_code)
        data = self._read_custom()
        bucket = set(data.get(code, []))
        bucket.add(clean)
        data[code] = sorted(bucket)
        self._write_custom(data)
        sb.invalidate_cache(code)

    def uses_bundled_dictionary(self, language_code: str) -> bool:
        return sb.uses_hunspell(language_code)

    def is_known(self, word: str, language_code: str) -> bool:
        return sb.is_correct(word, language_code, self._custom_set(language_code))

    def is_misspelled(self, word: str, language_code: str) -> bool:
        w = sb.clean_word(word)
        if len(w) < 2:
            return False
        return not self.is_known(w, language_code)

    def suggestions(self, word: str, language_code: str, limit: int = 12) -> list[str]:
        return sb.suggestions_for(word, language_code, self._custom_set(language_code), limit=limit)

    def check_words(self, text: str, language_code: str) -> list[dict]:
        custom = self._custom_set(language_code)
        errors: list[dict] = []
        seen: set[str] = set()
        for match in _TOKEN_RE.finditer(text or ""):
            word = match.group()
            wlower = word.lower()
            if wlower in seen:
                continue
            if sb.is_correct(word, language_code, custom):
                continue
            seen.add(wlower)
            errors.append({
                "word": word,
                "start": match.start(),
                "end": match.end(),
                "suggestions": self.suggestions(word, language_code),
            })
        return errors

    def check_text(self, text: str, language_code: str) -> list[SpellIssue]:
        custom = self._custom_set(language_code)
        freq: dict[str, int] = {}
        display: dict[str, str] = {}
        for match in _TOKEN_RE.finditer(text or ""):
            word = match.group()
            wlower = word.lower()
            if sb.is_correct(word, language_code, custom):
                continue
            freq[wlower] = freq.get(wlower, 0) + 1
            display.setdefault(wlower, word)

        issues: list[SpellIssue] = []
        for wlower, count in sorted(freq.items(), key=lambda item: item[1], reverse=True):
            issues.append(
                SpellIssue(
                    word=display.get(wlower, wlower),
                    count=count,
                    suggestions=self.suggestions(display.get(wlower, wlower), language_code),
                )
            )
        return issues

    def invalidate_cache(self, language_code: str | None = None) -> None:
        if language_code is None:
            self._custom_cache.clear()
        else:
            self._custom_cache.pop(self._lang(language_code), None)
        sb.invalidate_cache(language_code)
