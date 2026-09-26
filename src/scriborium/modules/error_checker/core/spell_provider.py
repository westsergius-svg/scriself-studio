from __future__ import annotations

from scriborium.modules.error_checker.core.error_model import TextError, ErrorSeverity, ErrorCategory
from scriborium.core.spellcheck import SpellcheckService


class SpellProvider:
    def __init__(self, service: SpellcheckService) -> None:
        self.service = service

    def check(self, text: str, language: str) -> list[TextError]:
        errors: list[TextError] = []
        # Один проход через SpellChecker — не создаём его для каждого слова
        spell_errors = self.service.check_words(text, language)
        for err in spell_errors:
            errors.append(
                TextError(
                    start=err["start"],
                    end=err["end"],
                    word=err["word"],
                    message=f"Неизвестное слово: {err['word']}",
                    severity=ErrorSeverity.SPELL,
                    category=ErrorCategory.ORPHOGRAPHY,
                    suggestions=err["suggestions"][:4],
                )
            )
        return errors
