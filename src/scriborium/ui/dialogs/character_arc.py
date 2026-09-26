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
    QPlainTextEdit,
    QPushButton,
    QSpinBox,
    QVBoxLayout,
    QWidget,
)


def _msg_from(messages: dict[str, str] | None, key: str, default: str) -> str:
    return str((messages or {}).get(key, default))


class CharacterArcPointDialog(QDialog):
    def __init__(
        self,
        parent: QWidget,
        chapters: list[dict],
        point: dict | None = None,
        messages: dict[str, str] | None = None,
    ) -> None:
        super().__init__(parent)
        self.chapters = chapters
        self.messages = messages or {}
        self.setWindowTitle(_msg_from(self.messages, "arc_point_title", "Arc Point"))
        self.resize(520, 280)

        layout = QVBoxLayout(self)
        scene_row = QHBoxLayout()
        scene_row.addWidget(QLabel(_msg_from(self.messages, "arc_scene_label", "Scene:")))
        self.scene_combo = QComboBox()
        self._fill_scenes()
        scene_row.addWidget(self.scene_combo, 1)
        layout.addLayout(scene_row)

        value_row = QHBoxLayout()
        value_row.addWidget(QLabel(_msg_from(self.messages, "arc_value_label", "Value (-10..+10):")))
        self.value_spin = QSpinBox()
        self.value_spin.setRange(-10, 10)
        self.value_spin.setValue(0)
        value_row.addWidget(self.value_spin)
        value_row.addStretch(1)
        layout.addLayout(value_row)

        layout.addWidget(QLabel(_msg_from(self.messages, "arc_description_label", "Description:")))
        self.description_input = QPlainTextEdit()
        layout.addWidget(self.description_input, 1)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

        if point:
            sid = point.get("sceneId", "")
            idx = self.scene_combo.findData(sid)
            if idx >= 0:
                self.scene_combo.setCurrentIndex(idx)
            self.value_spin.setValue(point.get("arcValue", 0))
            self.description_input.setPlainText(point.get("description", ""))

    def _fill_scenes(self) -> None:
        self.scene_combo.clear()
        for chapter in self.chapters:
            for scene in chapter.get("scenes", []):
                title = f"{chapter.get('title','')} / {scene.get('title','')}"
                self.scene_combo.addItem(title, scene.get("id", ""))

    def to_point(self) -> dict:
        return {
            "sceneId": self.scene_combo.currentData(),
            "arcValue": self.value_spin.value(),
            "description": self.description_input.toPlainText().strip(),
        }


class CharacterArcEditorDialog(QDialog):
    def __init__(
        self,
        parent: QWidget,
        character_name: str,
        chapters: list[dict],
        points: list[dict],
        messages: dict[str, str] | None = None,
    ) -> None:
        super().__init__(parent)
        self.character_name = character_name
        self.chapters = chapters
        self._points = [dict(p) for p in points]
        self.messages = messages or {}
        self.setWindowTitle(
            _msg_from(self.messages, "arc_editor_title", "Character arc — {name}").format(name=character_name)
        )
        self.resize(620, 420)

        layout = QVBoxLayout(self)
        self.list_widget = QListWidget()
        layout.addWidget(self.list_widget, 1)

        row = QHBoxLayout()
        add_btn = QPushButton(_msg_from(self.messages, "btn_add", "Add"))
        add_btn.clicked.connect(self._add_point)
        edit_btn = QPushButton(_msg_from(self.messages, "btn_edit", "Edit"))
        edit_btn.clicked.connect(self._edit_point)
        del_btn = QPushButton(_msg_from(self.messages, "btn_delete", "Delete"))
        del_btn.clicked.connect(self._remove_point)
        row.addWidget(add_btn)
        row.addWidget(edit_btn)
        row.addWidget(del_btn)
        row.addStretch(1)
        layout.addLayout(row)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)
        self._refresh()

    def _refresh(self) -> None:
        self.list_widget.clear()
        for point in self._points:
            scene_id = point.get("sceneId", "")
            title = self._scene_title(scene_id)
            text = f"{title} | {point.get('arcValue', 0)} | {point.get('description', '')[:60]}"
            item = QListWidgetItem(text)
            item.setData(Qt.ItemDataRole.UserRole, scene_id)
            self.list_widget.addItem(item)

    def _scene_title(self, scene_id: str) -> str:
        for chapter in self.chapters:
            for scene in chapter.get("scenes", []):
                if scene.get("id") == scene_id:
                    return f"{chapter.get('title','')} / {scene.get('title','')}"
        return scene_id

    def _selected_index(self) -> int:
        item = self.list_widget.currentItem()
        if item is None:
            return -1
        data = item.data(Qt.ItemDataRole.UserRole)
        for i, point in enumerate(self._points):
            if point.get("sceneId") == data:
                return i
        return -1

    def _add_point(self) -> None:
        dialog = CharacterArcPointDialog(self, self.chapters, None, self.messages)
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return
        point = dialog.to_point()
        if not point.get("sceneId"):
            return
        self._points.append(point)
        self._refresh()

    def _edit_point(self) -> None:
        index = self._selected_index()
        if index < 0:
            return
        dialog = CharacterArcPointDialog(self, self.chapters, self._points[index], self.messages)
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return
        value = dialog.to_point()
        if value.get("sceneId"):
            self._points[index] = value
        self._refresh()

    def _remove_point(self) -> None:
        index = self._selected_index()
        if index < 0:
            return
        self._points.pop(index)
        self._refresh()

    def points(self) -> list[dict]:
        return [dict(point) for point in self._points]
