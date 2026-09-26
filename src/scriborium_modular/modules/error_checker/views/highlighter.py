from __future__ import annotations

from PySide6.QtGui import QColor, QTextCharFormat, QTextCursor, QTextFormat


class ErrorHighlighter:
    def __init__(self, editor) -> None:
        self._editor = editor

    def apply(self, issues) -> None:
        selections = []
        for issue in issues:
            selection = self._editor.ExtraSelection()
            cursor = QTextCursor(self._editor.document())
            cursor.setPosition(issue.start)
            cursor.setPosition(issue.start + issue.length, QTextCursor.KeepAnchor)
            selection.cursor = cursor
            fmt = QTextCharFormat()
            fmt.setUnderlineColor(QColor("#2d7ef7"))
            fmt.setUnderlineStyle(QTextCharFormat.SpellCheckUnderline)
            fmt.setProperty(QTextFormat.ToolTip, issue.message)
            selection.format = fmt
            selections.append(selection)
        self._editor.setExtraSelections(selections)
