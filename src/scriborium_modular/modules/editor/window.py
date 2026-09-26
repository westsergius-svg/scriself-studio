from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QLabel,
    QListWidget,
    QListWidgetItem,
    QMainWindow,
    QPlainTextEdit,
    QSplitter,
    QVBoxLayout,
    QWidget,
)


class EditorWindow(QMainWindow):
    def __init__(self, context, project_model) -> None:
        super().__init__()
        self._context = context
        self._project = project_model

        self.setWindowTitle("Scriborium Modular — Editor")
        self.resize(1100, 700)

        root = QWidget()
        layout = QVBoxLayout(root)

        splitter = QSplitter(Qt.Horizontal)

        self._chapters = QListWidget()
        self._chapters.currentRowChanged.connect(self._on_chapter_selected)

        right = QWidget()
        right_layout = QVBoxLayout(right)
        self._chapter_label = QLabel("Глава")
        self._editor = QPlainTextEdit()
        self._editor.textChanged.connect(self._on_text_changed)
        right_layout.addWidget(self._chapter_label)
        right_layout.addWidget(self._editor)

        splitter.addWidget(self._chapters)
        splitter.addWidget(right)
        splitter.setStretchFactor(1, 1)

        self._timeline_label = QLabel()

        layout.addWidget(splitter)
        layout.addWidget(self._timeline_label)

        self.setCentralWidget(root)

        for chapter in self._project.chapters:
            self._chapters.addItem(QListWidgetItem(chapter.title))

        if self._project.chapters:
            self._chapters.setCurrentRow(0)

        error_checker = self._context.module_loader.get_module("error_checker")
        error_checker.attach_to_editor(self._editor)

    def _on_chapter_selected(self, row: int) -> None:
        if row < 0 or row >= len(self._project.chapters):
            return
        chapter = self._project.chapters[row]
        self._chapter_label.setText(chapter.title)
        scene = chapter.scenes[0] if chapter.scenes else None
        self._editor.setPlainText(scene.text if scene else "")
        self._render_timeline_stats()

    def _on_text_changed(self) -> None:
        row = self._chapters.currentRow()
        if row < 0 or row >= len(self._project.chapters):
            return
        chapter = self._project.chapters[row]
        if chapter.scenes:
            chapter.scenes[0].text = self._editor.toPlainText()

        self._context.event_bus.emit(
            "text:changed",
            {"text": self._editor.toPlainText(), "editor": self._editor},
        )
        self._render_timeline_stats()

    def _render_timeline_stats(self) -> None:
        timeline = self._context.module_loader.get_module("timeline")
        rows = timeline.chapter_stats(self._project)
        pretty = " | ".join(f"{name}: {words} слов" for name, words in rows)
        self._timeline_label.setText(f"Объём глав: {pretty}")
