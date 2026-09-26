from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from shutil import copy2

from scriborium.core.paths import app_data_dir


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


@dataclass(slots=True)
class RecoveryState:
    project_path: Path
    started_at: str
    dirty: bool
    autosave_snapshot: Path | None
    autosave_at: str | None


class RecoveryService:
    def __init__(self) -> None:
        self.root = app_data_dir() / "recovery"
        self.root.mkdir(parents=True, exist_ok=True)

    def start_session(self, project_path: Path) -> None:
        payload = {
            "project_path": str(project_path),
            "started_at": now_iso(),
            "dirty": False,
            "autosave_snapshot": None,
            "autosave_at": None,
        }
        self._marker_path(project_path).write_text(
            json.dumps(payload, ensure_ascii=False, indent=2),
            encoding="utf-8",
            newline="\n",
        )

    def mark_dirty(self, project_path: Path, dirty: bool) -> None:
        state = self.get_state(project_path)
        if state is None:
            self.start_session(project_path)
            state = self.get_state(project_path)
            if state is None:
                return
        payload = self._state_to_payload(state)
        payload["dirty"] = dirty
        self._marker_path(project_path).write_text(
            json.dumps(payload, ensure_ascii=False, indent=2),
            encoding="utf-8",
            newline="\n",
        )

    def update_autosave_snapshot(self, project_path: Path) -> Path | None:
        if not project_path.exists():
            return None
        snapshot = self._snapshot_path(project_path)
        copy2(project_path, snapshot)
        state = self.get_state(project_path)
        if state is None:
            self.start_session(project_path)
            state = self.get_state(project_path)
            if state is None:
                return snapshot
        payload = self._state_to_payload(state)
        payload["autosave_snapshot"] = str(snapshot)
        payload["autosave_at"] = now_iso()
        self._marker_path(project_path).write_text(
            json.dumps(payload, ensure_ascii=False, indent=2),
            encoding="utf-8",
            newline="\n",
        )
        return snapshot

    def end_session(self, project_path: Path) -> None:
        self._marker_path(project_path).unlink(missing_ok=True)

    def get_state(self, project_path: Path) -> RecoveryState | None:
        marker = self._marker_path(project_path)
        if not marker.exists():
            return None
        try:
            payload = json.loads(marker.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            return None
        autosave_value = payload.get("autosave_snapshot")
        autosave_snapshot = Path(autosave_value) if autosave_value else None
        return RecoveryState(
            project_path=Path(payload.get("project_path", str(project_path))),
            started_at=payload.get("started_at", ""),
            dirty=bool(payload.get("dirty", False)),
            autosave_snapshot=autosave_snapshot,
            autosave_at=payload.get("autosave_at"),
        )

    def _state_to_payload(self, state: RecoveryState) -> dict:
        return {
            "project_path": str(state.project_path),
            "started_at": state.started_at,
            "dirty": state.dirty,
            "autosave_snapshot": str(state.autosave_snapshot)
            if state.autosave_snapshot
            else None,
            "autosave_at": state.autosave_at,
        }

    def _marker_path(self, project_path: Path) -> Path:
        return self.root / f"{self._key(project_path)}.json"

    def _snapshot_path(self, project_path: Path) -> Path:
        return self.root / f"{self._key(project_path)}.autosave.scri"

    def _key(self, project_path: Path) -> str:
        return hashlib.sha1(str(project_path).encode("utf-8")).hexdigest()

