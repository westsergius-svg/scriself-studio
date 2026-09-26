from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from pathlib import Path

from scriborium.core.paths import settings_path


@dataclass(slots=True)
class AppSettings:
    language: str = "ru"
    open_last_project_on_startup: bool = True
    show_launcher_when_no_projects: bool = True
    minimize_to_tray_on_close: bool = False
    theme: str = "classic"
    high_contrast: bool = False
    spellcheck_enabled: bool = True
    default_projects_dir: str = str(Path.home() / "Documents" / "Scriborium")
    backup_dir: str = str(Path.home() / "Documents" / "Scriborium" / "Backups")
    autosave_minutes: int = 5
    create_backup_on_save: bool = True
    create_backup_before_autosave: bool = True
    max_backups_per_project: int = 10
    delete_old_backups: bool = True
    delete_backups_older_than_days: int = 30
    recent_projects: list[str] = field(default_factory=list)


class SettingsService:
    def __init__(self) -> None:
        self.path = settings_path()

    def load(self) -> AppSettings:
        if not self.path.exists():
            settings = AppSettings()
            self.save(settings)
            return settings

        try:
            data = json.loads(self.path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            settings = AppSettings()
            self.save(settings)
            return settings

        theme_value = data.get("theme", "classic")
        if theme_value == "standard":
            theme_value = "classic"

        return AppSettings(
            language=data.get("language", "ru"),
            open_last_project_on_startup=data.get("open_last_project_on_startup", True),
            show_launcher_when_no_projects=data.get("show_launcher_when_no_projects", True),
            minimize_to_tray_on_close=data.get("minimize_to_tray_on_close", False),
            theme=theme_value,
            high_contrast=data.get("high_contrast", False),
            spellcheck_enabled=data.get("spellcheck_enabled", True),
            default_projects_dir=data.get(
                "default_projects_dir", str(Path.home() / "Documents" / "Scriborium")
            ),
            backup_dir=data.get(
                "backup_dir", str(Path.home() / "Documents" / "Scriborium" / "Backups")
            ),
            autosave_minutes=data.get("autosave_minutes", 5),
            create_backup_on_save=data.get("create_backup_on_save", True),
            create_backup_before_autosave=data.get("create_backup_before_autosave", True),
            max_backups_per_project=data.get("max_backups_per_project", 10),
            delete_old_backups=data.get("delete_old_backups", True),
            delete_backups_older_than_days=data.get("delete_backups_older_than_days", 30),
            recent_projects=data.get("recent_projects", []),
        )

    def save(self, settings: AppSettings) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        payload = json.dumps(asdict(settings), ensure_ascii=False, indent=2)
        self.path.write_text(payload, encoding="utf-8", newline="\n")

    def add_recent_project(self, project_path: Path) -> AppSettings:
        settings = self.load()
        value = str(project_path)
        deduped = [p for p in settings.recent_projects if p != value]
        settings.recent_projects = [value, *deduped][:20]
        self.save(settings)
        return settings
