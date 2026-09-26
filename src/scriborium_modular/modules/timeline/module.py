from __future__ import annotations

from dataclasses import dataclass


@dataclass
class TimelineModule:
    id: str = "timeline"
    name: str = "Timeline"
    version: str = "1.0.0"

    def initialize(self, context) -> None:
        self._context = context

    def chapter_stats(self, project_model):
        rows = []
        for chapter in project_model.chapters:
            word_count = sum(len((scene.text or "").split()) for scene in chapter.scenes)
            rows.append((chapter.title, word_count))
        return rows
