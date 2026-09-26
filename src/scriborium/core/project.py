from __future__ import annotations

import json
import sqlite3
import zlib
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

from scriborium.core.templates import build_project_content

_SQLITE_MAGIC = b"SQLite format 3\x00"
_CHAPTER_STATUSES = {"plan", "in_progress", "done"}


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


@dataclass(slots=True)
class ProjectMeta:
    name: str
    template: str
    created_at: str
    updated_at: str


@dataclass(slots=True)
class ProjectDocument:
    path: Path
    manifest: dict
    content: dict


class ProjectService:
    manifest_name = "manifest.json"
    content_name = "content.json"

    def create_new(self, path: Path, name: str, template: str) -> ProjectDocument:
        stamp = now_iso()
        manifest = {
            "format": "scri",
            "version": 1,
            "name": name,
            "template": template,
            "createdAt": stamp,
            "updatedAt": stamp,
        }
        content = build_project_content(name, template)
        self._write(path, manifest, content)
        return ProjectDocument(path=path, manifest=manifest, content=content)

    def read_meta(self, path: Path) -> ProjectMeta:
        if self._is_sqlite(path):
            return self._read_sqlite_meta(path)
        with ZipFile(path, "r") as archive:
            manifest_raw = archive.read(self.manifest_name).decode("utf-8")
        manifest = json.loads(manifest_raw)
        return ProjectMeta(
            name=manifest.get("name", path.stem),
            template=manifest.get("template", ""),
            created_at=manifest.get("createdAt", ""),
            updated_at=manifest.get("updatedAt", ""),
        )

    def open_document(self, path: Path) -> ProjectDocument:
        if self._is_sqlite(path):
            return self._open_sqlite_document(path)
        with ZipFile(path, "r") as archive:
            manifest_raw = archive.read(self.manifest_name).decode("utf-8")
            content_raw = archive.read(self.content_name).decode("utf-8")
        manifest = json.loads(manifest_raw)
        content = json.loads(content_raw)
        # Миграция/гарантия наличия томов (многотомник).
        from scriborium.core.volumes import ensure_volumes

        ensure_volumes(content, fallback_title=manifest.get("name") or path.stem)
        return ProjectDocument(path=path, manifest=manifest, content=content)

    def save_document(self, document: ProjectDocument) -> None:
        document.manifest["updatedAt"] = now_iso()
        self._write(document.path, document.manifest, document.content)

    def _write(self, path: Path, manifest: dict, content: dict) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        with ZipFile(path, mode="w", compression=ZIP_DEFLATED) as archive:
            archive.writestr(
                self.manifest_name,
                json.dumps(manifest, ensure_ascii=False, indent=2),
            )
            archive.writestr(
                self.content_name,
                json.dumps(content, ensure_ascii=False, indent=2),
            )

    @staticmethod
    def _is_sqlite(path: Path) -> bool:
        try:
            with open(path, "rb") as handle:
                return handle.read(16) == _SQLITE_MAGIC
        except OSError:
            return False

    def _read_sqlite_meta(self, path: Path) -> ProjectMeta:
        meta = self._load_sqlite_meta(path)
        return ProjectMeta(
            name=meta.get("title") or path.stem,
            template=meta.get("template", ""),
            created_at=meta.get("createdAt", ""),
            updated_at=meta.get("updatedAt", ""),
        )

    @staticmethod
    def _load_sqlite_meta(path: Path) -> dict:
        con = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
        try:
            con.row_factory = sqlite3.Row
            return {
                str(r["key"]): str(r["value"])
                for r in con.execute("SELECT key, value FROM meta")
            }
        finally:
            con.close()

    def _open_sqlite_document(self, path: Path) -> ProjectDocument:
        """Читает проект старого формата (SQLite) и возвращает его как новый
        документ. Первое сохранение перезапишет файл уже в новом (zip) формате."""
        meta = self._load_sqlite_meta(path)
        con = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
        try:
            con.row_factory = sqlite3.Row
            title = meta.get("title") or path.stem
            manifest = {
                "format": "scri",
                "version": 1,
                "name": title,
                "template": meta.get("template", ""),
                "createdAt": meta.get("createdAt", ""),
                "updatedAt": meta.get("updatedAt", ""),
            }

            texts = self._load_sqlite_texts(con)

            book = {"title": title, "synopsis": "", "preface": ""}
            library = {"characters": [], "locations": [], "objects": []}
            plotlines: list[dict] = []
            chapters: dict[str, dict] = {}
            chapter_order: list[str] = []
            scenes: list[dict] = []
            chapter_ord: dict[str, float] = {}

            rows = list(con.execute("SELECT id, type, data, name, ord FROM entities"))
            for row in rows:
                etype = str(row["type"])
                data = self._parse_entity_data(row["data"])
                eid = str(row["id"])
                name = str(row["name"] or "").strip()
                if etype == "book":
                    book = {
                        "title": str(data.get("title") or title),
                        "subtitle": str(data.get("subtitle", "") or ""),
                        "author": str(data.get("author", "") or ""),
                        "genre": str(data.get("genre", "") or ""),
                        "preface": str(data.get("preface", "") or ""),
                        "synopsis": str(data.get("annotation", "") or ""),
                    }
                elif etype == "chapter":
                    chapters[eid] = {
                        "id": eid,
                        "title": str(data.get("title") or name or "Глава"),
                        "number": _to_int(data.get("number")),
                        "subtitle": str(data.get("subtitle", "") or ""),
                        "epigraph": str(data.get("epigraph", "") or ""),
                        "synopsis": str(data.get("description", "") or ""),
                        "notes": str(data.get("notes", "") or ""),
                        "status": _normalize_chapter_status(data.get("status")),
                        "targetWordCount": _to_int(data.get("targetWords")),
                        "chapterText": texts.get(str(data.get("textId", "") or ""), ""),
                        "scenes": [],
                    }
                    chapter_order.append(eid)
                    chapter_ord[eid] = _to_float(row["ord"])
                elif etype == "scene":
                    scenes.append(
                        {
                            "id": eid,
                            "chapter_id": str(data.get("chapterId", "") or ""),
                            "ord": _to_float(data.get("order", row["ord"])),
                            "title": str(data.get("title") or name or "Сцена"),
                            "text": texts.get(str(data.get("textId", "") or ""), ""),
                            "status": str(data.get("status") or "draft"),
                            "targetWordCount": _to_int(data.get("targetWords")),
                            "datetime": str(data.get("dateTime", "") or ""),
                            "goal": str(data.get("goal", "") or ""),
                            "conflict": str(data.get("conflict", "") or ""),
                            "result": str(data.get("result", "") or ""),
                        }
                    )
                elif etype == "character":
                    library["characters"].append(
                        {
                            "id": eid,
                            "name": str(data.get("name") or name or "Персонаж"),
                            "description": str(data.get("motivation", "") or ""),
                            "extra": str(data.get("role", "") or ""),
                            "field2": "",
                            "field3": "",
                            "short": "",
                            "notes": str(data.get("note", "") or ""),
                        }
                    )
                elif etype == "location":
                    library["locations"].append(
                        {
                            "id": eid,
                            "name": str(data.get("name") or name or "Локация"),
                            "description": str(data.get("description", "") or ""),
                            "extra": str(data.get("note", "") or ""),
                            "field2": "",
                            "field3": "",
                            "short": "",
                            "notes": "",
                        }
                    )
                elif etype == "object":
                    library["objects"].append(
                        {
                            "id": eid,
                            "name": str(data.get("name") or name or "Объект"),
                            "description": str(data.get("description", "") or ""),
                            "extra": str(data.get("note", "") or ""),
                            "field2": "",
                            "field3": "",
                            "short": "",
                            "notes": "",
                        }
                    )
                elif etype == "story_arc":
                    plotlines.append(
                        {
                            "id": eid,
                            "name": str(data.get("title") or name or "Сюжетная линия"),
                            "color": str(data.get("color") or "#4A7DFF"),
                        }
                    )

            for scene in scenes:
                chapter = chapters.get(scene["chapter_id"])
                if chapter is not None:
                    chapter["scenes"].append(scene)

            for chapter in chapters.values():
                chapter["scenes"].sort(key=lambda sc: sc["ord"])

            chapters_sorted = sorted(
                (chapters[eid] for eid in chapter_order if eid in chapters),
                key=lambda ch: (ch["number"], chapter_ord.get(ch["id"], 0.0)),
            )
            if not chapters_sorted:
                chapters_sorted = [
                    {
                        "id": "chapter_001",
                        "title": title or "Глава 1",
                        "number": 0,
                        "subtitle": "",
                        "epigraph": "",
                        "synopsis": "",
                        "notes": "",
                        "status": "in_progress",
                        "targetWordCount": 0,
                        "chapterText": "",
                        "scenes": [],
                    }
                ]

            content = {
                "book": book,
                "chapters": chapters_sorted,
                "library": library,
                "timeline": {"plotlines": plotlines or [{"id": "plotline_main", "name": "Главная сюжетная линия", "color": "#4A7DFF"}]},
            }
            from scriborium.core.volumes import ensure_volumes

            ensure_volumes(content, fallback_title=manifest.get("name") or title)
            return ProjectDocument(path=path, manifest=manifest, content=content)
        finally:
            con.close()

    @staticmethod
    def _load_sqlite_texts(con: sqlite3.Connection) -> dict[str, str]:
        texts: dict[str, str] = {}
        try:
            rows = con.execute(
                "SELECT id, content, compressed FROM texts"
            )
        except sqlite3.OperationalError:
            return texts
        for row in rows:
            raw = row[1]
            if raw is None:
                texts[str(row[0])] = ""
                continue
            if row[2]:
                try:
                    raw = zlib.decompress(raw)
                except Exception:
                    pass
            texts[str(row[0])] = raw.decode("utf-8", errors="replace")
        return texts

    @staticmethod
    def _parse_entity_data(raw: object) -> dict:
        if not isinstance(raw, str) or not raw.strip():
            return {}
        try:
            value = json.loads(raw)
        except json.JSONDecodeError:
            return {}
        return value if isinstance(value, dict) else {}


def _to_int(value: object) -> int:
    try:
        return max(0, int(value))
    except (TypeError, ValueError):
        return 0


def _to_float(value: object) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0


def _normalize_chapter_status(value: object) -> str:
    status = str(value or "in_progress")
    return status if status in _CHAPTER_STATUSES else "in_progress"


