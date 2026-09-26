"""Единый текстовый обмен Scriborium (Android ↔ Desktop).

Формат SCRIBORIUM-TXT v1:
  # SCRIBORIUM-TXT 1
  # Название книги
  ## Глава
  %%CHAPTER-TEXT%%
  текст свободного редактора
  ### Сцена
  текст сцены
"""
from __future__ import annotations

import re

HEADER = "# SCRIBORIUM-TXT 1"
MARKER_CHAPTER_TEXT = "%%CHAPTER-TEXT%%"
RE_ANDROID_SCENE = re.compile(r"^###\s+(.+?)\s+###\s*$")
RE_CHAPTER_UNDERLINE = re.compile(r"^-{3,}\s*$")
RE_BOOK_UNDERLINE = re.compile(r"^={3,}\s*$")


def export_txt(content: dict) -> str:
    lines: list[str] = [HEADER, ""]
    book = content.get("book") or {}
    book_title = str(book.get("title", "")).strip() or "Без названия"
    lines.append(f"# {book_title}")
    lines.append("")

    from scriborium.core.chapter_text import chapter_free_text_plain

    for chapter in content.get("chapters", []):
        chapter_title = str(chapter.get("title", "Глава")).strip() or "Глава"
        lines.append(f"## {chapter_title}")
        lines.append("")
        chapter_text = chapter_free_text_plain(chapter)
        if chapter_text.strip():
            lines.append(MARKER_CHAPTER_TEXT)
            lines.append(chapter_text.rstrip())
            lines.append("")
        for scene in chapter.get("scenes", []):
            scene_title = str(scene.get("title", "Сцена")).strip() or "Сцена"
            lines.append(f"### {scene_title}")
            body = str(scene.get("text", "")).rstrip()
            if body:
                lines.append(body)
            lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def import_txt(text: str) -> list[dict]:
    normalized = text.replace("\r\n", "\n").replace("\r", "\n")
    first = normalized.lstrip().split("\n", 1)[0].strip()
    if first == HEADER:
        return _parse_scriborium_txt(normalized)
    if RE_ANDROID_SCENE.search(normalized, re.MULTILINE):
        return _parse_legacy_android_txt(normalized)
    if _looks_like_legacy_desktop(normalized):
        return _parse_legacy_desktop_txt(normalized)
    return []


def _looks_like_legacy_desktop(text: str) -> bool:
    lines = text.split("\n")
    for i, line in enumerate(lines[:-1]):
        if line.strip() and RE_CHAPTER_UNDERLINE.match(lines[i + 1].strip()):
            return True
    return False


def _parse_scriborium_txt(text: str) -> list[dict]:
    lines = text.split("\n")
    chapters: list[dict] = []
    book_title = ""
    i = 0
    while i < len(lines):
        line = lines[i]
        if line.strip() == HEADER:
            i += 1
            continue
        if line.startswith("# ") and not line.startswith("##"):
            book_title = line[2:].strip()
            i += 1
            continue
        if line.startswith("## "):
            chapter_title = line[3:].strip() or f"Глава {len(chapters) + 1}"
            i += 1
            chapter_text_lines: list[str] = []
            scenes: list[tuple[str, str]] = []
            while i < len(lines):
                cur = lines[i]
                if cur.startswith("## "):
                    break
                if cur.strip() == MARKER_CHAPTER_TEXT:
                    i += 1
                    while i < len(lines) and not lines[i].startswith("### ") and not lines[i].startswith("## "):
                        chapter_text_lines.append(lines[i])
                        i += 1
                    continue
                if cur.startswith("### "):
                    scene_title = cur[4:].strip() or f"Сцена {len(scenes) + 1}"
                    i += 1
                    body_lines: list[str] = []
                    while i < len(lines) and not lines[i].startswith("### ") and not lines[i].startswith("## "):
                        if lines[i].strip() == MARKER_CHAPTER_TEXT:
                            break
                        body_lines.append(lines[i])
                        i += 1
                    scenes.append((scene_title, "\n".join(body_lines).strip()))
                    continue
                i += 1
            chapters.append(
                _build_chapter_dict(
                    len(chapters) + 1,
                    chapter_title,
                    "\n".join(chapter_text_lines).strip(),
                    scenes,
                )
            )
            continue
        i += 1
    if book_title and chapters:
        chapters[0]["_book_title"] = book_title
    return chapters


def _parse_legacy_android_txt(text: str) -> list[dict]:
    lines = text.split("\n")
    chapters: list[dict] = []
    i = 0
    if i < len(lines) and i + 1 < len(lines) and RE_BOOK_UNDERLINE.match(lines[i + 1].strip()):
        i += 2
    while i < len(lines):
        if i + 1 < len(lines) and RE_CHAPTER_UNDERLINE.match(lines[i + 1].strip()):
            chapter_title = lines[i].strip() or f"Глава {len(chapters) + 1}"
            i += 2
            chapter_text_lines: list[str] = []
            scenes: list[tuple[str, str]] = []
            while i < len(lines):
                m = RE_ANDROID_SCENE.match(lines[i].strip())
                if m:
                    scene_title = m.group(1).strip()
                    i += 1
                    body_lines: list[str] = []
                    while i < len(lines):
                        if RE_ANDROID_SCENE.match(lines[i].strip()):
                            break
                        if i + 1 < len(lines) and RE_CHAPTER_UNDERLINE.match(lines[i + 1].strip()):
                            break
                        body_lines.append(lines[i])
                        i += 1
                    scenes.append((scene_title, "\n".join(body_lines).strip()))
                    continue
                if i + 1 < len(lines) and RE_CHAPTER_UNDERLINE.match(lines[i + 1].strip()):
                    break
                if lines[i].strip():
                    chapter_text_lines.append(lines[i])
                i += 1
            chapters.append(
                _build_chapter_dict(
                    len(chapters) + 1,
                    chapter_title,
                    "\n".join(chapter_text_lines).strip(),
                    scenes,
                )
            )
            continue
        i += 1
    return chapters


def _parse_legacy_desktop_txt(text: str) -> list[dict]:
    lines = text.split("\n")
    chapters: list[dict] = []
    i = 0
    while i < len(lines) and not lines[i].strip():
        i += 1
    # skip book title block
    if i < len(lines):
        i += 1
        while i < len(lines) and not lines[i].strip():
            i += 1
    while i < len(lines):
        if i + 1 < len(lines) and RE_CHAPTER_UNDERLINE.match(lines[i + 1].strip()):
            chapter_title = lines[i].strip() or f"Глава {len(chapters) + 1}"
            i += 2
            buffer: list[str] = []
            scenes: list[tuple[str, str]] = []
            while i < len(lines):
                if i + 1 < len(lines) and RE_CHAPTER_UNDERLINE.match(lines[i + 1].strip()):
                    break
                buffer.append(lines[i])
                i += 1
            scenes, chapter_text = _split_chapter_buffer(buffer)
            chapters.append(_build_chapter_dict(len(chapters) + 1, chapter_title, chapter_text, scenes))
            continue
        i += 1
    return chapters


def _split_chapter_buffer(buffer: list[str]) -> tuple[list[tuple[str, str]], str]:
    """Разделить буфер главы десктоп-экспорта на сцены (по пустой строке + заголовку)."""
    scenes: list[tuple[str, str]] = []
    chapter_lines: list[str] = []
    i = 0
    meta_prefixes = (
        "Дата/время:",
        "Цель:",
        "Конфликт:",
        "Результат:",
        "Статус:",
        "Заметки:",
        "Персонажи:",
        "Локации:",
        "Объекты:",
        "Сюжетные линии:",
        "Целевой объём",
        "Заметки главы:",
    )
    while i < len(buffer):
        if not buffer[i].strip():
            i += 1
            continue
        if buffer[i].startswith("Заметки главы:"):
            i += 1
            while i < len(buffer) and buffer[i].strip():
                i += 1
            continue
        heading = buffer[i].strip()
        j = i + 1
        meta: list[str] = []
        while j < len(buffer) and buffer[j].strip().startswith(meta_prefixes):
            meta.append(buffer[j])
            j += 1
        body_lines: list[str] = []
        while j < len(buffer):
            if not buffer[j].strip():
                if j + 1 < len(buffer) and buffer[j + 1].strip() and not buffer[j + 1].strip().startswith(meta_prefixes):
                    peek = buffer[j + 1].strip()
                    if not any(peek.startswith(p) for p in meta_prefixes) and len(peek) < 120:
                        break
                body_lines.append(buffer[j])
                j += 1
                continue
            if any(buffer[j].strip().startswith(p) for p in meta_prefixes):
                break
            body_lines.append(buffer[j])
            j += 1
        body = "\n".join(body_lines).strip()
        if body or meta:
            scenes.append((heading, body))
            i = j
            continue
        chapter_lines.append(buffer[i])
        i += 1
    if not scenes:
        return [], "\n".join(chapter_lines).strip()
    if not scenes and chapter_lines:
        return [("Сцена 1", "\n".join(chapter_lines).strip())], ""
    if scenes and not scenes[0][1] and len(scenes) == 1 and chapter_lines:
        return [], "\n".join(chapter_lines + [scenes[0][0]]).strip()
    return scenes, "\n".join(chapter_lines).strip()


def _build_chapter_dict(
    index: int,
    title: str,
    chapter_text: str,
    scenes: list[tuple[str, str]],
) -> dict:
    if not scenes:
        scenes = [("Сцена 1", "")]
    scene_dicts = []
    for i, (scene_title, body) in enumerate(scenes):
        scene_dicts.append(
            {
                "id": f"scene_{index:03d}_{i+1:03d}",
                "title": scene_title,
                "text": body,
                "status": "draft",
                "targetWordCount": 0,
                "dateTime": "",
                "plotlineId": "plotline_main",
                "plotlineIds": ["plotline_main"],
                "characterIds": [],
                "locationIds": [],
                "objectIds": [],
                "goal": "",
                "conflict": "",
                "result": "",
                "description": "",
                "notes": "",
                "characterArcPoints": [],
            }
        )
    return {
        "id": f"chapter_{index:03d}",
        "title": title,
        "number": index,
        "subtitle": "",
        "epigraph": "",
        "synopsis": "",
        "notes": "",
        "status": "in_progress",
        "targetWordCount": 0,
        "chapterText": chapter_text,
        "chapterTextHtml": "",
        "chapterTextSections": [],
        "scenes": scene_dicts,
    }
