from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
    QLabel,
    QListWidget,
    QListWidgetItem,
    QMessageBox,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from scriborium.ui.dialogs.entity_edit import EntityEditDialog

ENTITY_KEYS = ("characters", "locations", "objects")


def _entity_label_from_messages(messages: dict[str, str] | None, kind: str) -> str:
    defaults = {
        "characters": "Characters",
        "locations": "Locations",
        "objects": "Objects",
    }
    return str((messages or {}).get(f"library_{kind}", defaults.get(kind, kind)))


def _msg_from(messages: dict[str, str] | None, key: str, default: str) -> str:
    return str((messages or {}).get(key, default))


class LibraryManagerDialog(QDialog):
    def __init__(self, parent: QWidget, library: dict, messages: dict[str, str] | None = None) -> None:
        super().__init__(parent)
        self.library = library
        self.messages = messages or {}
        self.select_hint: tuple[str, object] | None = None
        self.setWindowTitle(_msg_from(self.messages, "library_manage_title", "Library manager"))
        self.resize(620, 420)

        layout = QVBoxLayout(self)
        top = QHBoxLayout()
        top.addWidget(QLabel(_msg_from(self.messages, "entity_type_label", "Entity type")))
        self.kind_combo = QComboBox()
        for kind in ENTITY_KEYS:
            self.kind_combo.addItem(_entity_label_from_messages(self.messages, kind), kind)
        self.kind_combo.currentIndexChanged.connect(self._reload_list)
        top.addWidget(self.kind_combo)
        top.addStretch(1)
        layout.addLayout(top)

        self.list_widget = QListWidget()
        layout.addWidget(self.list_widget, 1)

        row = QHBoxLayout()
        add_btn = QPushButton(_msg_from(self.messages, "btn_add", "Add"))
        add_btn.clicked.connect(self._add_entity)
        edit_btn = QPushButton(_msg_from(self.messages, "btn_edit", "Edit"))
        edit_btn.clicked.connect(self._edit_entity)
        del_btn = QPushButton(_msg_from(self.messages, "btn_delete", "Delete"))
        del_btn.clicked.connect(self._delete_entity)
        row.addWidget(add_btn)
        row.addWidget(edit_btn)
        row.addWidget(del_btn)
        row.addStretch(1)
        layout.addLayout(row)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)
        self._reload_list()

    def _kind(self) -> str:
        return self.kind_combo.currentData()

    def _reload_list(self) -> None:
        kind = self._kind()
        self.list_widget.clear()
        for entity in self.library.get(kind, []):
            item = QListWidgetItem(entity.get("name", _msg_from(self.messages, "label_untitled", "Untitled")))
            item.setData(Qt.ItemDataRole.UserRole, entity.get("id"))
            self.list_widget.addItem(item)

    def _selected(self) -> tuple[int, dict] | None:
        item = self.list_widget.currentItem()
        if item is None:
            return None
        kind = self._kind()
        entity_id = item.data(Qt.ItemDataRole.UserRole)
        for i, entity in enumerate(self.library[kind]):
            if entity.get("id") == entity_id:
                return i, entity
        return None

    def _new_id(self, kind: str) -> str:
        prefix = {"characters": "char", "locations": "loc", "objects": "obj"}[kind]
        max_num = 0
        for entity in self.library[kind]:
            value = entity.get("id", "")
            if value.startswith(prefix + "_") and value.split("_")[-1].isdigit():
                max_num = max(max_num, int(value.split("_")[-1]))
        return f"{prefix}_{max_num + 1:03d}"

    def _add_entity(self) -> None:
        kind = self._kind()
        dialog = EntityEditDialog(self, kind, None, self.messages)
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return
        payload = dialog.to_entity()
        payload["id"] = self._new_id(kind)
        payload["sceneAppearances"] = []
        self.library[kind].append(payload)
        self.select_hint = ("library_entity", (kind, payload["id"]))
        self._reload_list()

    def _edit_entity(self) -> None:
        selected = self._selected()
        if selected is None:
            QMessageBox.information(
                self,
                _msg_from(self.messages, "edit_title", "Edit"),
                _msg_from(self.messages, "msg_select_entity", "Select an entity."),
            )
            return
        _, entity = selected
        kind = self._kind()
        dialog = EntityEditDialog(self, kind, entity, self.messages)
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return
        entity.update(dialog.to_entity())
        self.select_hint = ("library_entity", (kind, entity.get("id")))
        self._reload_list()

    def _delete_entity(self) -> None:
        selected = self._selected()
        if selected is None:
            QMessageBox.information(
                self,
                _msg_from(self.messages, "edit_title", "Edit"),
                _msg_from(self.messages, "msg_select_entity", "Select an entity."),
            )
            return
        index, entity = selected
        answer = QMessageBox.question(
            self,
            _msg_from(self.messages, "delete_title", "Delete"),
            _msg_from(self.messages, "msg_delete_entity_named", 'Delete "{name}"?').format(
                name=entity.get("name", "")
            ),
        )
        if answer != QMessageBox.StandardButton.Yes:
            return
        kind = self._kind()
        self.library[kind].pop(index)
        self.select_hint = None
        self._reload_list()
