from __future__ import annotations

from dataclasses import dataclass


@dataclass
class TextIssue:
    start: int
    length: int
    message: str


class CheckerEngine:
    def check(self, text: str) -> list[TextIssue]:
        issues: list[TextIssue] = []
        double_space = text.find("  ")
        if double_space >= 0:
            issues.append(TextIssue(start=double_space, length=2, message="Двойной пробел"))
        return issues
