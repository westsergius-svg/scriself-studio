from __future__ import annotations

import json
from dataclasses import asdict, dataclass
from pathlib import Path


@dataclass
class AppSettings:
    language: str = "ru"
    theme: str = "standard"


class SettingsService:
    def __init__(self) -> None:
        self._settings_path = Path.home() / "AppData" / "Roaming" / "ScriboriumModular" / "settings.json"
        self._settings_path.parent.mkdir(parents=True, exist_ok=True)
        self._settings = self._load()

    def _load(self) -> AppSettings:
        if not self._settings_path.exists():
            return AppSettings()
        try:
            data = json.loads(self._settings_path.read_text(encoding="utf-8"))
            return AppSettings(**data)
        except Exception:
            return AppSettings()

    def save(self) -> None:
        self._settings_path.write_text(
            json.dumps(asdict(self._settings), ensure_ascii=False, indent=2), encoding="utf-8"
        )

    @property
    def data(self) -> AppSettings:
        return self._settings
