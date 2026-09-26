from __future__ import annotations

from dataclasses import dataclass

from scriborium_modular.modules.error_checker.core.checker_engine import CheckerEngine
from scriborium_modular.modules.error_checker.views.highlighter import ErrorHighlighter


@dataclass
class ErrorCheckerModule:
    id: str = "error_checker"
    name: str = "Error Checker"
    version: str = "1.0.0"

    def initialize(self, context) -> None:
        self._context = context
        self._engine = CheckerEngine()

    def attach_to_editor(self, editor) -> None:
        highlighter = ErrorHighlighter(editor)

        def on_text(payload) -> None:
            text = payload.get("text", "")
            target_editor = payload.get("editor")
            if target_editor is not editor:
                return
            issues = self._engine.check(text)
            highlighter.apply(issues)

        self._context.event_bus.subscribe("text:changed", on_text)
