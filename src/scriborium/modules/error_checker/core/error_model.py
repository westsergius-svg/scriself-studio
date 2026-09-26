from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class ErrorSeverity(str, Enum):
    SPELL = "spell"          # red underline
    STYLE = "style"          # blue underline
    WARNING = "warning"      # orange underline


class ErrorCategory(str, Enum):
    ORPHOGRAPHY = "orthography"
    REPEAT = "repeat"
    LONG_SENTENCE = "long_sentence"
    FILLER = "filler"
    DOUBLE_SPACE = "double_space"


@dataclass
class TextError:
    start: int
    end: int
    word: str
    message: str
    severity: ErrorSeverity
    category: ErrorCategory
    suggestions: list[str] | None = None
