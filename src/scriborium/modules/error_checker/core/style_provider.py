from __future__ import annotations

import re

from scriborium.modules.error_checker.core.error_model import TextError, ErrorSeverity, ErrorCategory


_FILLERS_RU = {
    "как бы", "в принципе", "наверное", "скажем", "типа",
    "в общем", "вроде", "короче", "значит", "ну",
}
_FILLERS_EN = {
    "really", "just", "basically", "actually", "literally",
    "probably", "maybe", "kind of", "sort of", "you know",
}


class StyleProvider:
    def check(self, text: str, language: str) -> list[TextError]:
        errors: list[TextError] = []
        errors.extend(self._check_double_spaces(text))
        errors.extend(self._check_long_sentences(text))
        errors.extend(self._check_fillers(text, language))
        errors.extend(self._check_repeats(text))
        return errors

    def _check_double_spaces(self, text: str) -> list[TextError]:
        errors: list[TextError] = []
        for match in re.finditer(r" {2,}", text):
            errors.append(
                TextError(
                    start=match.start(),
                    end=match.end(),
                    word="  ",
                    message="Двойной пробел",
                    severity=ErrorSeverity.STYLE,
                    category=ErrorCategory.DOUBLE_SPACE,
                )
            )
        return errors

    def _check_long_sentences(self, text: str) -> list[TextError]:
        errors: list[TextError] = []
        sentences = re.split(r"[.!?]+\s*", text)
        offset = 0
        for sentence in sentences:
            stripped = sentence.strip()
            if not stripped:
                offset += len(sentence) + 1
                continue
            words = re.findall(r"[A-Za-zА-Яа-яЁё0-9]+", stripped)
            if len(words) >= 28:
                start = text.find(stripped, offset)
                if start >= 0:
                    errors.append(
                        TextError(
                            start=start,
                            end=start + len(stripped),
                            word=stripped[:40] + "...",
                            message=f"Длинное предложение ({len(words)} слов)",
                            severity=ErrorSeverity.WARNING,
                            category=ErrorCategory.LONG_SENTENCE,
                        )
                    )
                    offset = start + len(stripped)
            else:
                offset += len(sentence) + 1
        return errors

    def _check_fillers(self, text: str, language: str) -> list[TextError]:
        errors: list[TextError] = []
        fillers = _FILLERS_RU if language == "ru" else _FILLERS_EN
        lower = text.lower()
        for filler in fillers:
            for match in re.finditer(re.escape(filler), lower):
                start = match.start()
                end = match.end()
                errors.append(
                    TextError(
                        start=start,
                        end=end,
                        word=text[start:end],
                        message=f"Слово-паразит: {filler}",
                        severity=ErrorSeverity.STYLE,
                        category=ErrorCategory.FILLER,
                    )
                )
        return errors

    def _check_repeats(self, text: str) -> list[TextError]:
        errors: list[TextError] = []
        words = re.findall(r"[A-Za-zА-Яа-яЁё]{4,}", text.lower())
        freq: dict[str, int] = {}
        positions: dict[str, list[int]] = {}
        for i, word in enumerate(words):
            freq[word] = freq.get(word, 0) + 1
            if word not in positions:
                positions[word] = []
            # approximate position in original text
        for word, count in freq.items():
            if count >= 5:
                for match in re.finditer(re.escape(word), text.lower()):
                    errors.append(
                        TextError(
                            start=match.start(),
                            end=match.end(),
                            word=text[match.start():match.end()],
                            message=f"Повтор слова ({count} раз): {word}",
                            severity=ErrorSeverity.STYLE,
                            category=ErrorCategory.REPEAT,
                        )
                    )
        return errors
