from __future__ import annotations

import re
from dataclasses import dataclass

from scriborium.core.project import ProjectDocument


@dataclass(slots=True)
class SearchResult:
    chapter_index: int
    scene_index: int
    chapter_title: str
    scene_title: str
    field: str
    snippet: str
    start: int
    end: int


class SearchService:
    def search(
        self,
        document: ProjectDocument,
        query: str,
        case_sensitive: bool = False,
        whole_word: bool = False,
    ) -> list[SearchResult]:
        text = query.strip()
        if not text:
            return []

        pattern = re.escape(text)
        if whole_word:
            pattern = rf"\b{pattern}\b"
        flags = 0 if case_sensitive else re.IGNORECASE
        regex = re.compile(pattern, flags=flags)

        results: list[SearchResult] = []
        chapters = document.content.get("chapters", [])
        for chapter_index, chapter in enumerate(chapters):
            chapter_title = chapter.get("title", f"Глава {chapter_index + 1}")
            for scene_index, scene in enumerate(chapter.get("scenes", [])):
                scene_title = scene.get("title", f"Сцена {scene_index + 1}")
                scene_text = scene.get("text", "")
                for match in regex.finditer(scene_text):
                    start = match.start()
                    end = match.end()
                    snippet = self._snippet(scene_text, start, end)
                    results.append(
                        SearchResult(
                            chapter_index=chapter_index,
                            scene_index=scene_index,
                            chapter_title=chapter_title,
                            scene_title=scene_title,
                            field="text",
                            snippet=snippet,
                            start=start,
                            end=end,
                        )
                    )
        return results

    def _snippet(self, value: str, start: int, end: int, radius: int = 40) -> str:
        left = max(0, start - radius)
        right = min(len(value), end + radius)
        prefix = "..." if left > 0 else ""
        suffix = "..." if right < len(value) else ""
        return f"{prefix}{value[left:right]}{suffix}".replace("\n", " ")

