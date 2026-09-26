from __future__ import annotations

import json

from scriborium_modular.core.paths import resource_path
from scriborium_modular.core.settings_service import SettingsService


class I18nService:
    def __init__(self, settings_service: SettingsService) -> None:
        self._settings = settings_service
        self._cache: dict[str, dict[str, str]] = {}

    def msg(self, key: str, fallback: str) -> str:
        lang = self._settings.data.language
        if lang not in self._cache:
            path = resource_path(f"lang/{lang}.json")
            try:
                self._cache[lang] = json.loads(path.read_text(encoding="utf-8"))
            except Exception:
                self._cache[lang] = {}
        return self._cache[lang].get(key, fallback)
