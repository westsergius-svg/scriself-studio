from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QDialog,
    QDialogButtonBox,
    QLabel,
    QLineEdit,
    QMessageBox,
    QPlainTextEdit,
    QVBoxLayout,
)


def _msg_from(messages: dict[str, str] | None, key: str, default: str) -> str:
    return str((messages or {}).get(key, default))


class EntityEditDialog(QDialog):
    def __init__(
        self,
        parent,
        kind: str,
        entity: dict | None = None,
        messages: dict[str, str] | None = None,
    ) -> None:
        super().__init__(parent)
        self.kind = kind
        self.entity = entity or {}
        self.messages = messages or {}
        self.setWindowTitle(_msg_from(self.messages, "entity_card_title", "Entity card"))
        self.resize(420, 220)

        layout = QVBoxLayout(self)
        self.name_input = QLineEdit(self.entity.get("name", ""))
        self.description_input = QPlainTextEdit(self.entity.get("description", ""))
        self.description_input.setMaximumHeight(90)
        self.extra_input = QLineEdit(self.entity.get("extra", ""))

        # Force LTR for all text inputs (prevents RTL inheritance issues)
        self.name_input.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        self.name_input.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.extra_input.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        self.extra_input.setAlignment(Qt.AlignmentFlag.AlignLeft)
        option = self.description_input.document().defaultTextOption()
        option.setTextDirection(Qt.LayoutDirection.LeftToRight)
        option.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.description_input.document().setDefaultTextOption(option)
        self.description_input.setLayoutDirection(Qt.LayoutDirection.LeftToRight)

        extra_label_default = "Role" if kind == "characters" else ("Type" if kind == "locations" else "Tag")
        extra_label_key = (
            "entity_extra_role"
            if kind == "characters"
            else ("entity_extra_type" if kind == "locations" else "entity_extra_tag")
        )
        extra_label = _msg_from(self.messages, extra_label_key, extra_label_default)

        layout.addWidget(QLabel(_msg_from(self.messages, "entity_name_label", "Name")))
        layout.addWidget(self.name_input)
        layout.addWidget(QLabel(_msg_from(self.messages, "entity_description_label", "Description")))
        layout.addWidget(self.description_input)
        layout.addWidget(QLabel(extra_label))
        layout.addWidget(self.extra_input)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self._accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

    def _accept(self) -> None:
        if not self.name_input.text().strip():
            QMessageBox.warning(
                self,
                _msg_from(self.messages, "msg_error", "Error"),
                _msg_from(self.messages, "entity_name_required", "Name cannot be empty."),
            )
            return
        self.accept()

    def to_entity(self) -> dict:
        return {
            "name": self.name_input.text().strip(),
            "description": self.description_input.toPlainText(),
            "extra": self.extra_input.text().strip(),
        }
