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
    QVBoxLayout,
    QWidget,
)

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


class SceneLinkDialog(QDialog):
    def __init__(
        self,
        parent: QWidget,
        library: dict,
        scene: dict,
        messages: dict[str, str] | None = None,
    ) -> None:
        super().__init__(parent)
        self.library = library
        self.scene = scene
        self.messages = messages or {}
        self.setWindowTitle(_msg_from(self.messages, "scene_link_dialog_title", "Link entities to scene"))
        self.resize(620, 460)

        self.id_buckets = {
            "characters": list(scene.get("characterIds", [])),
            "locations": list(scene.get("locationIds", [])),
            "objects": list(scene.get("objectIds", [])),
        }

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

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self._accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

        self._reload_list()

    def _kind(self) -> str:
        return self.kind_combo.currentData()

    def _reload_list(self) -> None:
        kind = self._kind()
        self.list_widget.clear()
        selected = set(self.id_buckets[kind])
        for entity in self.library.get(kind, []):
            item = QListWidgetItem(entity.get("name", _msg_from(self.messages, "label_untitled", "Untitled")))
            item.setFlags(item.flags() | Qt.ItemFlag.ItemIsUserCheckable)
            item.setData(Qt.ItemDataRole.UserRole, entity.get("id"))
            item.setCheckState(Qt.CheckState.Checked if entity.get("id") in selected else Qt.CheckState.Unchecked)
            self.list_widget.addItem(item)

    def _save_current_kind(self) -> None:
        kind = self._kind()
        selected: list[str] = []
        for i in range(self.list_widget.count()):
            item = self.list_widget.item(i)
            if item.checkState() == Qt.CheckState.Checked:
                selected.append(item.data(Qt.ItemDataRole.UserRole))
        self.id_buckets[kind] = selected

    def _accept(self) -> None:
        self._save_current_kind()
        self.accept()

    def selected_ids(self, kind: str) -> list[str]:
        return list(self.id_buckets[kind])
