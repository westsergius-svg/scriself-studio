from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path


def utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


@dataclass
class Scene:
    id: str
    title: str
    text: str = ""


@dataclass
class Chapter:
    id: str
    title: str
    summary: str = ""
    status: str = "draft"
    order: int = 1
    scenes: list[Scene] = field(default_factory=list)


@dataclass
class ProjectModel:
    title: str
    path: Path | None = None
    author: str = ""
    version: str = "1.2.0"
    created_at: str = field(default_factory=utc_now)
    modified_at: str = field(default_factory=utc_now)
    chapters: list[Chapter] = field(default_factory=list)

    @property
    def is_saved(self) -> bool:
        return self.path is not None

    def total_word_count(self) -> int:
        return sum(chapter_word_count(chapter) for chapter in self.chapters)


def chapter_word_count(chapter: Chapter) -> int:
    return sum(len((scene.text or "").split()) for scene in chapter.scenes)


def chapter_text(chapter: Chapter) -> str:
    return "\n\n".join(scene.text for scene in chapter.scenes if scene.text)
