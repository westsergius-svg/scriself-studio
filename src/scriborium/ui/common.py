"""Common UI helpers reused across windows and dialogs."""
from __future__ import annotations

import html as html_module
import re

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QLineEdit, QPlainTextEdit, QTextEdit

from scriborium.core.chapter_text import (
    chapter_free_text_plain as _chapter_free_text_plain,
    chapter_text_html_for_editor as _chapter_text_html_for_editor,
)

ENTITY_KEYS = ("characters", "locations", "objects")


def _msg_from(messages: dict[str, str] | None, key: str, default: str) -> str:
    return str((messages or {}).get(key, default))


def _entity_label_from_messages(messages: dict[str, str] | None, kind: str) -> str:
    defaults = {
        "characters": "Characters",
        "locations": "Locations",
        "objects": "Objects",
    }
    return str((messages or {}).get(f"library_{kind}", defaults.get(kind, kind)))


def _entity_label(kind: str) -> str:
    return {"characters": "Персонажи", "locations": "Локации", "objects": "Объекты"}.get(kind, kind)


def _count_words(text: str | None) -> int:
    if not text:
        return 0
    return len(re.findall(r"[A-Za-zА-Яа-яЁё0-9]+", str(text)))


def count_chapter_free_words(chapter: dict) -> int:
    """Word count for the free chapter editor only (not scene texts)."""
    return _count_words(_chapter_free_text_plain(chapter))


def _default_chapter_title(index: int) -> str:
    return f"Глава {index}"


def _default_scene_title(index: int) -> str:
    return f"Сцена {index}"


def _enforce_ltr(widget) -> None:
    """Force Left-to-Right on a text widget to prevent RTL inheritance bugs."""
    if isinstance(widget, QLineEdit):
        widget.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        widget.setAlignment(Qt.AlignmentFlag.AlignLeft)
    elif isinstance(widget, (QPlainTextEdit, QTextEdit)):
        option = widget.document().defaultTextOption()
        option.setTextDirection(Qt.LayoutDirection.LeftToRight)
        option.setAlignment(Qt.AlignmentFlag.AlignLeft)
        widget.document().setDefaultTextOption(option)
        widget.setLayoutDirection(Qt.LayoutDirection.LeftToRight)


def _sanitize_filename(name: str) -> str:
    return re.sub(r"[^\w\s-]", "", name).strip() or "untitled"


def _escape_html(text: str) -> str:
    return html_module.escape(text)


def _new_id(prefix: str, values: list[str]) -> str:
    max_num = 0
    for value in values:
        if not value.startswith(prefix + "_"):
            continue
        tail = value.split("_")[-1]
        if tail.isdigit():
            max_num = max(max_num, int(tail))
    return f"{prefix}_{max_num + 1:03d}"
