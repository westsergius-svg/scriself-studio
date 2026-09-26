from __future__ import annotations

from collections import defaultdict
from typing import Any, Callable


class EventBus:
    def __init__(self) -> None:
        self._listeners: dict[str, list[Callable[[Any], None]]] = defaultdict(list)

    def subscribe(self, event_name: str, callback: Callable[[Any], None]) -> None:
        self._listeners[event_name].append(callback)

    def emit(self, event_name: str, payload: Any = None) -> None:
        for callback in self._listeners[event_name]:
            callback(payload)
