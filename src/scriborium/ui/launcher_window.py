from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import QThread, Qt, Signal
from PySide6.QtGui import QIcon, QStandardItemModel
from PySide6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QFileDialog,
    QFrame,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QMainWindow,
    QMessageBox,
    QProgressDialog,
    QPushButton,
    QRadioButton,
    QScrollArea,
    QSpinBox,
    QTabWidget,
    QTextEdit,
    QVBoxLayout,
    QWidget,
)

import logging

import os
import subprocess
import tempfile

from scriborium.core.i18n import I18nService, LANGUAGE_CODE_TO_NAME
from scriborium.shared.signals.event_bus import event_bus
from scriborium.core.import_export import ImportOptions, ImportService
from scriborium.core.project import ProjectDocument, ProjectService
from scriborium.core.recovery import RecoveryService
from scriborium.core.settings import AppSettings, SettingsService
from scriborium.core.templates import TEMPLATE_GROUPS
from scriborium.core import update_checker
from scriborium.core.paths import app_icon_path
from scriborium.ui.editor_window import EditorWindow
from scriborium.ui.help_dialog import HelpDialog
from scriborium.ui.theme_styles import (
    THEME_CLASSIC,
    THEME_STANDARD,
    normalize_theme,
    stylesheet_for_theme,
)


CREATE_TEMPLATE_PRESETS: list[tuple[str, str]] = [
    ("Чистая рукопись", ""),
    ("Роман (шаблон)", "Роман (базовый)"),
    ("Рассказ (шаблон)", "Повесть / Рассказ"),
    ("Сборник стихов", "Сборник стихов"),
    ("Нон-фикшн", "Научно-популярная книга"),
]


class _UpdateCheckThread(QThread):
    """Фоновая проверка наличия обновления."""

    result = Signal(object)  # UpdateInfo | None
    failed = Signal(str)

    def __init__(self, base_url: str) -> None:
        super().__init__()
        self._base_url = base_url

    def run(self) -> None:
        try:
            info, err = update_checker.find_latest(self._base_url)
            if err:
                self.failed.emit(str(err))
            else:
                self.result.emit(info)
        except Exception as exc:  # noqa: BLE001
            self.failed.emit(str(exc))


class _UpdateDownloadThread(QThread):
    """Фоновое скачивание инсталлятора."""

    progress = Signal(int, int)  # done, total
    done = Signal(str)
    failed = Signal(str)

    def __init__(self, url: str, dest: Path) -> None:
        super().__init__()
        self._url = url
        self._dest = dest

    def run(self) -> None:
        try:
            update_checker.download(
                self._url, self._dest, progress_cb=lambda done, total: self.progress.emit(done, total)
            )
            self.done.emit(str(self._dest))
        except Exception as exc:  # noqa: BLE001
            self.failed.emit(str(exc))


class LauncherWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.logger = logging.getLogger("scriborium.launcher")
        self.i18n_service = I18nService()
        self.settings_service = SettingsService()
        self.project_service = ProjectService()
        self.recovery_service = RecoveryService()
        self.import_service = ImportService()
        self.settings = self.settings_service.load()
        self.messages = self.i18n_service.messages(self.settings.language)

        self.tabs: QTabWidget
        self.name_input: QLineEdit
        self.template_combo: QComboBox
        self.folder_input: QLineEdit
        self.recent_list: QListWidget
        self.lang_combo: QComboBox
        self.cb_open_last: QCheckBox
        self.cb_show_launcher: QCheckBox
        self.cb_tray: QCheckBox
        self.theme_standard: QRadioButton
        self.theme_classic: QRadioButton
        self.cb_high_contrast: QCheckBox
        self.settings_projects_folder_input: QLineEdit
        self.backup_folder_input: QLineEdit
        self.autosave_minutes_spin: QSpinBox
        self.cb_backup_on_save: QCheckBox
        self.cb_backup_before_autosave: QCheckBox
        self.max_backups_spin: QSpinBox
        self.cb_delete_old_backups: QCheckBox
        self.delete_old_days_spin: QSpinBox
        self.settings_status: QLabel
        self.create_sidebar_buttons: dict[str, QPushButton] = {}
        self.template_value_to_index: dict[str, int] = {}
        self._active_template_preset = CREATE_TEMPLATE_PRESETS[0][0]

        self.open_editors: list[EditorWindow] = []
        self._setup_ui()
        self._load_settings_to_ui()
        self._apply_theme()
        self._enforce_text_widgets_ltr()
        self._reload_recent_projects()
        self._activate_template_preset(self._active_template_preset)

    def _msg(self, key: str, default: str) -> str:
        return str(self.messages.get(key, default))

    def _refresh_messages(self) -> None:
        self.messages = self.i18n_service.messages(self.settings.language)

    def _setup_ui(self) -> None:
        from scriborium.core.version import APP_VERSION_LABEL

        self.setWindowTitle(self._msg("app_title", "Scriborium {version}").format(version=APP_VERSION_LABEL))
        icon_path = app_icon_path()
        if icon_path.exists():
            self.setWindowIcon(QIcon(str(icon_path)))
        self.resize(1380, 820)
        self.setObjectName("launcherWindow")

        root = QWidget()
        root.setObjectName("launcherRoot")
        root_layout = QVBoxLayout(root)
        root_layout.setContentsMargins(0, 0, 0, 0)
        root_layout.setSpacing(0)

        self.tabs = QTabWidget()
        self.tabs.addTab(self._build_create_tab(), self._msg("tab_create", "Создать"))
        self.tabs.addTab(self._build_open_tab(), self._msg("tab_open", "Открыть"))
        self.tabs.addTab(self._build_settings_tab(), self._msg("tab_settings", "Настройки"))
        update_button = QPushButton(self._msg("update_btn", "Проверить обновления"))
        update_button.setObjectName("classicSecondaryButton")
        update_button.clicked.connect(self._check_for_updates)
        self.update_button = update_button
        help_button = QPushButton(self._msg("btn_help", "Справка"))
        help_button.setObjectName("classicSecondaryButton")
        help_button.clicked.connect(self._open_help)
        corner_widget = QWidget()
        corner_layout = QHBoxLayout(corner_widget)
        corner_layout.setContentsMargins(6, 4, 12, 4)
        corner_layout.setSpacing(6)
        corner_layout.addWidget(update_button)
        corner_layout.addWidget(help_button)
        self.tabs.setCornerWidget(corner_widget, Qt.Corner.TopRightCorner)
        root_layout.addWidget(self.tabs)
        self.setCentralWidget(root)

    def _check_for_updates(self) -> None:
        thread = getattr(self, "_update_check_thread", None)
        if thread is not None and thread.isRunning():
            return
        self.update_button.setEnabled(False)
        self.statusBar().showMessage(self._msg("update_checking", "Checking for updates..."))
        check_thread = _UpdateCheckThread(update_checker.UPDATE_BASE_URL)
        check_thread.result.connect(self._on_update_check_result)
        check_thread.failed.connect(self._on_update_check_failed)
        self._update_check_thread = check_thread
        check_thread.start()

    def _on_update_check_result(self, latest: object) -> None:
        self.update_button.setEnabled(True)
        self.statusBar().clearMessage()
        from scriborium.core.version import APP_VERSION

        if latest is None:
            QMessageBox.information(
                self,
                self._msg("update_title", "Update"),
                self._msg("update_up_to_date", "You already have the latest version."),
            )
            return
        display = str(getattr(latest, "display", "") or ".".join(str(x) for x in latest.version))
        if not update_checker.is_newer(latest.version, update_checker.current_version()):
            QMessageBox.information(
                self,
                self._msg("update_title", "Update"),
                self._msg("update_up_to_date_cur", "Version {version} is installed. You are up to date.").format(
                    version=APP_VERSION
                ),
            )
            return
        answer = QMessageBox.question(
            self,
            self._msg("update_title", "Update"),
            self._msg(
                "update_download_confirm",
                "New version {version} is available (installed: {current}).\n{file}\n\nDownload and install?",
            ).format(
                version=display,
                current=APP_VERSION,
                file=str(getattr(latest, "file_name", "") or ""),
            ),
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            QMessageBox.StandardButton.No,
        )
        if answer != QMessageBox.StandardButton.Yes:
            return
        self._start_update_download(latest)

    def _on_update_check_failed(self, error_text: str) -> None:
        self.update_button.setEnabled(True)
        self.statusBar().clearMessage()
        QMessageBox.warning(
            self,
            self._msg("update_title", "Update"),
            self._msg("update_unreachable", "Could not check for updates:\n{error}").format(error=error_text),
        )

    def _start_update_download(self, latest: object) -> None:
        file_name = str(getattr(latest, "file_name", "") or "update.exe")
        url = str(getattr(latest, "url", "") or update_checker._join_url(update_checker.UPDATE_BASE_URL, file_name))
        dest_dir = Path(tempfile.gettempdir()) / "ScriboriumUpdate"
        dest = dest_dir / file_name
        progress = QProgressDialog(self._msg("update_downloading", "Downloading update..."), "", 0, 100, self)
        progress.setWindowTitle(self._msg("update_title", "Update"))
        progress.setMinimumDuration(0)
        progress.setAutoClose(False)
        progress.setCancelButton(None)
        progress.setValue(0)
        self._progress_dialog = progress
        download_thread = _UpdateDownloadThread(url, dest)
        download_thread.progress.connect(self._on_update_download_progress)
        download_thread.done.connect(self._on_update_download_done)
        download_thread.failed.connect(self._on_update_download_failed)
        self._update_download_thread = download_thread
        download_thread.start()

    def _on_update_download_progress(self, done: int, total: int) -> None:
        progress = getattr(self, "_progress_dialog", None)
        if progress is None:
            return
        if total > 0:
            if progress.maximum() != total:
                progress.setRange(0, total)
            progress.setValue(done)
        else:
            progress.setRange(0, 0)

    def _on_update_download_failed(self, error_text: str) -> None:
        progress = getattr(self, "_progress_dialog", None)
        if progress is not None:
            progress.close()
        QMessageBox.warning(
            self,
            self._msg("update_title", "Update"),
            self._msg("update_failed", "Update download failed:\n{error}").format(error=error_text),
        )

    def _on_update_download_done(self, dest_path: str) -> None:
        progress = getattr(self, "_progress_dialog", None)
        if progress is not None:
            progress.close()
        answer = QMessageBox.question(
            self,
            self._msg("update_title", "Update"),
            self._msg(
                "update_install_confirm",
                "Update downloaded.\n\nInstall now? Please close all open projects first.",
            ),
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            QMessageBox.StandardButton.Yes,
        )
        if answer == QMessageBox.StandardButton.Yes:
            self._launch_installer(dest_path)

    def _launch_installer(self, dest_path: str) -> None:
        try:
            os.startfile(dest_path)  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            subprocess.Popen([dest_path])
        QApplication.quit()

    def _build_classic_shell(
        self,
        *,
        sidebar_title: str,
        sidebar_items: list[tuple[str, bool]],
        footer_text: str = "",
        click_handler=None,
    ) -> tuple[QWidget, QVBoxLayout]:
        root = QWidget()
        root.setObjectName("classicShell")
        shell_layout = QHBoxLayout(root)
        shell_layout.setContentsMargins(0, 0, 0, 0)
        shell_layout.setSpacing(0)

        sidebar = QFrame()
        sidebar.setObjectName("classicSidebar")
        sidebar.setFixedWidth(280)
        sidebar_layout = QVBoxLayout(sidebar)
        sidebar_layout.setContentsMargins(0, 28, 0, 24)
        sidebar_layout.setSpacing(8)

        for text, active in sidebar_items:
            button = QPushButton(text)
            button.setObjectName("classicSidebarItem")
            button.setProperty("active", active)
            button.setFlat(True)
            if click_handler is not None:
                button.clicked.connect(lambda checked=False, value=text: click_handler(value))
                self.create_sidebar_buttons[text] = button
            sidebar_layout.addWidget(button)

        sidebar_layout.addSpacing(20)
        title = QLabel(sidebar_title)
        title.setObjectName("classicSidebarTitle")
        title.setContentsMargins(24, 12, 24, 0)
        sidebar_layout.addWidget(title)
        sidebar_layout.addStretch(1)
        shell_layout.addWidget(sidebar)

        canvas = QFrame()
        canvas.setObjectName("classicCanvas")
        canvas_layout = QVBoxLayout(canvas)
        canvas_layout.setContentsMargins(48, 18, 48, 18)
        canvas_layout.setSpacing(0)
        shell_layout.addWidget(canvas, 1)

        content = QVBoxLayout()
        content.setContentsMargins(0, 0, 0, 0)
        content.setSpacing(8)
        canvas_layout.addLayout(content, 1)

        if footer_text:
            footer = QLabel(footer_text)
            footer.setObjectName("classicFooter")
            footer.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignBottom)
            canvas_layout.addWidget(footer)

        return root, content

    def _build_create_tab(self) -> QWidget:
        footer_text = (
            f"{self._msg('footer_backup_ok', 'Статус бэкапа: OK')}"
            f"   |   {self._msg('footer_sync_cloud', 'Синхронизация: Облако')}"
        )
        root, content = self._build_classic_shell(
            sidebar_title=self._msg("label_recent_projects", "Ваши проекты"),
            sidebar_items=[(label, label == CREATE_TEMPLATE_PRESETS[0][0]) for label, _ in CREATE_TEMPLATE_PRESETS],
            footer_text=footer_text,
            click_handler=self._activate_template_preset,
        )

        brand = QLabel("Scriptorium")
        brand.setObjectName("classicBrand")
        brand.setAlignment(Qt.AlignmentFlag.AlignHCenter)
        content.addWidget(brand)
        content.addSpacing(18)

        body = QHBoxLayout()
        body.setContentsMargins(36, 0, 36, 0)
        body.setSpacing(36)

        left_decor = QLabel("✒")
        left_decor.setObjectName("classicDecorFeather")
        left_decor.setAlignment(Qt.AlignmentFlag.AlignCenter)
        left_decor.setMinimumWidth(180)
        body.addWidget(left_decor, 1)

        form_host = QWidget()
        form_host.setMaximumWidth(620)
        form = QVBoxLayout(form_host)
        form.setContentsMargins(0, 0, 0, 0)
        form.setSpacing(10)

        title = QLabel(self._msg("title_new_project", "Новый проект"))
        title.setObjectName("classicTitle")
        title.setAlignment(Qt.AlignmentFlag.AlignHCenter)
        form.addWidget(title)
        form.addSpacing(22)

        title_label = QLabel(self._msg("label_project_name", "Название проекта"))
        title_label.setObjectName("classicCaption")
        form.addWidget(title_label)

        self.name_input = QLineEdit()
        self.name_input.setObjectName("classicInput")
        self.name_input.setPlaceholderText(self._msg("placeholder_project_name", "Без названия"))
        self._force_lineedit_ltr(self.name_input)
        form.addWidget(self.name_input)

        template_label = QLabel(self._msg("label_template", "Шаблон"))
        template_label.setObjectName("classicCaption")
        form.addWidget(template_label)

        self.template_combo = QComboBox()
        self.template_combo.setObjectName("classicInput")
        self._populate_template_combo()
        form.addWidget(self.template_combo)

        folder_label = QLabel(self._msg("label_project_folder", "Папка для сохранения"))
        folder_label.setObjectName("classicCaption")
        form.addWidget(folder_label)

        folder_row = QHBoxLayout()
        folder_row.setSpacing(10)
        self.folder_input = QLineEdit()
        self.folder_input.setObjectName("classicInput")
        self.folder_input.setPlaceholderText(r"C:\Users\Name\Documents\Scriborium")
        self._force_lineedit_ltr(self.folder_input)
        folder_row.addWidget(self.folder_input, 1)

        browse_button = QPushButton(self._msg("btn_browse", "Обзор..."))
        browse_button.setObjectName("classicBrowseButton")
        browse_button.clicked.connect(self._choose_project_folder)
        folder_row.addWidget(browse_button)
        form.addLayout(folder_row)

        form.addSpacing(28)
        create_button = QPushButton(self._msg("btn_create_project", "Создать проект"))
        create_button.setObjectName("classicPrimaryButton")
        create_button.setMinimumHeight(60)
        create_button.clicked.connect(self._create_project)
        form.addWidget(create_button)
        self.name_input.setToolTip(self._msg("tooltip_project_name", "Введите рабочее название новой книги или проекта."))
        self.template_combo.setToolTip(self._msg("tooltip_template", "Шаблон задаёт стартовую структуру проекта."))
        self.folder_input.setToolTip(self._msg("tooltip_project_folder", "Здесь будет создан файл проекта .scri."))
        create_button.setToolTip(self._msg("tooltip_create_project", "Создать проект и сразу открыть его в редакторе."))
        form.addStretch(1)
        body.addWidget(form_host, 3)

        right_decor = QLabel("🗎")
        right_decor.setObjectName("classicDecorBook")
        right_decor.setAlignment(Qt.AlignmentFlag.AlignCenter)
        right_decor.setMinimumWidth(180)
        body.addWidget(right_decor, 1)

        content.addLayout(body, 1)
        return root

    def _build_open_tab(self) -> QWidget:
        footer_text = (
            f"{self._msg('footer_backup_ok', 'Статус бэкапа: OK')}"
            f"   |   {self._msg('footer_sync_cloud', 'Синхронизация: Облако')}"
        )
        root, content = self._build_classic_shell(
            sidebar_title=self._msg("label_recent_projects", "Ваши проекты"),
            sidebar_items=[
                (self._msg("sidebar_recent_projects", "Недавние проекты"), True),
                (self._msg("sidebar_open_file", "Открыть файл"), False),
                (self._msg("btn_import_docx", "Импорт из .docx"), False),
            ],
            footer_text=footer_text,
        )

        content.addSpacing(34)
        title = QLabel(self._msg("title_recent_projects", "Недавние проекты"))
        title.setObjectName("classicTitle")
        title.setAlignment(Qt.AlignmentFlag.AlignHCenter)
        content.addWidget(title)

        hint = QLabel(self._msg("hint_open_projects", "Откройте недавний проект, выберите .scri вручную или импортируйте материал из другого инструмента."))
        hint.setObjectName("classicCaption")
        hint.setAlignment(Qt.AlignmentFlag.AlignHCenter)
        content.addWidget(hint)
        content.addSpacing(18)

        self.recent_list = QListWidget()
        self.recent_list.setObjectName("classicRecentList")
        self.recent_list.itemClicked.connect(self._open_recent_item_double_click)
        self.recent_list.itemDoubleClicked.connect(self._open_recent_item_double_click)
        self.recent_list.itemActivated.connect(self._open_recent_item_double_click)
        content.addWidget(self.recent_list, 1)

        buttons_row = QHBoxLayout()
        buttons_row.setSpacing(12)

        open_selected_btn = QPushButton(self._msg("btn_open_selected", "Открыть выбранный"))
        open_selected_btn.setObjectName("classicPrimaryButton")
        open_selected_btn.clicked.connect(self._open_selected_recent)
        buttons_row.addWidget(open_selected_btn)

        browse_btn = QPushButton(self._msg("btn_browse_files", "Обзор файлов..."))
        browse_btn.setObjectName("classicSecondaryButton")
        browse_btn.clicked.connect(self._open_project_from_dialog)
        buttons_row.addWidget(browse_btn)
        content.addLayout(buttons_row)

        import_label = QLabel(self._msg("label_import", "Импорт"))
        import_label.setObjectName("classicCaption")
        content.addWidget(import_label)

        import_row = QHBoxLayout()
        import_row.setSpacing(12)
        import_docx = QPushButton(self._msg("btn_import_docx", "Импорт из .docx"))
        import_docx.setObjectName("classicSecondaryButton")
        import_docx.clicked.connect(self._import_docx_from_launcher)
        import_row.addWidget(import_docx)
        content.addLayout(import_row)
        self.recent_list.setToolTip(self._msg("tooltip_recent_projects", "Список последних проектов. Двойной щелчок открывает проект."))
        open_selected_btn.setToolTip(self._msg("tooltip_open_selected", "Открыть выделенный проект из списка."))
        browse_btn.setToolTip(self._msg("tooltip_browse_project", "Выбрать файл проекта .scri вручную."))
        import_docx.setToolTip(self._msg("tooltip_import_docx", "Импортировать документ Word в новый проект Scriborium."))

        return root

    def _build_settings_tab(self) -> QWidget:
        root, content = self._build_classic_shell(
            sidebar_title=self._msg("tab_settings", "Настройки"),
            sidebar_items=[
                (self._msg("sidebar_design", "Оформление"), True),
                (self._msg("sidebar_save_backups", "Сохранение и бэкапы"), False),
                (self._msg("sidebar_language", "Язык"), False),
            ],
        )

        content.addSpacing(24)
        title = QLabel(self._msg("title_settings", "Настройки"))
        title.setObjectName("classicTitle")
        title.setAlignment(Qt.AlignmentFlag.AlignHCenter)
        content.addWidget(title)
        content.addSpacing(16)

        panel = QWidget()
        panel.setObjectName("classicSettingsPanel")
        panel_layout = QVBoxLayout(panel)
        panel_layout.setContentsMargins(28, 24, 28, 24)
        panel_layout.setSpacing(12)

        panel_layout.addWidget(QLabel(self._msg("section_general", "Общие")))
        panel_layout.addWidget(QLabel(self._msg("label_interface_language", "Язык интерфейса")))
        self.lang_combo = QComboBox()
        self.lang_combo.setObjectName("classicInput")
        for code, display_name in LANGUAGE_CODE_TO_NAME.items():
            self.lang_combo.addItem(display_name, code)
        panel_layout.addWidget(self.lang_combo)

        self.cb_open_last = QCheckBox(self._msg("setting_open_last", "Автоматически открывать последний проект при запуске"))
        self.cb_show_launcher = QCheckBox(self._msg("setting_show_launcher", "Показывать окно лаунчера, если нет открытых проектов"))
        self.cb_tray = QCheckBox(self._msg("setting_minimize_tray", "Сворачивать в системный трей при закрытии"))
        panel_layout.addWidget(self.cb_open_last)
        panel_layout.addWidget(self.cb_show_launcher)
        panel_layout.addWidget(self.cb_tray)

        panel_layout.addSpacing(8)
        panel_layout.addWidget(QLabel(self._msg("section_editor", "Редактор")))
        panel_layout.addWidget(QLabel(self._msg("label_theme", "Тема оформления")))
        self.theme_standard = QRadioButton(self._msg("theme_standard_label", "Стандарт"))
        self.theme_classic = QRadioButton("Classic")
        self.cb_high_contrast = QCheckBox(self._msg("setting_high_contrast", "Высокий контраст"))
        panel_layout.addWidget(self.theme_standard)
        panel_layout.addWidget(self.theme_classic)
        panel_layout.addWidget(self.cb_high_contrast)

        panel_layout.addSpacing(8)
        panel_layout.addWidget(QLabel(self._msg("section_save_backups", "Сохранение и резервные копии")))
        panel_layout.addWidget(QLabel(self._msg("label_default_projects_folder", "Папка проектов по умолчанию")))
        projects_folder_row = QHBoxLayout()
        self.settings_projects_folder_input = QLineEdit()
        self.settings_projects_folder_input.setObjectName("classicInput")
        self._force_lineedit_ltr(self.settings_projects_folder_input)
        projects_browse_button = QPushButton(self._msg("btn_browse", "Обзор..."))
        projects_browse_button.setObjectName("classicSecondaryButton")
        projects_browse_button.clicked.connect(self._choose_default_projects_folder)
        projects_folder_row.addWidget(self.settings_projects_folder_input, 1)
        projects_folder_row.addWidget(projects_browse_button)
        panel_layout.addLayout(projects_folder_row)

        panel_layout.addWidget(QLabel(self._msg("label_backup_folder", "Папка резервных копий")))
        backup_folder_row = QHBoxLayout()
        self.backup_folder_input = QLineEdit()
        self.backup_folder_input.setObjectName("classicInput")
        self._force_lineedit_ltr(self.backup_folder_input)
        backup_browse_button = QPushButton(self._msg("btn_browse", "Обзор..."))
        backup_browse_button.setObjectName("classicSecondaryButton")
        backup_browse_button.clicked.connect(self._choose_backup_folder)
        backup_folder_row.addWidget(self.backup_folder_input, 1)
        backup_folder_row.addWidget(backup_browse_button)
        panel_layout.addLayout(backup_folder_row)

        panel_layout.addWidget(QLabel(self._msg("label_autosave_every", "Автосохранение каждые (минуты)")))
        self.autosave_minutes_spin = QSpinBox()
        self.autosave_minutes_spin.setRange(1, 120)
        panel_layout.addWidget(self.autosave_minutes_spin)

        self.cb_backup_on_save = QCheckBox(self._msg("setting_backup_on_save", "Создавать резервную копию при каждом сохранении"))
        self.cb_backup_before_autosave = QCheckBox(self._msg("setting_backup_before_autosave", "Создавать бэкап перед автоматическим сохранением"))
        panel_layout.addWidget(self.cb_backup_on_save)
        panel_layout.addWidget(self.cb_backup_before_autosave)

        panel_layout.addWidget(QLabel(self._msg("label_max_backups", "Максимальное количество бэкапов на проект")))
        self.max_backups_spin = QSpinBox()
        self.max_backups_spin.setRange(1, 200)
        panel_layout.addWidget(self.max_backups_spin)

        self.cb_delete_old_backups = QCheckBox(self._msg("setting_delete_old_backups", "Удалять бэкапы старше N дней"))
        panel_layout.addWidget(self.cb_delete_old_backups)
        self.delete_old_days_spin = QSpinBox()
        self.delete_old_days_spin.setRange(1, 3650)
        panel_layout.addWidget(self.delete_old_days_spin)


        self.settings_status = QLabel(self._msg("status_settings_unsaved", "Строка состояния: Настройки не сохранены"))
        panel_layout.addWidget(self.settings_status)

        save_button = QPushButton(self._msg("btn_save_settings", "Сохранить настройки"))
        save_button.setObjectName("classicPrimaryButton")
        save_button.clicked.connect(self._save_settings)
        panel_layout.addWidget(save_button)

        reset_button = QPushButton(self._msg("btn_reset_defaults", "Сбросить по умолчанию"))
        reset_button.setObjectName("classicSecondaryButton")
        reset_button.clicked.connect(self._reset_settings)
        panel_layout.addWidget(reset_button)
        self.lang_combo.setToolTip(self._msg("tooltip_language", "Выберите язык интерфейса приложения."))
        save_button.setToolTip(self._msg("tooltip_save_settings", "Сохранить настройки и применить изменения."))
        reset_button.setToolTip(self._msg("tooltip_reset_settings", "Вернуть настройки к значениям по умолчанию."))
        panel_layout.addStretch(1)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.Shape.NoFrame)
        scroll.setWidget(panel)
        content.addWidget(scroll, 1)
        return root

    def _populate_template_combo(self) -> None:
        self.template_value_to_index.clear()
        self.template_combo.clear()
        self.template_combo.addItem("Без шаблона", "")
        self.template_value_to_index[""] = 0

        for group, items in TEMPLATE_GROUPS.items():
            self.template_combo.addItem(f"--- {group} ---", None)
            header_index = self.template_combo.count() - 1
            model = self.template_combo.model()
            if isinstance(model, QStandardItemModel):
                model_item = model.item(header_index)
                if model_item is not None:
                    model_item.setEnabled(False)
            for template in items:
                self.template_combo.addItem(template, template)
                self.template_value_to_index[template] = self.template_combo.count() - 1

    def _force_lineedit_ltr(self, widget: QLineEdit) -> None:
        widget.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        widget.setAlignment(Qt.AlignmentFlag.AlignLeft)
        widget.setCursorMoveStyle(Qt.CursorMoveStyle.LogicalMoveStyle)

    def _enforce_text_widgets_ltr(self) -> None:
        for line_edit in self.findChildren(QLineEdit):
            self._force_lineedit_ltr(line_edit)
        for text_edit in self.findChildren(QTextEdit):
            text_edit.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
            text_edit.setCursorMoveStyle(Qt.CursorMoveStyle.LogicalMoveStyle)
            option = text_edit.document().defaultTextOption()
            option.setTextDirection(Qt.LayoutDirection.LeftToRight)
            option.setAlignment(Qt.AlignmentFlag.AlignLeft)
            text_edit.document().setDefaultTextOption(option)

    def _activate_template_preset(self, preset_label: str) -> None:
        self._active_template_preset = preset_label
        for label, button in self.create_sidebar_buttons.items():
            active = label == preset_label
            button.setProperty("active", active)
            button.style().unpolish(button)
            button.style().polish(button)
            button.update()

        target_template = dict(CREATE_TEMPLATE_PRESETS).get(preset_label, "")
        target_index = self.template_value_to_index.get(target_template, 0)
        self.template_combo.setCurrentIndex(target_index)

    def _load_settings_to_ui(self) -> None:
        self.folder_input.setText(self.settings.default_projects_dir)
        self.settings_projects_folder_input.setText(self.settings.default_projects_dir)

        lang_code = self.i18n_service.resolve_code(self.settings.language)
        idx = self.lang_combo.findData(lang_code)
        if idx >= 0:
            self.lang_combo.setCurrentIndex(idx)

        self.cb_open_last.setChecked(self.settings.open_last_project_on_startup)
        self.cb_show_launcher.setChecked(self.settings.show_launcher_when_no_projects)
        self.cb_tray.setChecked(self.settings.minimize_to_tray_on_close)
        self.backup_folder_input.setText(self.settings.backup_dir)
        self.autosave_minutes_spin.setValue(self.settings.autosave_minutes)
        self.cb_backup_on_save.setChecked(self.settings.create_backup_on_save)
        self.cb_backup_before_autosave.setChecked(self.settings.create_backup_before_autosave)
        self.max_backups_spin.setValue(self.settings.max_backups_per_project)
        self.cb_delete_old_backups.setChecked(self.settings.delete_old_backups)
        self.delete_old_days_spin.setValue(self.settings.delete_backups_older_than_days)

        normalized = normalize_theme(self.settings.theme)
        self.theme_standard.setChecked(normalized == THEME_STANDARD)
        self.theme_classic.setChecked(normalized == THEME_CLASSIC)
        self.cb_high_contrast.setChecked(bool(self.settings.high_contrast))

    def _reload_recent_projects(self) -> None:
        self.recent_list.clear()
        valid_recent: list[str] = []
        for recent in self.settings.recent_projects:
            path = Path(recent)
            if not path.is_absolute():
                path = (Path.cwd() / path).resolve()
            if not path.exists():
                continue
            valid_recent.append(str(path))
            item = QListWidgetItem(path.name)
            item.setToolTip(str(path))
            item.setData(Qt.ItemDataRole.UserRole, str(path))
            self.recent_list.addItem(item)
        if valid_recent != self.settings.recent_projects:
            self.settings.recent_projects = valid_recent
            self.settings_service.save(self.settings)
        if self.recent_list.count() > 0:
            self.recent_list.setCurrentRow(0)

    def _open_help(self) -> None:
        HelpDialog(self, self.settings.language).exec()

    def _choose_project_folder(self) -> None:
        folder = QFileDialog.getExistingDirectory(
            self,
            self._msg("dialog_select_project_folder", "Выберите папку для проектов"),
            self.folder_input.text() or self.settings.default_projects_dir,
        )
        if folder:
            self.folder_input.setText(folder)

    def _current_template_value(self) -> str:
        value = self.template_combo.currentData()
        return value if isinstance(value, str) else ""

    def _create_project(self) -> None:
        project_name = self.name_input.text().strip()
        if not project_name:
            QMessageBox.warning(self, self._msg("msg_error", "Ошибка"), self._msg("msg_enter_project_name", "Введите название проекта."))
            return

        folder_text = self.folder_input.text().strip()
        if not folder_text:
            QMessageBox.warning(self, self._msg("msg_error", "Ошибка"), self._msg("msg_set_project_folder", "Укажите папку для сохранения."))
            return

        file_path = Path(folder_text) / f"{project_name}.scri"
        if file_path.exists():
            QMessageBox.warning(self, self._msg("msg_error", "Ошибка"), f"{self._msg('msg_file_exists', 'Файл уже существует:')}\n{file_path}")
            return

        self.logger.info("Creating project: %s", file_path)
        try:
            self.project_service.create_new(
                path=file_path,
                name=project_name,
                template=self._current_template_value(),
            )
            self.logger.info("Project created successfully: %s", file_path)
        except OSError as err:
            self.logger.exception("Failed to create project at %s", file_path)
            QMessageBox.critical(self, self._msg("msg_error", "Ошибка"), f"{self._msg('msg_create_failed', 'Не удалось создать проект:')}\n{err}")
            return

        self._finalize_created_project(file_path)

    def _choose_default_projects_folder(self) -> None:
        folder = QFileDialog.getExistingDirectory(
            self,
            self._msg("dialog_select_project_folder", "Выберите папку для проектов"),
            self.settings_projects_folder_input.text() or self.settings.default_projects_dir,
        )
        if folder:
            self.settings_projects_folder_input.setText(folder)

    def _choose_backup_folder(self) -> None:
        folder = QFileDialog.getExistingDirectory(
            self,
            self._msg("dialog_select_backup_folder", "Выберите папку для резервных копий"),
            self.backup_folder_input.text() or self.settings.backup_dir,
        )
        if folder:
            self.backup_folder_input.setText(folder)

    def _open_selected_recent(self) -> None:
        item = None
        selected_items = self.recent_list.selectedItems()
        if selected_items:
            item = selected_items[0]
        if item is None:
            item = self.recent_list.currentItem()
        if item is None and self.recent_list.count() > 0:
            row = self.recent_list.currentRow()
            if row < 0:
                row = 0
                self.recent_list.setCurrentRow(row)
            item = self.recent_list.item(row)
        if item is None:
            QMessageBox.information(self, self._msg("msg_info_open", "Открытие"), self._msg("msg_select_project_in_list", "Выберите проект в списке."))
            return
        path_value = item.data(Qt.ItemDataRole.UserRole)
        if not path_value:
            QMessageBox.warning(self, self._msg("msg_error", "Ошибка"), self._msg("msg_missing_project_path", "У выбранного проекта не найден путь."))
            return
        self._open_project(Path(path_value))

    def _open_recent_item_double_click(self, item: QListWidgetItem) -> None:
        path = item.data(Qt.ItemDataRole.UserRole)
        if path:
            self._open_project(Path(path))

    def _on_editor_closed(self, *_args) -> None:
        alive: list[EditorWindow] = []
        for editor in self.open_editors:
            try:
                if editor.isVisible():
                    alive.append(editor)
            except RuntimeError:
                continue
        self.open_editors = alive
        if not self.open_editors and self.settings.show_launcher_when_no_projects:
            self.show()
            self.raise_()
            self.activateWindow()

    def _open_project_from_dialog(self) -> None:
        path_str, _ = QFileDialog.getOpenFileName(
            self,
            self._msg("dialog_open_project", "Открыть проект"),
            self.settings.default_projects_dir,
            self._msg("dialog_filter_scri", "Scriborium Project (*.scri)"),
        )
        if path_str:
            self._open_project(Path(path_str))

    def _import_docx_from_launcher(self) -> None:
        source_str, _ = QFileDialog.getOpenFileName(
            self,
            self._msg("btn_import_docx", "Импорт из .docx"),
            self.settings.default_projects_dir,
            "Word Document (*.docx)",
        )
        if not source_str:
            return
        self._import_external_source(Path(source_str), self._msg("btn_import_docx", "Импорт из .docx"))

    def _import_external_source(self, source: Path, import_label: str) -> None:
        try:
            result = self.import_service.import_file(
                source,
                ImportOptions(split_mode="headings", create_entity_cards=True),
            )
        except Exception as err:
            QMessageBox.critical(self, self._msg("msg_import_error", "Ошибка импорта"), str(err))
            return

        default_name = result.content.get("book", {}).get("title") or result.source.stem
        target_default = Path(self.folder_input.text().strip() or self.settings.default_projects_dir) / f"{default_name}.scri"
        target_str, _ = QFileDialog.getSaveFileName(
            self,
            self._msg("dialog_save_imported_project", "Сохранить импортированный проект"),
            str(target_default),
            self._msg("dialog_filter_scri", "Scriborium Project (*.scri)"),
        )
        if not target_str:
            return

        target_path = Path(target_str)
        if target_path.suffix.lower() != ".scri":
            target_path = target_path.with_suffix(".scri")
        if target_path.exists():
            QMessageBox.warning(self, self._msg("msg_error", "Ошибка"), f"{self._msg('msg_file_exists', 'Файл уже существует:')}\n{target_path}")
            return

        document = self._create_document_from_import(target_path, result.content, import_label)
        self.folder_input.setText(str(target_path.parent))
        self.settings_status.setText(f"{import_label}: {self._msg('status_import_completed', 'завершён')}")
        self._finalize_created_project(document.path)

    def _create_document_from_import(self, path: Path, content: dict, template_label: str) -> ProjectDocument:
        project_name = content.get("book", {}).get("title") or path.stem
        document = self.project_service.create_new(path=path, name=project_name, template=template_label)
        document.content = content
        document.manifest["name"] = project_name
        document.manifest["template"] = template_label
        self.project_service.save_document(document)
        return document

    def _finalize_created_project(self, file_path: Path) -> None:
        self.settings = self.settings_service.add_recent_project(file_path)
        self.settings.default_projects_dir = str(file_path.parent)
        self.settings_service.save(self.settings)
        self.settings_projects_folder_input.setText(str(file_path.parent))
        self._reload_recent_projects()
        self._open_project(file_path)

    def _open_project(self, path: Path) -> None:
        self.logger.info("Opening project: %s", path)
        if not path.exists():
            self.logger.error("Project file not found: %s", path)
            QMessageBox.warning(self, self._msg("msg_error", "Ошибка"), f"{self._msg('msg_file_not_found', 'Файл не найден:')}\n{path}")
            return

        recovery_state = self.recovery_service.get_state(path)
        open_attempts: list[Path] = []
        if (
            recovery_state is not None
            and recovery_state.dirty
            and recovery_state.autosave_snapshot
            and recovery_state.autosave_snapshot.exists()
        ):
            open_attempts.append(recovery_state.autosave_snapshot)
        open_attempts.append(path)

        document: ProjectDocument | None = None
        used_source: Path | None = None
        last_error: Exception | None = None
        for source in open_attempts:
            try:
                document = self.project_service.open_document(source)
                used_source = source
                break
            except Exception as err:
                last_error = err
                self.logger.warning("Failed to open from %s: %s", source, err)
                continue

        if document is None:
            err_text = str(last_error) if last_error is not None else self._msg("msg_unknown_error", "Unknown error")
            self.logger.error("Failed to open project %s: %s", path, err_text)
            QMessageBox.critical(self, self._msg("msg_error", "Ошибка"), f"{self._msg('msg_open_failed', 'Не удалось открыть проект:')}\n{err_text}")
            return

        if used_source is not None and used_source != path:
            document.path = path
        if used_source == path and recovery_state is not None and recovery_state.dirty:
            self.recovery_service.mark_dirty(path, False)

        self.settings = self.settings_service.add_recent_project(path)
        self._reload_recent_projects()
        self.logger.info("Opening EditorWindow for %s", path)
        try:
            editor = EditorWindow(self.project_service, self.settings_service, document)
        except Exception as exc:
            self.logger.exception("EditorWindow failed to initialize for %s: %s", path, exc)
            QMessageBox.critical(
                self,
                self._msg("msg_error", "Ошибка"),
                f"Failed to open editor:\n{exc}\n\nCheck the log file for details.",
            )
            return
        editor.setGeometry(self.geometry())
        editor.show()
        editor.raise_()
        editor.activateWindow()
        self.open_editors.append(editor)
        editor.destroyed.connect(self._on_editor_closed)
        self.hide()
        self.logger.info("Project opened successfully: %s", path)

    def _save_settings(self) -> None:
        theme = THEME_CLASSIC if self.theme_classic.isChecked() else THEME_STANDARD
        new_lang_code = self.lang_combo.currentData()
        current_lang_code = self.i18n_service.resolve_code(self.settings.language)

        self.settings.language = str(new_lang_code)
        self.settings.open_last_project_on_startup = self.cb_open_last.isChecked()
        self.settings.show_launcher_when_no_projects = self.cb_show_launcher.isChecked()
        self.settings.minimize_to_tray_on_close = self.cb_tray.isChecked()
        self.settings.theme = normalize_theme(theme)
        self.settings.high_contrast = self.cb_high_contrast.isChecked()
        self.settings.default_projects_dir = (
            self.settings_projects_folder_input.text().strip()
            or str(Path.home() / "Documents" / "Scriborium")
        )
        self.folder_input.setText(self.settings.default_projects_dir)
        self.settings.backup_dir = (
            self.backup_folder_input.text().strip()
            or str(Path.home() / "Documents" / "Scriborium" / "Backups")
        )
        self.settings.autosave_minutes = self.autosave_minutes_spin.value()
        self.settings.create_backup_on_save = self.cb_backup_on_save.isChecked()
        self.settings.create_backup_before_autosave = self.cb_backup_before_autosave.isChecked()
        self.settings.max_backups_per_project = self.max_backups_spin.value()
        self.settings.delete_old_backups = self.cb_delete_old_backups.isChecked()
        self.settings.delete_backups_older_than_days = self.delete_old_days_spin.value()
        self.settings_service.save(self.settings)
        self._refresh_messages()
        if current_lang_code != str(new_lang_code):
            event_bus.emit("language:changed", {"language": str(new_lang_code)})
            self._rebuild_ui_after_language_change()
        self._apply_theme()
        self.settings_status.setText(self._msg("status_settings_saved", "Строка состояния: Настройки сохранены"))

    def _reset_settings(self) -> None:
        recent = list(self.settings.recent_projects)
        self.settings = AppSettings(recent_projects=recent)
        self.settings_service.save(self.settings)
        self._refresh_messages()
        self._rebuild_ui_after_language_change()
        self._apply_theme()
        self.settings_status.setText(self._msg("status_settings_reset", "Строка состояния: Сброшено к умолчанию"))

    def _rebuild_ui_after_language_change(self) -> None:
        old_tabs_index = self.tabs.currentIndex()
        current_geometry = self.geometry()
        central = self.centralWidget()
        if central is not None:
            central.deleteLater()
        self.create_sidebar_buttons.clear()
        self._refresh_messages()
        self._setup_ui()
        self.setGeometry(current_geometry)
        self._load_settings_to_ui()
        self._reload_recent_projects()
        self.tabs.setCurrentIndex(old_tabs_index)
        self._apply_theme()
        self._activate_template_preset(self._active_template_preset)

    def _apply_theme(self) -> None:
        self.setStyleSheet(
            stylesheet_for_theme(
                normalize_theme(self.settings.theme),
                bool(self.settings.high_contrast),
            )
        )
