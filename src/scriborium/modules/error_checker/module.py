from __future__ import annotations

from typing import Any

from PySide6.QtCore import Qt, QTimer

from scriborium.modules.error_checker.core.checker_engine import CheckerEngine
from scriborium.modules.error_checker.core.error_model import ErrorCategory
from scriborium.modules.error_checker.core.spell_provider import SpellProvider
from scriborium.modules.error_checker.core.style_provider import StyleProvider
from scriborium.modules.error_checker.views.error_dock import ErrorDockWidget
from scriborium.modules.error_checker.views.highlighter import ErrorHighlighter
from scriborium.shared.signals.event_bus import event_bus


class ErrorCheckerModule:
    id = "error_checker"
    name = "Диспетчер ошибок"
    version = "1.0.0"
    dependencies: list[str] = []

    def __init__(self) -> None:
        self.engine = CheckerEngine()
        self.dock: ErrorDockWidget | None = None
        self.highlighter: ErrorHighlighter | None = None
        self._language = "ru"
        self._pending_text: str = ""
        self._check_timer = QTimer()
        self._check_timer.setSingleShot(True)
        self._check_timer.setInterval(500)
        self._check_timer.timeout.connect(self._do_check)
        self._spell_enabled = True

    def set_spell_enabled(self, enabled: bool) -> None:
        self._spell_enabled = bool(enabled)
        self._do_check()

    def initialize(self, context: Any) -> None:
        spell_service = getattr(context, "spellcheck_service", None)
        if spell_service:
            self.engine.register(SpellProvider(spell_service))
        self.engine.register(StyleProvider())
        event_bus.subscribe("text:changed", self._on_text_changed)
        event_bus.subscribe("document:loaded", self._on_document_loaded)

    def attach_to_editor(self, editor_window: Any) -> None:
        self.dock = ErrorDockWidget(editor_window)
        editor_window.addDockWidget(Qt.DockWidgetArea.RightDockWidgetArea, self.dock)
        self.dock.refresh_btn.clicked.connect(self._request_check)
        self.dock.set_on_navigate(lambda err: self._navigate_to_error(editor_window, err))
        if hasattr(editor_window, "editor"):
            self.highlighter = ErrorHighlighter(editor_window.editor.document())

    def _on_text_changed(self, data: Any) -> None:
        text = data.get("text", "") if isinstance(data, dict) else str(data)
        self._pending_text = text
        # Перезапускаем таймер: проверка через 500 мс после остановки печати
        self._check_timer.start()

    def _on_document_loaded(self, data: Any) -> None:
        text = data.get("text", "") if isinstance(data, dict) else ""
        lang = data.get("language", "ru") if isinstance(data, dict) else "ru"
        self._language = lang
        self._pending_text = text
        self._check_timer.start()

    def _request_check(self) -> None:
        self._check_timer.stop()
        self._do_check()

    def _do_check(self) -> None:
        text = self._pending_text
        if not text:
            errors: list[Any] = []
        else:
            errors = self.engine.check(text, self._language)
            if not self._spell_enabled:
                errors = [err for err in errors if err.category != ErrorCategory.ORPHOGRAPHY]
        if self.dock:
            self.dock.set_errors(errors)
        if self.highlighter:
            self.highlighter.set_errors(errors)
        event_bus.emit("errors:updated", {"errors": errors})

    def _navigate_to_error(self, editor_window: Any, err: Any) -> None:
        if hasattr(editor_window, "editor"):
            cursor = editor_window.editor.textCursor()
            cursor.setPosition(err.start)
            cursor.setPosition(err.end, cursor.MoveMode.KeepAnchor)
            editor_window.editor.setTextCursor(cursor)
            editor_window.editor.setFocus()
