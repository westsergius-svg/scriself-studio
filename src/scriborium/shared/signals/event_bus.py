from __future__ import annotations

from typing import Any, Callable
from collections import defaultdict

from PySide6.QtCore import QObject, Signal


class EventBus(QObject):
    """Thread-safe application-wide event bus using Qt signals."""

    _event = Signal(str, object)

    def __init__(self) -> None:
        super().__init__()
        self._handlers: dict[str, list[Callable[[Any], None]]] = defaultdict(list)
        self._event.connect(self._on_event)

    def subscribe(self, event_name: str, handler: Callable[[Any], None]) -> None:
        if handler not in self._handlers[event_name]:
            self._handlers[event_name].append(handler)

    def unsubscribe(self, event_name: str, handler: Callable[[Any], None]) -> None:
        if handler in self._handlers[event_name]:
            self._handlers[event_name].remove(handler)

    def emit(self, event_name: str, data: Any | None = None) -> None:
        self._event.emit(event_name, data)

    def _on_event(self, event_name: str, data: Any) -> None:
        for handler in list(self._handlers.get(event_name, [])):
            try:
                handler(data)
            except Exception:
                pass


event_bus = EventBus()
