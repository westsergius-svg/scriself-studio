from __future__ import annotations

import json
from pathlib import Path


LANGUAGE_CODE_TO_NAME = {
    "ru": "Русский",
    "en": "English",
    "de": "Deutsch",
    "es": "Español",
    "fr": "Français",
    "uk": "Українська",
    "zh": "中文",
    "ja": "日本語",
}

LANGUAGE_NAME_TO_CODE = {value: key for key, value in LANGUAGE_CODE_TO_NAME.items()}


class I18nService:
    def __init__(self) -> None:
        self.lang_dir = Path(__file__).resolve().parents[1] / "resources" / "lang"
        self.default_code = "ru"
        self.default_messages = self._load_file(self.default_code)

    def resolve_code(self, language_name_or_code: str) -> str:
        if language_name_or_code in LANGUAGE_CODE_TO_NAME:
            return language_name_or_code
        return LANGUAGE_NAME_TO_CODE.get(language_name_or_code, self.default_code)

    def display_name(self, code: str) -> str:
        return LANGUAGE_CODE_TO_NAME.get(code, LANGUAGE_CODE_TO_NAME[self.default_code])

    def available_codes(self) -> list[str]:
        return list(LANGUAGE_CODE_TO_NAME.keys())

    def messages(self, language_name_or_code: str) -> dict[str, str]:
        code = self.resolve_code(language_name_or_code)
        selected = self._load_file(code)
        merged = dict(self.default_messages)
        merged.update(selected)
        return merged

    def _load_file(self, code: str) -> dict[str, str]:
        file_path = self.lang_dir / f"{code}.json"
        if not file_path.exists():
            return {}
        try:
            return json.loads(file_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            return {}
