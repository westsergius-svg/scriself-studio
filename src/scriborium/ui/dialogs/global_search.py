from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QCheckBox,
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from scriborium.core.search import SearchResult, SearchService
from scriborium.core.project import ProjectDocument


def _msg_from(messages: dict[str, str] | None, key: str, default: str) -> str:
    return str((messages or {}).get(key, default))


class GlobalSearchDialog(QDialog):
    def __init__(
        self,
        parent: QWidget,
        search_service: SearchService,
        document: ProjectDocument,
        messages: dict[str, str] | None = None,
    ) -> None:
        super().__init__(parent)
        self.search_service = search_service
        self.document = document
        self.messages = messages or {}
        self.results: list[SearchResult] = []
        self.selected_result: SearchResult | None = None

        self.setWindowTitle(_msg_from(self.messages, "search_project_title", "Search in Project"))
        self.resize(860, 560)

        layout = QVBoxLayout(self)
        self.query_input = QLineEdit()
        self.query_input.setPlaceholderText(_msg_from(self.messages, "search_query_placeholder", "Enter search text"))
        self.query_input.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        self.query_input.setAlignment(Qt.AlignmentFlag.AlignLeft)
        layout.addWidget(self.query_input)

        filters = QHBoxLayout()
        self.case_checkbox = QCheckBox(_msg_from(self.messages, "search_case_sensitive", "Case sensitive"))
        self.word_checkbox = QCheckBox(_msg_from(self.messages, "search_whole_word", "Whole word only"))
        filters.addWidget(self.case_checkbox)
        filters.addWidget(self.word_checkbox)
        filters.addStretch(1)
        find_button = QPushButton(_msg_from(self.messages, "btn_find", "Find"))
        find_button.clicked.connect(self._run_search)
        filters.addWidget(find_button)
        layout.addLayout(filters)

        self.results_list = QListWidget()
        self.results_list.itemDoubleClicked.connect(self._accept_selected)
        layout.addWidget(self.results_list, 1)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self._accept_selected)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

    def _run_search(self) -> None:
        self.results_list.clear()
        self.selected_result = None
        self.results = self.search_service.search(
            document=self.document,
            query=self.query_input.text().strip(),
            case_sensitive=self.case_checkbox.isChecked(),
            whole_word=self.word_checkbox.isChecked(),
        )
        if not self.results:
            self.results_list.addItem(_msg_from(self.messages, "search_no_matches", "No matches found"))
            return
        for index, result in enumerate(self.results):
            item = QListWidgetItem(f"{result.chapter_title} / {result.scene_title} — {result.snippet}")
            item.setData(Qt.ItemDataRole.UserRole, index)
            self.results_list.addItem(item)
        self.results_list.setCurrentRow(0)

    def _accept_selected(self) -> None:
        item = self.results_list.currentItem()
        if item is None:
            return
        index = item.data(Qt.ItemDataRole.UserRole)
        if index is None:
            return
        self.selected_result = self.results[int(index)]
        self.accept()
