from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta

from PySide6.QtCore import QRectF, Qt, Signal
from PySide6.QtGui import QColor, QPainter, QPen, QWheelEvent
from PySide6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QFrame,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QScrollArea,
    QVBoxLayout,
    QWidget,
)

from scriborium.core.i18n import I18nService
from scriborium.core.settings import SettingsService
from scriborium.core.scene_fields import SCENE_STATUS_LABEL_RU, SCENE_STATUS_ORDER, normalize_scene_status


def parse_scene_datetime(raw: object) -> datetime | None:
    value = str(raw or "").strip()
    if not value:
        return None
    for fmt in (
        "%d.%m.%Y %H:%M",
        "%d.%m.%Y %H.%M",
        "%d.%m.%Y %H:%M:%S",
        "%d.%m.%Y",
        "%Y-%m-%d %H:%M",
        "%Y-%m-%d %H:%M:%S",
        "%Y-%m-%d",
        "%d/%m/%Y %H:%M",
        "%d/%m/%Y",
    ):
        try:
            parsed = datetime.strptime(value, fmt)
            if fmt in {"%d.%m.%Y", "%Y-%m-%d", "%d/%m/%Y"}:
                return parsed.replace(hour=0, minute=0, second=0, microsecond=0)
            return parsed.replace(second=0, microsecond=0)
        except ValueError:
            continue
    return None


def format_axis_datetime(value: datetime) -> str:
    return value.strftime("%d.%m.%Y %H:%M")


def _best_tick_minutes(span_minutes: float) -> int:
    if span_minutes <= 240:
        return 30
    if span_minutes <= 24 * 60:
        return 120
    if span_minutes <= 3 * 24 * 60:
        return 360
    if span_minutes <= 14 * 24 * 60:
        return 24 * 60
    if span_minutes <= 90 * 24 * 60:
        return 7 * 24 * 60
    if span_minutes <= 365 * 24 * 60:
        return 30 * 24 * 60
    return 90 * 24 * 60


@dataclass(slots=True)
class TimelineScenePoint:
    chapter_index: int
    scene_index: int
    chapter_title: str
    scene_title: str
    scene: dict
    dt: datetime
    minutes: float
    plotline_ids: list[str]
    x: float = 0.0


class TimelineCanvas(QWidget):
    def __init__(self, timeline: TimelineWidget) -> None:
        super().__init__()
        self.setObjectName("timelineCanvas")
        self._timeline = timeline
        self._baseline: datetime | None = None
        self._tick_minutes = 60
        self._plotlines: list[dict] = []
        self._points: list[TimelineScenePoint] = []
        self._segments: dict[str, list[tuple[float, float]]] = {}
        self._scene_rects: list[tuple[QRectF, TimelineScenePoint]] = []
        self._ordered_mode = False
        self.setMouseTracking(True)
        self.setMinimumHeight(260)

    def set_model(
        self,
        *,
        baseline: datetime | None,
        tick_minutes: int,
        plotlines: list[dict],
        points: list[TimelineScenePoint],
        segments: dict[str, list[tuple[float, float]]],
        total_width: int,
        ordered_mode: bool = False,
    ) -> None:
        self._baseline = baseline
        self._tick_minutes = tick_minutes
        self._plotlines = plotlines
        self._points = points
        self._segments = segments
        self._ordered_mode = ordered_mode
        height = max(240, 70 + len(plotlines) * 52)
        viewport_width = 0
        parent = self.parent()
        if parent is not None and hasattr(parent, "viewport"):
            viewport_width = int(parent.viewport().width())
        target_width = max(900, total_width, max(0, viewport_width - 6))
        self.setMinimumSize(target_width, height)
        self.resize(target_width, height)
        self.update()

    def wheelEvent(self, event: QWheelEvent) -> None:
        if event.modifiers() & Qt.KeyboardModifier.ControlModifier:
            self._timeline.adjust_zoom(event.angleDelta().y(), event.position().x())
        else:
            self._timeline.scroll_horizontally(event.angleDelta().y())
        event.accept()

    def mousePressEvent(self, event) -> None:  # type: ignore[override]
        if event.button() != Qt.MouseButton.LeftButton:
            return super().mousePressEvent(event)
        for rect, point in reversed(self._scene_rects):
            if rect.contains(event.position()):
                self._timeline.scene_activated.emit(point.chapter_index, point.scene_index)
                event.accept()
                return
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event) -> None:  # type: ignore[override]
        for rect, point in reversed(self._scene_rects):
            if rect.contains(event.position()):
                self.setToolTip(
                    f"{point.chapter_title} / {point.scene_title}\n"
                    f"{format_axis_datetime(point.dt)}\n"
                    f"{self._timeline._msg('label_plotlines', 'Plotlines')}: {', '.join(point.plotline_ids)}"
                )
                return
        self.setToolTip("")
        super().mouseMoveEvent(event)

    def paintEvent(self, _event) -> None:  # type: ignore[override]
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing, True)
        painter.fillRect(self.rect(), QColor("#f5f6fa"))

        left_pad = 120
        top_pad = 46
        row_gap = 52
        row_y_map: dict[str, float] = {}
        for index, plotline in enumerate(self._plotlines):
            row_y = top_pad + index * row_gap
            row_y_map[str(plotline.get("id", ""))] = row_y
            painter.setPen(QPen(QColor("#64748b")))
            painter.drawText(10, int(row_y + 5), str(plotline.get("name", plotline.get("id", ""))))

        if self._baseline is not None and self._points and not self._ordered_mode:
            max_minutes = max(point.minutes for point in self._points)
            axis_y = top_pad - 18
            painter.setPen(QPen(QColor("#94a3b8"), 1))
            painter.drawLine(left_pad, axis_y, self.width() - 20, axis_y)

            tick = 0.0
            while tick <= max_minutes + self._tick_minutes:
                x = left_pad + self._timeline.minutes_to_pixels(tick)
                painter.drawLine(int(x), axis_y - 4, int(x), self.height() - 18)
                tick_dt = self._baseline + timedelta(minutes=tick)
                painter.setPen(QPen(QColor("#475569"), 1))
                painter.drawText(int(x + 4), axis_y - 8, format_axis_datetime(tick_dt))
                painter.setPen(QPen(QColor("#d8dee9"), 1))
                tick += self._tick_minutes

        for plotline in self._plotlines:
            plotline_id = str(plotline.get("id", ""))
            row_y = row_y_map.get(plotline_id)
            if row_y is None:
                continue
            color = QColor(str(plotline.get("color", "#4A7DFF")))
            painter.setPen(QPen(color, 6, Qt.PenStyle.SolidLine, Qt.PenCapStyle.RoundCap))
            for start_x, end_x in self._segments.get(plotline_id, []):
                painter.drawLine(int(start_x), int(row_y), int(end_x), int(row_y))

        self._scene_rects.clear()
        for point in self._points:
            rows = [row_y_map.get(plotline_id) for plotline_id in point.plotline_ids if plotline_id in row_y_map]
            if not rows:
                continue
            top = min(rows) - 14
            bottom = max(rows) + 14
            rect = QRectF(point.x - 10, top, 20, bottom - top)
            fill = QColor("#ffffff")
            border = QColor("#334155")
            if self._timeline.active_scene() == (point.chapter_index, point.scene_index):
                fill = QColor("#fde68a")
                border = QColor("#b45309")
            painter.setPen(QPen(border, 2))
            painter.setBrush(fill)
            painter.drawRect(rect)
            self._scene_rects.append((rect, point))

            painter.setPen(QPen(QColor("#1f2937"), 1))
            painter.drawText(
                QRectF(point.x - 48, top - 22, 120, 18),
                Qt.AlignmentFlag.AlignLeft,
                point.scene_title,
            )


class TimelineWidget(QWidget):
    scene_activated = Signal(int, int)
    plotline_added = Signal()
    scene_moved = Signal(int, int, int, int)
    scene_plotline_changed = Signal(int, int, str)

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setObjectName("timelineRoot")
        self._i18n_service = I18nService()
        self._settings_service = SettingsService()
        self._messages = self._i18n_service.messages(self._settings_service.load().language)
        self._content: dict = {}
        self._active: tuple[int, int] | None = None
        self._block_filter_signals = False
        self._pixels_per_minute = 0.18

        outer = QVBoxLayout(self)
        outer.setContentsMargins(4, 2, 4, 4)
        outer.setSpacing(4)

        header = QHBoxLayout()
        title = QLabel(self._msg("timeline_title", "Таймлайн"))
        title.setObjectName("timelineTitle")
        header.addWidget(title)
        self._show_arcs = QCheckBox(self._msg("timeline_show_arcs", "Показывать арки"))
        self._show_arcs.toggled.connect(self.rebuild)
        header.addWidget(self._show_arcs)
        self._zoom_label = QLabel(self._msg("timeline_zoom_default", "Масштаб: 100%"))
        self._zoom_label.setObjectName("timelineZoomLabel")
        header.addWidget(self._zoom_label)
        header.addStretch(1)
        outer.addLayout(header)

        filters = QHBoxLayout()
        self._filter_character = self._make_filter_combo(filters, self._msg("timeline_filter_character", "Персонаж"))
        self._filter_location = self._make_filter_combo(filters, self._msg("timeline_filter_location", "Локация"))
        self._filter_plotline = self._make_filter_combo(filters, self._msg("timeline_filter_plotline", "Сюжетная линия"))
        self._filter_status = self._make_filter_combo(filters, self._msg("timeline_filter_status", "Статус"))
        self._arc_character = self._make_filter_combo(filters, self._msg("timeline_filter_arc", "Арка"))
        self._add_plotline_btn = QPushButton(self._msg("timeline_add_plotline", "Добавить линию"))
        self._add_plotline_btn.clicked.connect(self._on_add_plotline_clicked)
        filters.addWidget(self._add_plotline_btn)
        filters.addStretch(1)
        outer.addLayout(filters)

        self._hint = QLabel("")
        self._hint.setObjectName("timelineHint")
        outer.addWidget(self._hint)

        self._scroll = QScrollArea()
        self._scroll.setWidgetResizable(False)
        self._scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self._scroll.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self._scroll.setFrameShape(QFrame.Shape.StyledPanel)
        self._canvas = TimelineCanvas(self)
        self._scroll.setWidget(self._canvas)
        outer.addWidget(self._scroll, 1)
        self._update_zoom_label()

    def _msg(self, key: str, default: str) -> str:
        return str(self._messages.get(key, default))

    def _make_filter_combo(self, layout: QHBoxLayout, label: str) -> QComboBox:
        layout.addWidget(QLabel(f"{label}:"))
        combo = QComboBox()
        combo.currentIndexChanged.connect(self._on_filter_changed)
        combo.setMinimumWidth(140)
        layout.addWidget(combo)
        return combo

    def active_scene(self) -> tuple[int, int] | None:
        return self._active

    def drag_allowed(self) -> bool:
        return False

    def set_content(self, content: dict) -> None:
        self._content = content
        self._populate_filter_combos()
        self.rebuild()

    def set_active_scene(self, chapter_index: int, scene_index: int) -> None:
        self._active = None if chapter_index < 0 or scene_index < 0 else (chapter_index, scene_index)
        self.rebuild()
        if self._active is not None:
            for rect, point in self._canvas._scene_rects:
                if (point.chapter_index, point.scene_index) == self._active:
                    self._scroll.ensureVisible(int(rect.center().x()), int(rect.center().y()), 80, 40)
                    break

    def refresh_filters_only(self) -> None:
        self._populate_filter_combos()
        self.rebuild()

    def scroll_horizontally(self, wheel_delta: int) -> None:
        bar = self._scroll.horizontalScrollBar()
        bar.setValue(bar.value() - int(wheel_delta / 2))

    def adjust_zoom(self, wheel_delta: int, anchor_x: float) -> None:
        old_value = self._pixels_per_minute
        factor = 1.12 if wheel_delta > 0 else 1 / 1.12
        self._pixels_per_minute = min(48.0, max(0.01, self._pixels_per_minute * factor))
        ratio = self._pixels_per_minute / old_value if old_value else 1.0
        bar = self._scroll.horizontalScrollBar()
        old_scroll = bar.value()
        self.rebuild()
        bar.setValue(int((old_scroll + anchor_x) * ratio - anchor_x))

    def minutes_to_pixels(self, minutes: float) -> float:
        return minutes * self._pixels_per_minute

    def _update_zoom_label(self) -> None:
        percent = max(1, int(self._pixels_per_minute / 0.18 * 100))
        self._zoom_label.setText(self._msg("timeline_zoom_format", "Масштаб: {percent}%").format(percent=percent))

    def _populate_filter_combos(self) -> None:
        self._block_filter_signals = True
        library = self._content.get("library", {})
        plotlines = self._content.get("timeline", {}).get("plotlines", [])

        def refill(combo: QComboBox, items: list[tuple[str, str | None]]) -> None:
            current = combo.currentData()
            combo.clear()
            combo.addItem(self._msg("timeline_all", "Все"), None)
            for label, data in items:
                combo.addItem(label, data)
            index = combo.findData(current)
            if index >= 0:
                combo.setCurrentIndex(index)

        refill(
            self._filter_character,
            [(entity.get("name", "?"), entity.get("id")) for entity in library.get("characters", []) if entity.get("id")],
        )
        refill(
            self._filter_location,
            [(entity.get("name", "?"), entity.get("id")) for entity in library.get("locations", []) if entity.get("id")],
        )
        refill(
            self._filter_plotline,
            [(plotline.get("name", "?"), plotline.get("id")) for plotline in plotlines if plotline.get("id")],
        )
        refill(
            self._arc_character,
            [
                (entity.get("name", "?"), entity.get("id"))
                for entity in library.get("characters", [])
                if entity.get("id") and entity.get("arcPoints")
            ],
        )

        current_status = self._filter_status.currentData()
        self._filter_status.clear()
        self._filter_status.addItem(self._msg("timeline_all", "Все"), None)
        for status_key in SCENE_STATUS_ORDER:
            self._filter_status.addItem(self._msg(f"scene_status_{status_key}", SCENE_STATUS_LABEL_RU[status_key]), status_key)
        index = self._filter_status.findData(current_status)
        if index >= 0:
            self._filter_status.setCurrentIndex(index)
        self._block_filter_signals = False

    def _on_filter_changed(self) -> None:
        if self._block_filter_signals:
            return
        self.rebuild()

    def _scene_passes_filters(self, scene: dict) -> bool:
        character_id = self._filter_character.currentData()
        if character_id and character_id not in scene.get("characterIds", []):
            return False
        location_id = self._filter_location.currentData()
        if location_id and location_id not in scene.get("locationIds", []):
            return False
        plotline_id = self._filter_plotline.currentData()
        if plotline_id and plotline_id not in (scene.get("plotlineIds", []) or [scene.get("plotlineId", "")]):
            return False
        status_key = self._filter_status.currentData()
        if status_key and normalize_scene_status(scene.get("status")) != status_key:
            return False
        arc_character_id = self._arc_character.currentData()
        if arc_character_id:
            arc_character_ids = {point.get("characterId") for point in scene.get("characterArcPoints", [])}
            if arc_character_id not in arc_character_ids:
                return False
        return True

    def _collect_points(self) -> tuple[datetime | None, list[dict], list[TimelineScenePoint]]:
        plotlines = [
            plotline
            for plotline in self._content.get("timeline", {}).get("plotlines", [])
            if not self._filter_plotline.currentData() or plotline.get("id") == self._filter_plotline.currentData()
        ]
        plotline_ids = {str(plotline.get("id", "")) for plotline in plotlines}

        points: list[TimelineScenePoint] = []
        narrative_index = 0
        for chapter_index, chapter in enumerate(self._content.get("chapters", [])):
            chapter_title = str(chapter.get("title", f"Глава {chapter_index + 1}"))
            for scene_index, scene in enumerate(chapter.get("scenes", [])):
                if not self._scene_passes_filters(scene):
                    continue
                ids = [plotline_id for plotline_id in (scene.get("plotlineIds", []) or [scene.get("plotlineId", "plotline_main")]) if plotline_id in plotline_ids]
                if not ids and plotlines:
                    continue
                point = TimelineScenePoint(
                    chapter_index=chapter_index,
                    scene_index=scene_index,
                    chapter_title=chapter_title,
                    scene_title=str(scene.get("title", f"Сцена {scene_index + 1}")),
                    scene=scene,
                    dt=parse_scene_datetime(scene.get("dateTime")),
                    minutes=0.0,
                    plotline_ids=ids or [str(scene.get("plotlineId", "plotline_main"))],
                )
                if point.dt is None:
                    point.minutes = float(narrative_index * 10)
                points.append(point)
                narrative_index += 1

        known = [point.dt for point in points if point.dt is not None]
        if not known:
            return None, plotlines, points

        baseline = min(known) - timedelta(minutes=10)
        latest_known_minutes = max((point.dt - baseline).total_seconds() / 60.0 for point in points if point.dt is not None)
        fallback_minutes = latest_known_minutes + 10.0
        for point in points:
            if point.dt is None:
                point.dt = baseline + timedelta(minutes=fallback_minutes)
                point.minutes = fallback_minutes
                fallback_minutes += 10.0
            else:
                point.minutes = (point.dt - baseline).total_seconds() / 60.0

        points.sort(key=lambda point: (point.minutes, point.chapter_index, point.scene_index))
        return baseline, plotlines, points

    def _build_segments(
        self,
        plotlines: list[dict],
        points: list[TimelineScenePoint],
        left_pad: int,
        ordered_mode: bool,
    ) -> tuple[dict[str, list[tuple[float, float]]], int]:
        if not points:
            return {}, 1200

        if ordered_mode:
            scene_spacing = 110.0
            first_offset = 38.0
            for index, point in enumerate(points):
                point.x = left_pad + first_offset + index * scene_spacing
            width = int(left_pad + first_offset * 2 + max(1, len(points) - 1) * scene_spacing + 120)
        else:
            for point in points:
                point.x = left_pad + self.minutes_to_pixels(point.minutes)
            total_minutes = max(point.minutes for point in points)
            width = int(left_pad + self.minutes_to_pixels(total_minutes + 120) + 80)
        xs = [point.x for point in points]
        boundaries: list[tuple[float, float]] = []
        for index, point in enumerate(points):
            left = left_pad if index == 0 else (xs[index - 1] + xs[index]) / 2
            right = width - 40 if index == len(points) - 1 else (xs[index] + xs[index + 1]) / 2
            boundaries.append((left, right))

        segments: dict[str, list[tuple[float, float]]] = {str(plotline.get("id", "")): [] for plotline in plotlines}
        for point, (left, right) in zip(points, boundaries):
            for plotline_id in point.plotline_ids:
                if plotline_id in segments:
                    segments[plotline_id].append((left, right))
        return segments, width

    def rebuild(self) -> None:
        self._update_zoom_label()
        baseline, plotlines, points = self._collect_points()
        left_pad = 120
        ordered_mode = baseline is None
        segments, width = self._build_segments(plotlines, points, left_pad, ordered_mode)
        span_minutes = max((point.minutes for point in points), default=60.0)
        tick_minutes = _best_tick_minutes(span_minutes)
        self._canvas.set_model(
            baseline=baseline,
            tick_minutes=tick_minutes,
            plotlines=plotlines,
            points=points,
            segments=segments,
            total_width=width,
            ordered_mode=ordered_mode,
        )

        if not points:
            self._hint.setText(self._msg("timeline_hint_empty", "Добавьте даты/время сценам, чтобы выстроить их на шкале. Колёсико мыши прокручивает шкалу, Ctrl+колёсико меняет масштаб."))
        elif baseline is None:
            self._hint.setText(self._msg("timeline_hint_no_dates", "У сцен ещё нет распознанного времени. Сейчас шкала построена по порядку сцен. Укажите дату вроде 23.03.1985 или 23.03.1985 14:30."))
        else:
            self._hint.setText(
                self._msg("timeline_hint_ready", "Шкала начинается с {baseline}. Колёсико мыши прокручивает шкалу, Ctrl+колёсико меняет масштаб.").format(
                    baseline=format_axis_datetime(baseline)
                )
            )

    def _on_add_plotline_clicked(self) -> None:
        from PySide6.QtWidgets import QColorDialog, QInputDialog

        name, ok = QInputDialog.getText(
            self,
            self._msg("timeline_new_plotline_title", "Новая сюжетная линия"),
            self._msg("timeline_name_label", "Название:"),
        )
        if not ok or not name.strip():
            return
        color = QColorDialog.getColor(parent=self)
        hex_color = color.name() if color.isValid() else "#888888"
        timeline = self._content.setdefault("timeline", {})
        plotlines = timeline.setdefault("plotlines", [])
        next_number = 1
        for plotline in plotlines:
            plotline_id = str(plotline.get("id", ""))
            if plotline_id.startswith("plotline_") and plotline_id.split("_")[-1].isdigit():
                next_number = max(next_number, int(plotline_id.split("_")[-1]) + 1)
        plotlines.append({"id": f"plotline_{next_number:03d}", "name": name.strip(), "color": hex_color})
        self._populate_filter_combos()
        self.rebuild()
        self.plotline_added.emit()
