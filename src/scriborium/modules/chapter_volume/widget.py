from __future__ import annotations

from PySide6.QtCore import Qt, QRect
from PySide6.QtGui import QColor, QFont, QMouseEvent, QPainter, QPen
from PySide6.QtWidgets import QToolTip, QWidget

from scriborium.ui.common import count_chapter_free_words

# Норма для главы: 10 000 слов = 100%, 5 секций по 20% (2 000 слов).
CHAPTER_WORD_GOAL = 10_000
SEGMENT_COUNT = 5
SEGMENT_WORDS = CHAPTER_WORD_GOAL // SEGMENT_COUNT
SEGMENT_COLORS = (
    QColor("#d62828"),  # 0–20%
    QColor("#f77f00"),  # 20–40%
    QColor("#fcbf49"),  # 40–60%
    QColor("#90be6d"),  # 60–80%
    QColor("#43aa66"),  # 80–100%
)
SEGMENT_EMPTY = QColor("#ece8e0")
SEGMENT_BORDER = QColor("#1a1a1a")


class ChapterVolumeWidget(QWidget):
    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self._chapters: list[dict] = []
        self._volumes: list[int] = []
        self.setMinimumHeight(180)
        self.setMaximumHeight(350)
        self.setMouseTracking(True)
        self._hover_index: int = -1

    def set_chapters(self, chapters: list[dict]) -> None:
        self._chapters = list(chapters)
        self._volumes = [count_chapter_free_words(ch) for ch in self._chapters]
        self.update()

    def _row_geometry(self, index: int) -> tuple[int, int, int, int, int]:
        """Return y, row_h, bar_x, bar_max_w, label_w for a chapter row."""
        w = self.width()
        h = self.height()
        row_h = min(28, max(18, (h - 16) // max(1, len(self._volumes)) - 6))
        y = 8 + index * (row_h + 6)
        label_w = 95
        bar_x = 100
        bar_max_w = max(120, w - bar_x - 56)
        return y, row_h, bar_x, bar_max_w, label_w

    def _bar_volume(self, volume: int) -> int:
        """Объём для полосы: не больше нормы 10 000 слов."""
        return min(max(0, volume), CHAPTER_WORD_GOAL)

    def _segment_fill_ratio(self, bar_volume: int, segment_index: int) -> float:
        start_words = segment_index * SEGMENT_WORDS
        if bar_volume <= start_words:
            return 0.0
        return min(1.0, (bar_volume - start_words) / SEGMENT_WORDS)

    def _draw_segmented_bar(
        self,
        painter: QPainter,
        bar_x: int,
        y: int,
        bar_max_w: int,
        row_h: int,
        bar_volume: int,
        *,
        hovered: bool,
    ) -> None:
        gap = 3
        segment_w = max(8, (bar_max_w - gap * (SEGMENT_COUNT - 1)) // SEGMENT_COUNT)
        inner_h = max(10, row_h - 4)
        inner_y = y + (row_h - inner_h) // 2

        for seg in range(SEGMENT_COUNT):
            sx = bar_x + seg * (segment_w + gap)
            outer = QRect(sx, inner_y, segment_w, inner_h)
            painter.setPen(QPen(SEGMENT_BORDER, 1))
            painter.setBrush(SEGMENT_EMPTY)
            painter.drawRect(outer)

            fill_ratio = self._segment_fill_ratio(bar_volume, seg)
            if fill_ratio <= 0:
                continue
            fill_w = max(1, int((segment_w - 2) * fill_ratio))
            fill_rect = QRect(sx + 1, inner_y + 1, fill_w, inner_h - 2)
            color = SEGMENT_COLORS[seg]
            if hovered:
                color = color.lighter(115)
            painter.setPen(QPen(SEGMENT_BORDER, 1))
            painter.setBrush(color)
            painter.drawRect(fill_rect)

    def paintEvent(self, event) -> None:  # noqa: N802
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        if not self._volumes:
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, "Нет глав")
            return

        font = QFont("Segoe UI", 9)
        painter.setFont(font)

        for i, (chapter, volume) in enumerate(zip(self._chapters, self._volumes)):
            y, row_h, bar_x, bar_max_w, label_w = self._row_geometry(i)
            title = str(chapter.get("title", f"Глава {i + 1}"))[:18]
            hovered = i == self._hover_index

            painter.setPen(QColor("#333333"))
            painter.drawText(
                QRect(0, y, label_w, row_h),
                Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter,
                title,
            )

            # Полоса не растёт выше 100% (10 000), число справа — фактическое.
            self._draw_segmented_bar(
                painter, bar_x, y, bar_max_w, row_h, self._bar_volume(volume), hovered=hovered
            )
            count_x = bar_x + bar_max_w + 8
            painter.setPen(QColor("#333333"))
            painter.drawText(
                QRect(count_x, y, self.width() - count_x, row_h),
                Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
                str(volume),
            )

    def mouseMoveEvent(self, event: QMouseEvent) -> None:  # noqa: N802
        if not self._volumes:
            return
        row_h = min(28, max(18, (self.height() - 16) // max(1, len(self._volumes)) - 6))
        idx = (event.pos().y() - 8) // (row_h + 6)
        if 0 <= idx < len(self._volumes):
            self._hover_index = idx
            ch = self._chapters[idx]
            vol = self._volumes[idx]
            pct = min(100.0, (vol / CHAPTER_WORD_GOAL) * 100.0)
            tip = (
                f"{ch.get('title', '')}: {vol} слов (свободный редактор)\n"
                f"Шкала: {pct:.0f}% от {CHAPTER_WORD_GOAL} (норма главы)"
            )
            if vol > CHAPTER_WORD_GOAL:
                tip += f"\nПолоса на 100%, счётчик: {vol}."
            QToolTip.showText(event.globalPosition().toPoint(), tip, self)
        else:
            self._hover_index = -1
        self.update()

    def mousePressEvent(self, event: QMouseEvent) -> None:  # noqa: N802
        if self._hover_index >= 0:
            from scriborium.shared.signals.event_bus import event_bus

            event_bus.emit("chapter_volume:clicked", {"index": self._hover_index})
