from __future__ import annotations

import json
import re
import zipfile
from pathlib import Path

from scriborium_modular.core.models.project_model import Chapter, ProjectModel, Scene


class ProjectServiceError(Exception):
    pass


class ProjectService:
    def create_project(self, title: str, folder: str | Path) -> ProjectModel:
        project = ProjectModel(
            title=title.strip() or "Новый проект",
            chapters=[
                Chapter(
                    id="chapter_001",
                    title="Глава 1",
                    order=1,
                    scenes=[Scene(id="scene_001_001", title="Текст главы")],
                )
            ],
        )
        safe_name = self._safe_file_name(project.title)
        project.path = Path(folder) / f"{safe_name}.scri"
        self.save_project(project)
        return project

    def open_project(self, path: str | Path) -> ProjectModel:
        project_path = Path(path)
        if not project_path.exists():
            raise ProjectServiceError(f"Файл не найден: {project_path}")

        with zipfile.ZipFile(project_path, "r") as archive:
            try:
                content = json.loads(archive.read("content.json").decode("utf-8"))
            except KeyError as exc:
                raise ProjectServiceError("В проекте нет content.json") from exc

        project = self._from_content(content)
        project.path = project_path
        return project

    def save_project(self, project: ProjectModel, path: str | Path | None = None) -> Path:
        target = Path(path) if path else project.path
        if target is None:
            raise ProjectServiceError("Не указан путь сохранения проекта")
        target.parent.mkdir(parents=True, exist_ok=True)
        project.path = target
        project.modified_at = __import__(
            "scriborium_modular.core.models.project_model",
            fromlist=["utc_now"],
        ).utc_now()

        manifest = {
            "app": "Scriborium",
            "format": "scri",
            "formatVersion": "1.2.0",
            "title": project.title,
            "createdAt": project.created_at,
            "modifiedAt": project.modified_at,
        }
        content = self._to_content(project)
        with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2))
            archive.writestr("content.json", json.dumps(content, ensure_ascii=False, indent=2))
        return target

    def export_markdown(self, project: ProjectModel, path: str | Path) -> Path:
        target = Path(path)
        target.parent.mkdir(parents=True, exist_ok=True)
        lines = [f"# {project.title}", ""]
        if project.author:
            lines.extend([f"Автор: {project.author}", ""])
        for chapter in project.chapters:
            lines.extend([f"## {chapter.title}", ""])
            if chapter.summary:
                lines.extend([f"_{chapter.summary}_", ""])
            for scene in chapter.scenes:
                if scene.text:
                    lines.extend([scene.text, ""])
        target.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")
        return target

    def _to_content(self, project: ProjectModel) -> dict:
        return {
            "formatVersion": "1.2.0",
            "schemaVersion": 2,
            "book": {
                "id": "book_root",
                "title": project.title,
                "author": project.author,
                "createdAt": project.created_at,
                "modifiedAt": project.modified_at,
            },
            "modules": {
                "project_map": {
                    "chapters": [
                        {
                            "id": chapter.id,
                            "title": chapter.title,
                            "summary": chapter.summary,
                            "status": chapter.status,
                            "order": chapter.order,
                        }
                        for chapter in project.chapters
                    ]
                },
                "chapter_editor": {
                    "chapterTexts": {
                        chapter.id: chapter.scenes[0].text if chapter.scenes else ""
                        for chapter in project.chapters
                    }
                },
            },
            "chapters": [
                {
                    "id": chapter.id,
                    "title": chapter.title,
                    "number": chapter.order,
                    "synopsis": chapter.summary,
                    "status": chapter.status,
                    "scenes": [
                        {"id": scene.id, "title": scene.title, "text": scene.text, "order": index + 1}
                        for index, scene in enumerate(chapter.scenes)
                    ],
                }
                for chapter in project.chapters
            ],
        }

    def _from_content(self, content: dict) -> ProjectModel:
        book = content.get("book", {})
        chapters: list[Chapter] = []
        raw_chapters = content.get("chapters") or content.get("modules", {}).get("project_map", {}).get("chapters", [])
        chapter_texts = content.get("modules", {}).get("chapter_editor", {}).get("chapterTexts", {})
        for index, raw in enumerate(raw_chapters, start=1):
            chapter_id = raw.get("id") or f"chapter_{index:03d}"
            raw_scenes = raw.get("scenes") or []
            if raw_scenes:
                scenes = [
                    Scene(
                        id=scene.get("id") or f"scene_{index:03d}_{scene_index:03d}",
                        title=scene.get("title") or "Текст главы",
                        text=scene.get("text") or "",
                    )
                    for scene_index, scene in enumerate(raw_scenes, start=1)
                ]
            else:
                scenes = [Scene(id=f"scene_{index:03d}_001", title="Текст главы", text=chapter_texts.get(chapter_id, ""))]
            chapters.append(
                Chapter(
                    id=chapter_id,
                    title=raw.get("title") or f"Глава {index}",
                    summary=raw.get("summary") or raw.get("synopsis") or "",
                    status=raw.get("status") or "draft",
                    order=int(raw.get("order") or raw.get("number") or index),
                    scenes=scenes,
                )
            )
        if not chapters:
            chapters.append(Chapter(id="chapter_001", title="Глава 1", scenes=[Scene(id="scene_001_001", title="Текст главы")]))
        return ProjectModel(
            title=book.get("title") or content.get("title") or "Без названия",
            author=book.get("author") or "",
            version=content.get("formatVersion") or "1.2.0",
            created_at=book.get("createdAt") or content.get("createdAt") or "",
            modified_at=book.get("modifiedAt") or content.get("lastModified") or "",
            chapters=chapters,
        )

    def _safe_file_name(self, title: str) -> str:
        value = re.sub(r'[<>:"/\\\\|?*]+', "_", title).strip().strip(".")
        return value or "ScriboriumProject"
