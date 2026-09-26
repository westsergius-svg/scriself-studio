from __future__ import annotations

from typing import Protocol

from scriborium.modules.error_checker.core.error_model import TextError


class CheckProvider(Protocol):
    def check(self, text: str, language: str) -> list[TextError]:
        ...


class CheckerEngine:
    def __init__(self) -> None:
        self._providers: list[CheckProvider] = []

    def register(self, provider: CheckProvider) -> None:
        self._providers.append(provider)

    def check(self, text: str, language: str) -> list[TextError]:
        errors: list[TextError] = []
        for provider in self._providers:
            try:
                errors.extend(provider.check(text, language))
            except Exception:
                pass
        errors.sort(key=lambda e: e.start)
        return errors
