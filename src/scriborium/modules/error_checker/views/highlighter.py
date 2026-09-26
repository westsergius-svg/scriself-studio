from __future__ import annotations

from PySide6.QtGui import QColor, QTextCharFormat, QTextDocument, QSyntaxHighlighter

from scriborium.modules.error_checker.core.error_model import TextError, ErrorSeverity


class ErrorHighlighter(QSyntaxHighlighter):
    def __init__(self, document: QTextDocument) -> None:
        super().__init__(document)
        self._errors: list[TextError] = []
        self._spell_format = QTextCharFormat()
        self._spell_format.setUnderlineColor(QColor("#ff4444"))
        self._spell_format.setUnderlineStyle(QTextCharFormat.UnderlineStyle.SpellCheckUnderline)
        self._style_format = QTextCharFormat()
        self._style_format.setUnderlineColor(QColor("#4488ff"))
        self._style_format.setUnderlineStyle(QTextCharFormat.UnderlineStyle.SpellCheckUnderline)
        self._warning_format = QTextCharFormat()
        self._warning_format.setUnderlineColor(QColor("#ff8800"))
        self._warning_format.setUnderlineStyle(QTextCharFormat.UnderlineStyle.SpellCheckUnderline)

    def set_errors(self, errors: list[TextError]) -> None:
        self._errors = errors
        self.rehighlight()

    def highlightBlock(self, text: str) -> None:
        block_start = self.currentBlock().position()
        block_end = block_start + len(text)
        for err in self._errors:
            if err.end <= block_start or err.start >= block_end:
                continue
            start = max(err.start, block_start) - block_start
            end = min(err.end, block_end) - block_start
            length = end - start
            if length <= 0:
                continue
            fmt = self._spell_format
            if err.severity == ErrorSeverity.STYLE:
                fmt = self._style_format
            elif err.severity == ErrorSeverity.WARNING:
                fmt = self._warning_format
            self.setFormat(start, length, fmt)
