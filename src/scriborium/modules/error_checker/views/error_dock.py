from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QDockWidget,
    QListWidget,
    QListWidgetItem,
    QVBoxLayout,
    QWidget,
    QLabel,
    QPushButton,
    QHBoxLayout,
)

from scriborium.modules.error_checker.core.error_model import TextError, ErrorSeverity




class ErrorDockWidget(QDockWidget):
    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__("Диспетчер ошибок", parent)
        self.setAllowedAreas(Qt.DockWidgetArea.LeftDockWidgetArea | Qt.DockWidgetArea.RightDockWidgetArea)

        container = QWidget()
        layout = QVBoxLayout(container)

        self.status_label = QLabel("Нет ошибок")
        layout.addWidget(self.status_label)

        self.list_widget = QListWidget()
        self.list_widget.itemClicked.connect(self._on_item_clicked)
        layout.addWidget(self.list_widget, 1)

        btn_row = QHBoxLayout()
        self.refresh_btn = QPushButton("Обновить")
        btn_row.addWidget(self.refresh_btn)
        btn_row.addStretch(1)
        layout.addLayout(btn_row)

        self.setWidget(container)
        self._errors: list[TextError] = []
        self._on_navigate: callable | None = None

    def set_errors(self, errors: list[TextError]) -> None:
        self._errors = errors
        self.list_widget.clear()
        counts = {"spell": 0, "style": 0, "warning": 0}
        for err in errors:
            counts[err.severity.value] = counts.get(err.severity.value, 0) + 1
            item = QListWidgetItem(f"{err.word}: {err.message}")
            item.setForeground(Qt.GlobalColor.red if err.severity == ErrorSeverity.SPELL else Qt.GlobalColor.blue)
            item.setData(Qt.ItemDataRole.UserRole, err)
            self.list_widget.addItem(item)
        self.status_label.setText(
            f"Орфография: {counts['spell']} | Стиль: {counts['style']} | Предупреждения: {counts['warning']}"
        )

    def set_on_navigate(self, callback: callable) -> None:
        self._on_navigate = callback

    def _on_item_clicked(self, item: QListWidgetItem) -> None:
        err = item.data(Qt.ItemDataRole.UserRole)
        if err and self._on_navigate:
            self._on_navigate(err)
