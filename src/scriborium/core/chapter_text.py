"""chapterText / chapterTextHtml — общая логика для десктопа и экспорта."""
from __future__ import annotations

import html as html_module
import re


def html_to_plain_chapter(chapter_html: str) -> str:
    if not chapter_html:
        return ""
    text = re.sub(r"<\s*br\s*/?\s*>", "\n", chapter_html, flags=re.IGNORECASE)
    text = re.sub(r"</\s*(p|div|h[1-6]|li)\s*>", "\n", text, flags=re.IGNORECASE)
    text = re.sub(r"<[^>]+>", "", text)
    return html_module.unescape(text).strip()


def plain_to_chapter_html_fragment(plain_text: str) -> str:
    if not plain_text.strip():
        return ""
    return f"<p>{html_module.escape(plain_text).replace(chr(10), '<br/>')}</p>"


def chapter_text_html_for_editor(chapter: dict) -> str:
    stored_html = str(chapter.get("chapterTextHtml", "")).strip()
    stored_plain = str(chapter.get("chapterText", "")).strip()
    html_plain = html_to_plain_chapter(stored_html) if stored_html else ""
    if stored_plain and (not html_plain or len(stored_plain) > len(html_plain)):
        return plain_to_chapter_html_fragment(stored_plain)
    if stored_html and html_plain:
        return stored_html
    return ""


def chapter_free_text_plain(chapter: dict) -> str:
    stored_plain = str(chapter.get("chapterText", "")).strip()
    chapter_html = str(chapter.get("chapterTextHtml", "")).strip()
    html_plain = html_to_plain_chapter(chapter_html) if chapter_html else ""
    if stored_plain and (not html_plain or len(stored_plain) >= len(html_plain)):
        return stored_plain
    return html_plain
