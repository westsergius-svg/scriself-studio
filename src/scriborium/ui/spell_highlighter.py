from __future__ import annotations

import re
from collections.abc import Callable

from PySide6.QtCore import QTimer
from PySide6.QtGui import QColor, QSyntaxHighlighter, QTextCharFormat

from scriborium.core.spellcheck import SpellcheckService


class LiveSpellHighlighter(QSyntaxHighlighter):
    def __init__(
        self,
        document,
        *,
        language_getter: Callable[[], str],
        spellcheck_service: SpellcheckService,
    ) -> None:
        super().__init__(document)
        self._language_getter = language_getter
        self._service = spellcheck_service
        self._token_re = re.compile(r"[A-Za-zА-Яа-яЁё'-]{2,}")
        self._spell_format = QTextCharFormat()
        self._spell_format.setUnderlineColor(QColor("#d62828"))
        self._spell_format.setUnderlineStyle(QTextCharFormat.UnderlineStyle.SpellCheckUnderline)
        self._grammar_format = QTextCharFormat()
        self._grammar_format.setUnderlineColor(QColor("#2563eb"))
        self._grammar_format.setUnderlineStyle(QTextCharFormat.UnderlineStyle.WaveUnderline)
        self._enabled = True
        self._rehighlight_timer = QTimer()
        self._rehighlight_timer.setSingleShot(True)
        self._rehighlight_timer.setInterval(120)
        self._rehighlight_timer.timeout.connect(self.rehighlight)
        if document is not None:
            document.contentsChanged.connect(self._schedule_rehighlight)

    def set_enabled(self, enabled: bool) -> None:
        self._enabled = bool(enabled)
        self.rehighlight()

    def _schedule_rehighlight(self, *_args) -> None:
        if self._enabled:
            self._rehighlight_timer.start()

    def _language_code(self) -> str:
        return (self._language_getter() or "ru").split("-")[0].lower()

    def reload_dictionary(self) -> None:
        self._service.invalidate_cache(self._language_code())
        self.rehighlight()

    def highlightBlock(self, text: str) -> None:  # type: ignore[override]
        if not self._enabled or not text:
            return

        lang = self._language_code()
        for match in self._token_re.finditer(text):
            token = match.group(0)
            if self._service.is_misspelled(token, lang):
                self.setFormat(match.start(), match.end() - match.start(), self._spell_format)

        for m in re.finditer(r" {2,}", text):
            self.setFormat(m.start(), m.end() - m.start(), self._grammar_format)

        for m in re.finditer(r"\b([A-Za-zА-Яа-яЁёÀ-ÿ]+)\s+\1\b", text, re.IGNORECASE):
            self.setFormat(m.start(), m.end() - m.start(), self._grammar_format)

        for m in re.finditer(r"(?<=[.!?]\s)([a-zа-яё])", text):
            self.setFormat(m.start(1), 1, self._grammar_format)

    def word_under_cursor(self, cursor) -> tuple[str, int, int] | None:
        block = cursor.block()
        text = block.text()
        pos = cursor.positionInBlock()
        for m in self._token_re.finditer(text):
            if m.start() <= pos < m.end():
                return m.group(0), m.start(), m.end()
        return None

    def is_misspelled(self, word: str) -> bool:
        return self._service.is_misspelled(word, self._language_code())

    def suggestions(self, word: str) -> list[str]:
        return self._service.suggestions(word, self._language_code(), limit=16)

    def after_correction(self) -> None:
        """Вызвать после замены слова — снять «залипшее» подчёркивание."""
        self._rehighlight_timer.stop()
        self.rehighlight()
        self._schedule_rehighlight()
