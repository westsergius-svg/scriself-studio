from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QFileDialog,
    QFormLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMainWindow,
    QMessageBox,
    QPushButton,
    QTabWidget,
    QVBoxLayout,
    QWidget,
)


class LauncherWindow(QMainWindow):
    def __init__(self, context) -> None:
        super().__init__()
        self._context = context

        self.setWindowTitle("Scriborium beta 1.2.0 — Лаунчер")
        self.resize(820, 520)

        tabs = QTabWidget()
        tabs.addTab(self._build_projects_tab(), "Проекты")
        tabs.addTab(self._build_settings_tab(), "Настройки")
        self.setCentralWidget(tabs)

    def _build_projects_tab(self) -> QWidget:
        root = QWidget()
        layout = QVBoxLayout(root)
        layout.setContentsMargins(28, 24, 28, 24)
        layout.setSpacing(18)

        title = QLabel("Проекты")
        title.setStyleSheet("font-size: 26px; font-weight: 700;")
        subtitle = QLabel("Создайте новый проект или откройте существующий .scri. Проект откроется сразу в редакторе глав.")
        subtitle.setWordWrap(True)

        form = QFormLayout()
        self._title_input = QLineEdit("Новый роман")
        self._folder_input = QLineEdit(str(Path.home() / "Documents" / "Scriborium"))
        browse_btn = QPushButton("...")
        browse_btn.setFixedWidth(42)
        browse_btn.clicked.connect(self._choose_folder)

        folder_row = QWidget()
        folder_layout = QHBoxLayout(folder_row)
        folder_layout.setContentsMargins(0, 0, 0, 0)
        folder_layout.addWidget(self._folder_input)
        folder_layout.addWidget(browse_btn)

        form.addRow("Название", self._title_input)
        form.addRow("Папка", folder_row)

        create_btn = QPushButton("Создать проект")
        create_btn.setMinimumHeight(40)
        create_btn.clicked.connect(self._create_project)

        open_btn = QPushButton("Открыть .scri")
        open_btn.setMinimumHeight(40)
        open_btn.clicked.connect(self._open_project_file)

        buttons = QWidget()
        buttons_layout = QHBoxLayout(buttons)
        buttons_layout.setContentsMargins(0, 0, 0, 0)
        buttons_layout.addWidget(create_btn)
        buttons_layout.addWidget(open_btn)

        layout.addWidget(title)
        layout.addWidget(subtitle)
        layout.addLayout(form)
        layout.addWidget(buttons)
        layout.addStretch(1)
        return root

    def _build_settings_tab(self) -> QWidget:
        root = QWidget()
        layout = QVBoxLayout(root)
        layout.setContentsMargins(28, 24, 28, 24)
        title = QLabel("Настройки")
        title.setStyleSheet("font-size: 26px; font-weight: 700;")
        info = QLabel("Beta 1.2.0: базовая модульная сборка. Обновления и расширенные настройки будут подключены отдельными системными модулями.")
        info.setWordWrap(True)
        layout.addWidget(title)
        layout.addWidget(info)
        layout.addStretch(1)
        return root

    def _choose_folder(self) -> None:
        folder = QFileDialog.getExistingDirectory(self, "Папка проекта", self._folder_input.text())
        if folder:
            self._folder_input.setText(folder)

    def _create_project(self) -> None:
        try:
            project = self._context.project_service.create_project(self._title_input.text(), self._folder_input.text())
        except Exception as exc:
            QMessageBox.critical(self, "Ошибка создания", str(exc))
            return
        self._open_editor(project)

    def _open_project_file(self) -> None:
        path, _ = QFileDialog.getOpenFileName(self, "Открыть проект", str(Path.home()), "Scriborium (*.scri)")
        if not path:
            return
        try:
            project = self._context.project_service.open_project(path)
        except Exception as exc:
            QMessageBox.critical(self, "Ошибка открытия", str(exc))
            return
        self._open_editor(project)

    def _open_editor(self, project) -> None:
        editor = self._context.module_loader.get_module("editor")
        editor.open_project(project)
        self.hide()
