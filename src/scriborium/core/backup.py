from __future__ import annotations

from datetime import datetime, timedelta
from pathlib import Path
from shutil import copy2


class BackupService:
    def create_backup(self, project_path: Path, backup_dir: Path, reason: str) -> Path:
        backup_dir.mkdir(parents=True, exist_ok=True)
        stamp = datetime.now().strftime("%Y%m%d_%H%M%S_%f")
        safe_reason = reason.replace(" ", "_")
        backup_name = f"{project_path.stem}__{stamp}__{safe_reason}.backup"
        target = backup_dir / backup_name
        copy2(project_path, target)
        return target

    def cleanup(
        self,
        backup_dir: Path,
        project_stem: str,
        max_backups: int,
        delete_old: bool,
        max_age_days: int,
    ) -> None:
        if not backup_dir.exists():
            return

        backups = sorted(
            backup_dir.glob(f"{project_stem}__*.backup"),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )

        for extra in backups[max_backups:]:
            extra.unlink(missing_ok=True)

        if not delete_old:
            return

        threshold = datetime.now() - timedelta(days=max_age_days)
        for path in backups[:max_backups]:
            modified = datetime.fromtimestamp(path.stat().st_mtime)
            if modified < threshold:
                path.unlink(missing_ok=True)
