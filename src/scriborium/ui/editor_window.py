from __future__ import annotations

from pathlib import Path
import html

from PySide6.QtCore import QTimer, Qt, Signal
from PySide6.QtGui import QAction, QIcon, QKeySequence, QTextCursor
from PySide6.QtWidgets import (
    QAbstractItemView,
    QApplication,
    QCheckBox,
    QFileDialog,
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QMainWindow,
    QMessageBox,
    QMenu,
    QInputDialog,
    QPlainTextEdit,
    QPushButton,
    QColorDialog,
    QSpinBox,
    QSplitter,
    QTreeWidget,
    QTreeWidgetItem,
    QVBoxLayout,
    QWidget,
    QTextEdit,
)

from scriborium.core.import_export import ExportService, ImportOptions, ImportService
from scriborium.core.i18n import I18nService
from scriborium.core.backup import BackupService
from scriborium.core.paths import app_icon_path
from scriborium.core.project import ProjectDocument, ProjectService
from scriborium.core.recovery import RecoveryService
from scriborium.core.spellcheck import SpellcheckService
from scriborium.core.scene_fields import (
    SCENE_STATUS_LABEL_RU,
    SCENE_STATUS_ORDER,
    default_scene_dict,
    normalize_scene_status,
)
from scriborium.core.scene_move import apply_scene_move
from scriborium.core.search import SearchService
from scriborium.core.settings import SettingsService
from scriborium.core.chapter_text import html_to_plain_chapter
import logging

from scriborium.core.version import APP_VERSION_LABEL
from scriborium.ui.common import (
    ENTITY_KEYS,
    _chapter_text_html_for_editor,
    count_chapter_free_words,
    _enforce_ltr,
)
from scriborium.ui.dialogs import (
    CharacterArcEditorDialog,
    EntityEditDialog,
    GlobalSearchDialog,
    LibraryManagerDialog,
    SceneLinkDialog,
)
from scriborium.modules.chapter_volume.widget import ChapterVolumeWidget
from scriborium.shared.signals.event_bus import event_bus
from scriborium.ui.export_dialog import ExportDialog
from scriborium.ui.help_dialog import HelpDialog
from scriborium.ui.chapter_text_editor import ChapterTextEditorDialog
from scriborium.ui.spell_highlighter import LiveSpellHighlighter
from scriborium.ui.theme_styles import (
    THEME_CLASSIC,
    THEME_STANDARD,
    normalize_theme,
    stylesheet_for_theme,
)
from scriborium.ui.timeline_widget import TimelineWidget


ENTITY_LABEL_KEYS = {
    "characters": "label_characters",
    "locations": "label_locations",
    "objects": "label_objects",
}

CHAPTER_STATUS_ORDER = ("plan", "in_progress", "done")
CHAPTER_STATUS_FALLBACK: dict[str, str] = {
    "plan": "Plan",
    "in_progress": "In Progress",
    "done": "Done",
}

ENTITY_ROLE_LABEL_KEYS = {
    "characters": "entity_role_character",
    "locations": "entity_role_location",
    "objects": "entity_role_object",
}
ENTITY_ROLE_LABEL_DEFAULTS = {
    "characters": "Role:",
    "locations": "Location Type:",
    "objects": "Object Type:",
}
ENTITY_FIELD2_LABEL_KEYS = {
    "characters": "entity_field2_character",
    "locations": "entity_field2_location",
    "objects": "entity_field2_object",
}
ENTITY_FIELD2_LABEL_DEFAULTS = {
    "characters": "Age / Stage:",
    "locations": "Era / Time:",
    "objects": "State:",
}
ENTITY_FIELD3_LABEL_KEYS = {
    "characters": "entity_field3_character",
    "locations": "entity_field3_location",
    "objects": "entity_field3_object",
}
ENTITY_FIELD3_LABEL_DEFAULTS = {
    "characters": "Motivation:",
    "locations": "Atmosphere:",
    "objects": "Purpose:",
}


class StoryTreeWidget(QTreeWidget):
    structure_reordered = Signal()

    def dropEvent(self, event) -> None:  # type: ignore[override]
        super().dropEvent(event)
        self.structure_reordered.emit()


class EditorWindow(QMainWindow):
    def __init__(
        self,
        project_service: ProjectService,
        settings_service: SettingsService,
        document: ProjectDocument,
    ) -> None:
        super().__init__()
        self.logger = logging.getLogger("scriborium.editor")
        self.project_service = project_service
        self.settings_service = settings_service
        self.document = document
        self.i18n_service = I18nService()
        self.messages = self.i18n_service.messages(self.settings_service.load().language)
        self.backup_service = BackupService()
        self.recovery_service = RecoveryService()
        self.search_service = SearchService()
        self.import_service = ImportService()
        self.export_service = ExportService()
        self.spellcheck_service = SpellcheckService()
        self.dirty = False
        self.logger.info("EditorWindow init start for %s", document.path)

        self.tree = StoryTreeWidget()
        self.editor = QPlainTextEdit()
        self.meta_label = QLabel()
        self._plotline_label = QLabel(self._msg("label_plotline_single", "Plotline:"))
        self.plotline_combo = QComboBox()
        self._plotline_label.setVisible(False)
        self.plotline_combo.setVisible(False)
        self.plotline_combo.setMinimumWidth(220)
        self._plotline_combo_blocked = False
        self._scene_fields_blocked = False
        self.scene_status_combo = QComboBox()
        for sk in SCENE_STATUS_ORDER:
            self.scene_status_combo.addItem(self._scene_status_label(sk), sk)
        self.scene_target_spin = QSpinBox()
        self.scene_target_spin.setRange(0, 500_000)
        self.scene_title_input = QLineEdit()
        self.scene_datetime_input = QLineEdit()
        self.scene_goal_input = QLineEdit()
        self.scene_conflict_input = QLineEdit()
        self.scene_result_input = QLineEdit()
        self.scene_char_btn = QPushButton(self._msg("button_characters", "Characters..."))
        self.scene_loc_btn = QPushButton(self._msg("button_locations", "Locations..."))
        self.scene_obj_btn = QPushButton(self._msg("button_objects", "Objects..."))
        self.scene_plotlines_btn = QPushButton(self._msg("button_plotlines", "Plotlines..."))
        self.scene_links_label = QLabel("")
        self._scene_extra = QWidget()
        self._scene_form_blocked = False

        # Редактор главы (по ТЗ)
        self._chapter_fields_blocked = False
        self.chapter_title_input = QLineEdit()
        self.chapter_number_spin = QSpinBox()
        self.chapter_number_spin.setRange(0, 10_000)
        self.chapter_subtitle_input = QLineEdit()
        self.chapter_epigraph_input = QPlainTextEdit()
        self.chapter_epigraph_input.setMaximumHeight(110)
        self.chapter_synopsis_input = QPlainTextEdit()
        self.chapter_synopsis_input.setMaximumHeight(170)
        self.chapter_notes_input = QPlainTextEdit()
        self.chapter_notes_input.setMaximumHeight(170)
        self.chapter_status_combo = QComboBox()
        for sk in CHAPTER_STATUS_ORDER:
            self.chapter_status_combo.addItem(self._chapter_status_label(sk), sk)
        self.chapter_target_spin = QSpinBox()
        self.chapter_target_spin.setRange(0, 2_000_000)

        self._editor_stack_scene = QWidget()
        self._editor_stack_chapter = QWidget()
        self._editor_stack_entity = QWidget()
        self._editor_stack_book = QWidget()

        # Редактор книги
        self._book_fields_blocked = False
        self.book_title_input = QLineEdit()
        self.book_subtitle_input = QLineEdit()
        self.book_author_input = QLineEdit()
        self.book_genre_input = QLineEdit()
        self.book_format_input = QLineEdit()
        self.book_tone_input = QLineEdit()
        self.book_audience_input = QLineEdit()
        self.book_preface_input = QPlainTextEdit()
        self.book_synopsis_input = QPlainTextEdit()
        _enforce_ltr(self.scene_title_input)
        _enforce_ltr(self.scene_datetime_input)
        _enforce_ltr(self.scene_goal_input)
        _enforce_ltr(self.scene_conflict_input)
        _enforce_ltr(self.scene_result_input)
        _enforce_ltr(self.chapter_title_input)
        _enforce_ltr(self.chapter_subtitle_input)
        _enforce_ltr(self.book_title_input)
        _enforce_ltr(self.book_subtitle_input)
        _enforce_ltr(self.book_author_input)
        _enforce_ltr(self.book_genre_input)
        _enforce_ltr(self.book_format_input)
        _enforce_ltr(self.book_tone_input)
        _enforce_ltr(self.book_audience_input)
        _enforce_ltr(self.book_preface_input)
        _enforce_ltr(self.book_synopsis_input)
        _enforce_ltr(self.chapter_epigraph_input)
        _enforce_ltr(self.chapter_synopsis_input)
        _enforce_ltr(self.chapter_notes_input)
        _enforce_ltr(self.editor)

        # Редактор сущности (персонаж/локация/объект)
        self._entity_fields_blocked = False
        self.entity_name_input = QLineEdit()
        self.entity_role_input = QLineEdit()
        self.entity_field2_input = QLineEdit()
        self.entity_field3_input = QLineEdit()
        self.entity_arc_summary_input = QPlainTextEdit()
        self.entity_arc_summary_input.setMaximumHeight(90)
        self.entity_arc_points_btn = QPushButton(self._msg("button_arc_points", "Arc Points..."))
        self.entity_short_input = QLineEdit()
        self.entity_description_input = QPlainTextEdit()
        self.entity_notes_input = QPlainTextEdit()
        _enforce_ltr(self.entity_name_input)
        _enforce_ltr(self.entity_role_input)
        _enforce_ltr(self.entity_field2_input)
        _enforce_ltr(self.entity_field3_input)
        _enforce_ltr(self.entity_short_input)
        _enforce_ltr(self.entity_arc_summary_input)
        _enforce_ltr(self.entity_description_input)
        _enforce_ltr(self.entity_notes_input)
        self.timeline = TimelineWidget()
        self._live_spell_editor = LiveSpellHighlighter(
            self.editor.document(),
            language_getter=lambda: self.settings_service.load().language,
            spellcheck_service=self.spellcheck_service,
        )
        self._timeline_word_timer = QTimer(self)
        self._timeline_word_timer.setSingleShot(True)
        self._timeline_word_timer.setInterval(400)
        self._timeline_word_timer.timeout.connect(self._refresh_timeline_word_counts)
        self.status = self.statusBar()
        self.autosave_timer = QTimer(self)

        # New modules from predlog.md
        self.chapter_volume_widget = ChapterVolumeWidget()
        event_bus.subscribe("chapter_volume:clicked", self._on_chapter_volume_clicked)
        event_bus.subscribe("language:changed", self._on_language_changed)

        try:
            self._ensure_base_structure()
            self._setup_ui()
            self._enforce_text_widgets_ltr()
            self._load_from_document()
            self._setup_autosave()
            self._apply_spellcheck_settings()
            self.recovery_service.start_session(self.document.path)
            self.logger.info("EditorWindow init complete for %s", document.path)
        except Exception as exc:
            self.logger.exception("EditorWindow init failed for %s: %s", document.path, exc)
            raise

    def _msg(self, key: str, default: str) -> str:
        return str(self.messages.get(key, default))

    def _entity_label(self, kind: str) -> str:
        defaults = {
            "characters": "Characters",
            "locations": "Locations",
            "objects": "Objects",
        }
        return self._msg(ENTITY_LABEL_KEYS[kind], defaults[kind])

    def _scene_status_label(self, status_key: str) -> str:
        return self._msg(f"scene_status_{status_key}", SCENE_STATUS_LABEL_RU[status_key])

    def _chapter_status_label(self, status_key: str) -> str:
        return self._msg(f"chapter_status_{status_key}", CHAPTER_STATUS_FALLBACK[status_key])

    def _default_scene_title(self, number: int) -> str:
        return self._msg("default_scene_title", "Scene {number}").format(number=number)

    def _default_chapter_title(self, number: int) -> str:
        return self._msg("default_chapter_title", "Chapter {number}").format(number=number)

    def _enforce_text_widgets_ltr(self) -> None:
        for line_edit in self.findChildren(QLineEdit):
            _enforce_ltr(line_edit)
        for plain_edit in self.findChildren(QPlainTextEdit):
            _enforce_ltr(plain_edit)
        for text_edit in self.findChildren(QTextEdit):
            _enforce_ltr(text_edit)


    def _migrate_scene_text_to_chapter(self, chapter: dict) -> None:
        """Переносит текст сцен в свободный текст главы (сцен 1,2,3... по порядку),
        чтобы текст правился только в свободном редакторе главы."""
        if not isinstance(chapter, dict):
            return
        scenes = chapter.get("scenes", [])
        if not isinstance(scenes, list) or not scenes:
            return
        parts: list[str] = []
        merged_any = False
        for index, scene in enumerate(scenes, start=1):
            text = str((scene or {}).get("text", "")).strip()
            if not text:
                continue
            title = str((scene or {}).get("title", "")).strip() or f"Сцена {index}"
            parts.append(f"=== Сцена {index}. {title} ===\n{text}")
            merged_any = True
        if not merged_any:
            return
        base = html_to_plain_chapter(str(chapter.get("chapterTextHtml", ""))) or str(chapter.get("chapterText", "")).strip()
        combined = "\n\n".join([p for p in ([base] if base else []) + parts])
        chapter["chapterText"] = combined
        chapter["chapterTextHtml"] = ""
        chapter["chapterTextSections"] = []
        for scene in scenes:
            if isinstance(scene, dict):
                scene["text"] = ""

    def _ensure_base_structure(self) -> None:
        content = self.document.content
        if "chapters" not in content or not isinstance(content["chapters"], list):
            content["chapters"] = []
        if not content["chapters"]:
            content["chapters"].append(
                {
                    "id": "chapter_001",
                    "title": self._default_chapter_title(1),
                    "scenes": [{"id": "scene_001", "title": self._default_scene_title(1), "text": ""}],
                }
            )
        if "library" not in content or not isinstance(content["library"], dict):
            content["library"] = {}
        for key in ENTITY_KEYS:
            if key not in content["library"] or not isinstance(content["library"][key], list):
                content["library"][key] = []

        if "timeline" not in content or not isinstance(content["timeline"], dict):
            content["timeline"] = {}
        if "book" not in content or not isinstance(content["book"], dict):
            content["book"] = {}
        book = content["book"]
        if "title" not in book:
            book["title"] = self.document.manifest.get("name", self.document.path.stem)
        for key in ("subtitle", "author", "genre", "format", "tone", "audience", "preface", "synopsis"):
            if key not in book:
                book[key] = ""
        # Гарантия наличия томов (многотомник).
        from scriborium.core.volumes import ensure_volumes

        ensure_volumes(content, fallback_title=book.get("title") or self.document.manifest.get("name", "Книга 1"))
        tl = content["timeline"]
        if "plotlines" not in tl or not isinstance(tl["plotlines"], list) or not tl["plotlines"]:
            tl["plotlines"] = [
                {"id": "plotline_main", "name": self._msg("default_plotline_main", "Main Plotline"), "color": "#4A7DFF"}
            ]
        main_plotline = tl["plotlines"][0].get("id", "plotline_main")
        for chapter in content["chapters"]:
            if "number" not in chapter:
                chapter["number"] = 0
            if "subtitle" not in chapter:
                chapter["subtitle"] = ""
            if "epigraph" not in chapter:
                chapter["epigraph"] = ""
            if "synopsis" not in chapter:
                chapter["synopsis"] = ""
            if "notes" not in chapter:
                chapter["notes"] = ""
            if "status" not in chapter:
                chapter["status"] = "in_progress"
            if "targetWordCount" not in chapter:
                chapter["targetWordCount"] = 0
            else:
                try:
                    chapter["targetWordCount"] = max(0, int(chapter["targetWordCount"]))
                except (TypeError, ValueError):
                    chapter["targetWordCount"] = 0
            if "chapterText" not in chapter:
                chapter["chapterText"] = ""
            if "chapterTextHtml" not in chapter:
                chapter["chapterTextHtml"] = ""
            if "chapterTextSections" not in chapter or not isinstance(chapter["chapterTextSections"], list):
                chapter["chapterTextSections"] = []
            for scene in chapter.get("scenes", []):
                if "plotlineId" not in scene:
                    scene["plotlineId"] = main_plotline
                for key in ("characterIds", "locationIds", "objectIds"):
                    if key not in scene:
                        scene[key] = []
                if "status" not in scene:
                    scene["status"] = default_scene_dict()["status"]
                else:
                    scene["status"] = normalize_scene_status(scene.get("status"))
                if "targetWordCount" not in scene:
                    scene["targetWordCount"] = 0
                else:
                    try:
                        scene["targetWordCount"] = max(0, int(scene["targetWordCount"]))
                    except (TypeError, ValueError):
                        scene["targetWordCount"] = 0
                if "dateTime" not in scene:
                    scene["dateTime"] = ""
                if "goal" not in scene:
                    scene["goal"] = ""
                if "conflict" not in scene:
                    scene["conflict"] = ""
                if "result" not in scene:
                    scene["result"] = ""
                if "characterArcPoints" not in scene or not isinstance(scene.get("characterArcPoints"), list):
                    scene["characterArcPoints"] = []
                if "plotlineIds" not in scene or not isinstance(scene.get("plotlineIds"), list):
                    scene["plotlineIds"] = [scene.get("plotlineId", main_plotline)]
                if scene.get("plotlineId") not in scene["plotlineIds"]:
                    scene["plotlineIds"].insert(0, scene.get("plotlineId", main_plotline))
        # Перенос текста сцен в свободный текст главы (текст редактируется только в свободном редакторе)
        for chapter in content["chapters"]:
            self._migrate_scene_text_to_chapter(chapter)

        for kind in ENTITY_KEYS:
            for entity in content.get("library", {}).get(kind, []):
                if "short" not in entity:
                    entity["short"] = ""
                if "notes" not in entity:
                    entity["notes"] = ""
                if "field2" not in entity:
                    entity["field2"] = ""
                if "field3" not in entity:
                    entity["field3"] = ""
                if "arcSummary" not in entity:
                    entity["arcSummary"] = ""
                if kind == "characters" and (
                    "arcPoints" not in entity or not isinstance(entity.get("arcPoints"), list)
                ):
                    entity["arcPoints"] = []
        self._sync_scene_appearances()

    def _default_plotline_id(self) -> str:
        pls = self.document.content.get("timeline", {}).get("plotlines", [])
        if pls:
            return str(pls[0].get("id", "plotline_main"))
        return "plotline_main"

    def _on_timeline_scene(self, chapter_index: int, scene_index: int) -> None:
        self._select_scene(chapter_index, scene_index)

    def _on_language_changed(self, data: object) -> None:
        lang = data.get("language", "ru") if isinstance(data, dict) else "ru"
        self.messages = self.i18n_service.messages(lang)
        self._live_spell_editor.reload_dictionary()
        self.logger.info("Language changed to %s in editor", lang)

    def _on_chapter_volume_clicked(self, data: object) -> None:
        if isinstance(data, dict):
            idx = data.get("index", 0)
            self._load_from_document(("chapter", idx))

    def _on_timeline_scene_moved(self, fc: int, fs: int, tc: int, insert_at: int) -> None:
        result = apply_scene_move(self.document.content, fc, fs, tc, insert_at)
        if result is None:
            return
        nc, ns = result
        self._sync_scene_appearances()
        self._mark_dirty()
        self._load_from_document(("scene", (nc, ns)))

    def _focus_timeline(self) -> None:
        self.timeline.setFocus()

    def _on_timeline_plotline_added(self) -> None:
        self._mark_dirty()
        self._refresh_plotline_combo()

    def _refresh_plotline_combo(self) -> None:
        selection = self._current_selection()
        if selection is None or selection[0] != "scene":
            return
        chapter_index, scene_index = selection[1]
        self._fill_plotline_combo_for_scene(chapter_index, scene_index)

    def _fill_plotline_combo_for_scene(self, chapter_index: int, scene_index: int) -> None:
        self._plotline_combo_blocked = True
        self.plotline_combo.clear()
        plotlines = self.document.content.get("timeline", {}).get("plotlines", [])
        scene = self.document.content["chapters"][chapter_index]["scenes"][scene_index]
        current = scene.get("plotlineId", "")
        for pl in plotlines:
            pid = pl.get("id", "")
            self.plotline_combo.addItem(pl.get("name", pid), pid)
        idx = self.plotline_combo.findData(current)
        if idx >= 0:
            self.plotline_combo.setCurrentIndex(idx)
        self._plotline_combo_blocked = False

    def _on_scene_plotline_changed(self) -> None:
        if self._plotline_combo_blocked:
            return
        selection = self._current_selection()
        if selection is None or selection[0] != "scene":
            return
        chapter_index, scene_index = selection[1]
        pid = self.plotline_combo.currentData()
        if not pid:
            return
        scene = self.document.content["chapters"][chapter_index]["scenes"][scene_index]
        if scene.get("plotlineId") == pid:
            return
        scene["plotlineId"] = pid
        ids = list(scene.get("plotlineIds", []))
        if pid not in ids:
            ids.insert(0, pid)
        scene["plotlineIds"] = ids
        self._mark_dirty()
        self._refresh_timeline_preserving_focus(chapter_index, scene_index)

    def _refresh_timeline_preserving_focus(self, chapter_index: int, scene_index: int) -> None:
        self.timeline.set_content(self.document.content)
        self.timeline.set_active_scene(chapter_index, scene_index)

    def _fill_scene_fields_for_scene(self, chapter_index: int, scene_index: int) -> None:
        self._scene_fields_blocked = True
        scene = self.document.content["chapters"][chapter_index]["scenes"][scene_index]
        self._scene_form_blocked = True
        self.scene_title_input.setText(scene.get("title", ""))
        self.scene_datetime_input.setText(scene.get("dateTime", ""))
        self.scene_goal_input.setText(scene.get("goal", ""))
        self.scene_conflict_input.setText(scene.get("conflict", ""))
        self.scene_result_input.setText(scene.get("result", ""))
        self._scene_form_blocked = False
        st = normalize_scene_status(scene.get("status"))
        idx = self.scene_status_combo.findData(st)
        if idx >= 0:
            self.scene_status_combo.setCurrentIndex(idx)
        self.scene_target_spin.setValue(int(scene.get("targetWordCount") or 0))
        self._refresh_scene_links_label(scene)
        self._scene_fields_blocked = False

    def _name_by_id(self, kind: str, entity_id: str) -> str:
        for e in self.document.content.get("library", {}).get(kind, []):
            if e.get("id") == entity_id:
                return e.get("name", entity_id)
        return entity_id

    def _plotline_name(self, plotline_id: str) -> str:
        for pl in self.document.content.get("timeline", {}).get("plotlines", []):
            if pl.get("id") == plotline_id:
                return pl.get("name", plotline_id)
        return plotline_id

    def _chapter_scene_context(self, chapter: dict) -> list[dict[str, object]]:
        context: list[dict[str, object]] = []
        scenes = chapter.get("scenes", [])
        for scene in scenes:
            status_key = normalize_scene_status(scene.get("status"))
            plotline_ids = list(scene.get("plotlineIds", []))
            if not plotline_ids:
                plotline_id = str(scene.get("plotlineId", "plotline_main"))
                if plotline_id:
                    plotline_ids = [plotline_id]
            context.append(
                {
                    "id": str(scene.get("id", "")),
                    "title": str(scene.get("title", "")),
                    "status": status_key,
                    "statusLabel": self._scene_status_label(status_key),
                    "dateTime": str(scene.get("dateTime", "") or scene.get("datetime", "")),
                    "goal": str(scene.get("goal", "")),
                    "conflict": str(scene.get("conflict", "")),
                    "result": str(scene.get("result", "")),
                    "description": str(scene.get("description", "") or scene.get("summary", "") or scene.get("synopsis", "")),
                    "notes": str(scene.get("notes", "")),
                    "text": str(scene.get("text", "")),
                    "characters": [self._name_by_id("characters", x) for x in scene.get("characterIds", [])],
                    "locations": [self._name_by_id("locations", x) for x in scene.get("locationIds", [])],
                    "objects": [self._name_by_id("objects", x) for x in scene.get("objectIds", [])],
                    "plotlines": [self._plotline_name(x) for x in plotline_ids],
                }
            )
        return context

    def _refresh_scene_links_label(self, scene: dict) -> None:
        chars = ", ".join(self._name_by_id("characters", x) for x in scene.get("characterIds", [])) or "—"
        locs = ", ".join(self._name_by_id("locations", x) for x in scene.get("locationIds", [])) or "—"
        objs = ", ".join(self._name_by_id("objects", x) for x in scene.get("objectIds", [])) or "—"
        lines = scene.get("plotlineIds", [])
        pl = ", ".join(self._plotline_name(x) for x in lines) or self._plotline_name(scene.get("plotlineId", "plotline_main"))
        self.scene_links_label.setText(
            (
                f"{self._msg('label_characters', 'Characters')}: {chars}\n"
                f"{self._msg('label_locations', 'Locations')}: {locs}\n"
                f"{self._msg('label_objects', 'Objects')}: {objs}\n"
                f"{self._msg('label_plotlines', 'Plotlines')}: {pl}"
            )
        )

    def _open_scene_entity_picker(self, kind: str) -> None:
        selection = self._current_selection()
        if selection is None or selection[0] != "scene":
            QMessageBox.information(
                self,
                self._msg("dlg_link_title", "Link"),
                self._msg("msg_select_scene_first", "Select a scene first."),
            )
            return
        chapter_index, scene_index = selection[1]
        scene = self.document.content["chapters"][chapter_index]["scenes"][scene_index]
        key = {"characters": "characterIds", "locations": "locationIds", "objects": "objectIds"}[kind]

        dlg = QDialog(self)
        dlg.setWindowTitle(self._msg("dlg_select_prefix", "Select:") + f" {self._entity_label(kind)}")
        dlg.resize(520, 420)
        lay = QVBoxLayout(dlg)
        lst = QListWidget()
        selected = set(scene.get(key, []))
        for entity in self.document.content["library"].get(kind, []):
            item = QListWidgetItem(entity.get("name", self._msg("label_untitled", "Untitled")))
            item.setFlags(item.flags() | Qt.ItemFlag.ItemIsUserCheckable)
            item.setData(Qt.ItemDataRole.UserRole, entity.get("id"))
            item.setCheckState(Qt.CheckState.Checked if entity.get("id") in selected else Qt.CheckState.Unchecked)
            lst.addItem(item)
        lay.addWidget(lst, 1)
        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(dlg.accept)
        buttons.rejected.connect(dlg.reject)
        lay.addWidget(buttons)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return

        chosen: list[str] = []
        for i in range(lst.count()):
            item = lst.item(i)
            if item.checkState() == Qt.CheckState.Checked:
                chosen.append(item.data(Qt.ItemDataRole.UserRole))
        scene[key] = chosen
        self._sync_scene_appearances()
        self._mark_dirty()
        self._refresh_scene_links_label(scene)
        self._load_from_document(("scene", (chapter_index, scene_index)))

    def _open_scene_plotlines_picker(self) -> None:
        selection = self._current_selection()
        if selection is None or selection[0] != "scene":
            QMessageBox.information(
                self,
                self._msg("plotlines_title", "Plotlines"),
                self._msg("msg_select_scene_first", "Select a scene first."),
            )
            return
        chapter_index, scene_index = selection[1]
        scene = self.document.content["chapters"][chapter_index]["scenes"][scene_index]
        plotlines = self.document.content.get("timeline", {}).get("plotlines", [])

        dlg = QDialog(self)
        dlg.setWindowTitle(self._msg("scene_plotlines_title", "Scene plotlines"))
        dlg.resize(520, 420)
        lay = QVBoxLayout(dlg)
        lst = QListWidget()
        selected = set(scene.get("plotlineIds", [scene.get("plotlineId", "plotline_main")]))
        for pl in plotlines:
            item = QListWidgetItem(pl.get("name", pl.get("id", "")))
            item.setFlags(item.flags() | Qt.ItemFlag.ItemIsUserCheckable)
            item.setData(Qt.ItemDataRole.UserRole, pl.get("id"))
            item.setCheckState(Qt.CheckState.Checked if pl.get("id") in selected else Qt.CheckState.Unchecked)
            lst.addItem(item)
        lay.addWidget(lst, 1)
        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(dlg.accept)
        buttons.rejected.connect(dlg.reject)
        lay.addWidget(buttons)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return

        chosen: list[str] = []
        for i in range(lst.count()):
            item = lst.item(i)
            if item.checkState() == Qt.CheckState.Checked:
                chosen.append(item.data(Qt.ItemDataRole.UserRole))
        if not chosen:
            chosen = [scene.get("plotlineId", "plotline_main")]
        scene["plotlineIds"] = chosen
        scene["plotlineId"] = chosen[0]
        self._mark_dirty()
        self._refresh_scene_links_label(scene)
        self._load_from_document(("scene", (chapter_index, scene_index)))

    def _on_scene_form_changed(self) -> None:
        if self._scene_form_blocked:
            return
        selection = self._current_selection()
        if selection is None or selection[0] != "scene":
            return
        chapter_index, scene_index = selection[1]
        scene = self.document.content["chapters"][chapter_index]["scenes"][scene_index]
        scene["title"] = self.scene_title_input.text().strip() or scene.get("title", "")
        scene["dateTime"] = self.scene_datetime_input.text().strip()
        scene["goal"] = self.scene_goal_input.text().strip()
        scene["conflict"] = self.scene_conflict_input.text().strip()
        scene["result"] = self.scene_result_input.text().strip()
        self._mark_dirty()
        self._refresh_scene_live_ui(chapter_index, scene_index)

    def _on_scene_status_changed(self) -> None:
        if self._scene_fields_blocked:
            return
        selection = self._current_selection()
        if selection is None or selection[0] != "scene":
            return
        chapter_index, scene_index = selection[1]
        st = self.scene_status_combo.currentData()
        if not isinstance(st, str):
            return
        scene = self.document.content["chapters"][chapter_index]["scenes"][scene_index]
        if normalize_scene_status(scene.get("status")) == st:
            return
        scene["status"] = st
        self._mark_dirty()
        self._refresh_timeline_preserving_focus(chapter_index, scene_index)

    def _on_scene_target_changed(self, value: int) -> None:
        if self._scene_fields_blocked:
            return
        selection = self._current_selection()
        if selection is None or selection[0] != "scene":
            return
        chapter_index, scene_index = selection[1]
        v = max(0, int(value))
        scene = self.document.content["chapters"][chapter_index]["scenes"][scene_index]
        if int(scene.get("targetWordCount") or 0) == v:
            return
        scene["targetWordCount"] = v
        self._mark_dirty()
        self._refresh_timeline_preserving_focus(chapter_index, scene_index)

    def _refresh_timeline_word_counts(self) -> None:
        selection = self._current_selection()
        if selection is None or selection[0] != "scene":
            return
        ci, si = selection[1]
        self.timeline.set_content(self.document.content)
        self.timeline.set_active_scene(ci, si)

    def _new_scene_dict(self, title: str) -> dict:
        d = default_scene_dict()
        d.update(
            {
                "id": self._new_id("scene", self._all_scene_ids()),
                "title": title,
                "text": "",
                "dateTime": "",
                "goal": "",
                "conflict": "",
                "result": "",
                "characterIds": [],
                "locationIds": [],
                "objectIds": [],
                "characterArcPoints": [],
                "plotlineId": self._default_plotline_id(),
                "plotlineIds": [self._default_plotline_id()],
            }
        )
        return d

    def _new_chapter_dict(self, title: str) -> dict:
        return {
            "id": self._new_id("chapter", [c.get("id", "") for c in self.document.content.get("chapters", [])]),
            "title": title,
            "number": 0,
            "subtitle": "",
            "epigraph": "",
            "synopsis": "",
            "notes": "",
            "status": "in_progress",
            "targetWordCount": 0,
            "chapterText": "",
            "chapterTextHtml": "",
            "chapterTextSections": [],
            "scenes": [self._new_scene_dict(self._default_scene_title(1))],
        }

    def _revert_to_last_saved(self) -> None:
        if not self.document.path.exists():
            return
        try:
            fresh = self.project_service.open_document(self.document.path)
        except OSError as err:
            QMessageBox.critical(
                self,
                self._msg("msg_error", "Error"),
                self._msg("msg_reload_project_failed", "Failed to reload project:") + f"\n{err}",
            )
            return
        self.document = fresh
        self._ensure_base_structure()
        self.dirty = False
        self.recovery_service.mark_dirty(self.document.path, False)
        self._load_from_document()
        self.status.showMessage(self._msg("status_changes_reverted", "Changes reverted (loaded from disk)"), 2500)

    def _setup_ui(self) -> None:
        title = self.document.manifest.get("name", self.document.path.stem)
        self.setWindowTitle(f"Scriborium {APP_VERSION_LABEL} — {title}.scri")
        icon_path = app_icon_path()
        if icon_path.exists():
            self.setWindowIcon(QIcon(str(icon_path)))
        screen = QApplication.primaryScreen()
        if screen is not None:
            g = screen.availableGeometry()
            self.resize(min(1280, max(1000, int(g.width() * 0.9))), min(820, max(700, int(g.height() * 0.9))))
        else:
            self.resize(1200, 780)

        root = QWidget()
        root_layout = QVBoxLayout(root)
        
        # Верхняя панель быстрых действий (как было раньше)
        actions_row = QHBoxLayout()
        add_chapter_btn = QPushButton(self._msg("editor_btn_add_chapter", "Добавить главу"))
        add_chapter_btn.clicked.connect(self._add_chapter)
        add_scene_btn = QPushButton(self._msg("editor_btn_add_scene", "Добавить сцену"))
        add_scene_btn.clicked.connect(self._add_scene)
        rename_btn = QPushButton(self._msg("editor_btn_rename", "Переименовать"))
        rename_btn.clicked.connect(self._rename_selected)
        delete_btn = QPushButton(self._msg("editor_btn_delete", "Удалить"))
        delete_btn.clicked.connect(self._delete_selected)
        library_btn = QPushButton(self._msg("editor_btn_library", "Библиотека"))
        library_btn.clicked.connect(self._open_library_manager)
        plotlines_btn = QPushButton(self._msg("editor_btn_plotlines", "Сюжетные линии"))
        plotlines_btn.clicked.connect(self._open_plotline_manager)
        link_btn = QPushButton(self._msg("editor_btn_link_scene", "Привязать к сцене"))
        link_btn.clicked.connect(self._open_scene_link_dialog)
        export_btn = QPushButton(self._msg("editor_btn_build_document", "Сформировать документ"))
        export_btn.setObjectName("primaryActionButton")
        export_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        export_btn.clicked.connect(lambda: self._export_project("docx"))
        add_chapter_btn.setToolTip(self._msg("tooltip_add_chapter", "Добавить новую главу в проект."))
        add_scene_btn.setToolTip(self._msg("tooltip_add_scene", "Добавить новую сцену в текущую главу."))
        rename_btn.setToolTip(self._msg("tooltip_rename", "Переименовать выбранный элемент структуры."))
        delete_btn.setToolTip(self._msg("tooltip_delete", "Удалить выбранный элемент структуры или библиотеки."))
        library_btn.setToolTip(self._msg("tooltip_library", "Открыть управление персонажами, локациями и объектами."))
        plotlines_btn.setToolTip(self._msg("tooltip_plotlines", "Открыть менеджер сюжетных линий."))
        link_btn.setToolTip(self._msg("tooltip_link_scene", "Связать текущую сцену с сущностями и линиями."))
        export_btn.setToolTip(self._msg("tooltip_build_document", "Открыть экспортный центр и сформировать документ."))
        actions_row.addWidget(add_chapter_btn)
        actions_row.addWidget(add_scene_btn)
        actions_row.addWidget(rename_btn)
        actions_row.addWidget(delete_btn)
        actions_row.addWidget(library_btn)
        actions_row.addWidget(plotlines_btn)
        actions_row.addWidget(link_btn)
        actions_row.addWidget(export_btn)
        actions_row.addStretch(1)
        root_layout.addLayout(actions_row)

        splitter = QSplitter()
        self.tree.setHeaderLabel(self._msg("tree_structure_library", "Structure and Library"))
        self.tree.currentItemChanged.connect(self._on_tree_item_changed)
        self.tree.structure_reordered.connect(self._sync_structure_from_tree)
        self.tree.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.tree.customContextMenuRequested.connect(self._show_tree_context_menu)
        self.tree.setDragDropMode(QAbstractItemView.DragDropMode.InternalMove)
        self.tree.setDefaultDropAction(Qt.DropAction.MoveAction)

        sidebar_splitter = QSplitter(Qt.Orientation.Vertical)
        sidebar_splitter.addWidget(self.tree)
        sidebar_splitter.addWidget(self.chapter_volume_widget)
        sidebar_splitter.setSizes([400, 200])
        splitter.addWidget(sidebar_splitter)
        # Редактор книги (как отдельная форма)
        book_layout = QVBoxLayout(self._editor_stack_book)
        b1 = QHBoxLayout()
        b1.addWidget(QLabel(self._msg("book_label_title", "Title:")))
        b1.addWidget(self.book_title_input, 1)
        book_layout.addLayout(b1)
        b2 = QHBoxLayout()
        b2.addWidget(QLabel(self._msg("book_label_subtitle", "Subtitle:")))
        b2.addWidget(self.book_subtitle_input, 1)
        book_layout.addLayout(b2)
        b3 = QHBoxLayout()
        b3.addWidget(QLabel(self._msg("book_label_author", "Author:")))
        b3.addWidget(self.book_author_input, 1)
        b3.addSpacing(12)
        b3.addWidget(QLabel(self._msg("book_label_genre", "Genre:")))
        b3.addWidget(self.book_genre_input, 1)
        book_layout.addLayout(b3)
        b4 = QHBoxLayout()
        b4.addWidget(QLabel(self._msg("book_label_format", "Format:")))
        b4.addWidget(self.book_format_input, 1)
        b4.addSpacing(12)
        b4.addWidget(QLabel(self._msg("book_label_tone", "Tone:")))
        b4.addWidget(self.book_tone_input, 1)
        b4.addSpacing(12)
        b4.addWidget(QLabel(self._msg("book_label_audience", "Audience:")))
        b4.addWidget(self.book_audience_input, 1)
        book_layout.addLayout(b4)
        book_layout.addWidget(QLabel(self._msg("book_label_preface", "Introductory Texts (Preface):")))
        book_layout.addWidget(self.book_preface_input)
        book_layout.addWidget(QLabel(self._msg("book_label_synopsis", "Annotation / Synopsis:")))
        book_layout.addWidget(self.book_synopsis_input)
        self.book_title_input.textChanged.connect(self._on_book_fields_changed)
        self.book_subtitle_input.textChanged.connect(self._on_book_fields_changed)
        self.book_author_input.textChanged.connect(self._on_book_fields_changed)
        self.book_genre_input.textChanged.connect(self._on_book_fields_changed)
        self.book_format_input.textChanged.connect(self._on_book_fields_changed)
        self.book_tone_input.textChanged.connect(self._on_book_fields_changed)
        self.book_audience_input.textChanged.connect(self._on_book_fields_changed)
        self.book_preface_input.textChanged.connect(self._on_book_fields_changed)
        self.book_synopsis_input.textChanged.connect(self._on_book_fields_changed)


        editor_side = QWidget()
        editor_layout = QVBoxLayout(editor_side)
        meta_row = QHBoxLayout()
        meta_row.addWidget(self.meta_label, 1)
        meta_row.addWidget(self._plotline_label)
        meta_row.addWidget(self.plotline_combo)
        editor_layout.addLayout(meta_row)
        self.plotline_combo.currentIndexChanged.connect(self._on_scene_plotline_changed)

        # Стек правой области: сцена / глава / сущность
        scene_layout = QVBoxLayout(self._editor_stack_scene)
        scene_primary = QHBoxLayout()
        scene_primary.addWidget(QLabel(self._msg("scene_label_title", "Scene Title:")))
        scene_primary.addWidget(self.scene_title_input, 1)
        scene_layout.addLayout(scene_primary)
        scene_row2 = QHBoxLayout()
        scene_row2.addWidget(QLabel(self._msg("scene_label_datetime", "Date/Time:")))
        scene_row2.addWidget(self.scene_datetime_input, 1)
        scene_layout.addLayout(scene_row2)
        scene_row3 = QHBoxLayout()
        scene_row3.addWidget(QLabel(self._msg("scene_label_goal", "Goal:")))
        scene_row3.addWidget(self.scene_goal_input, 1)
        scene_layout.addLayout(scene_row3)
        scene_row4 = QHBoxLayout()
        scene_row4.addWidget(QLabel(self._msg("scene_label_conflict", "Conflict:")))
        scene_row4.addWidget(self.scene_conflict_input, 1)
        scene_layout.addLayout(scene_row4)
        scene_row5 = QHBoxLayout()
        scene_row5.addWidget(QLabel(self._msg("scene_label_result", "Result:")))
        scene_row5.addWidget(self.scene_result_input, 1)
        scene_layout.addLayout(scene_row5)
        scene_links_row = QHBoxLayout()
        scene_links_row.addWidget(self.scene_char_btn)
        scene_links_row.addWidget(self.scene_loc_btn)
        scene_links_row.addWidget(self.scene_obj_btn)
        scene_links_row.addWidget(self.scene_plotlines_btn)
        scene_links_row.addStretch(1)
        scene_layout.addLayout(scene_links_row)
        self.scene_links_label.setWordWrap(True)
        scene_layout.addWidget(self.scene_links_label)
        scene_extra = QHBoxLayout(self._scene_extra)
        scene_extra.setContentsMargins(0, 0, 0, 0)
        scene_extra.addWidget(QLabel(self._msg("scene_label_status", "Status:")))
        scene_extra.addWidget(self.scene_status_combo)
        scene_extra.addWidget(QLabel(self._msg("scene_label_target_words", "Target Words (0 = none):")))
        scene_extra.addWidget(self.scene_target_spin)
        scene_extra.addStretch(1)
        scene_layout.addWidget(self._scene_extra)
        self._scene_extra.setVisible(False)
        self.scene_status_combo.currentIndexChanged.connect(self._on_scene_status_changed)
        self.scene_target_spin.valueChanged.connect(self._on_scene_target_changed)
        self.scene_title_input.textChanged.connect(self._on_scene_form_changed)
        self.scene_datetime_input.textChanged.connect(self._on_scene_form_changed)
        self.scene_goal_input.textChanged.connect(self._on_scene_form_changed)
        self.scene_conflict_input.textChanged.connect(self._on_scene_form_changed)
        self.scene_result_input.textChanged.connect(self._on_scene_form_changed)
        self.scene_char_btn.clicked.connect(lambda: self._open_scene_entity_picker("characters"))
        self.scene_loc_btn.clicked.connect(lambda: self._open_scene_entity_picker("locations"))
        self.scene_obj_btn.clicked.connect(lambda: self._open_scene_entity_picker("objects"))
        self.scene_plotlines_btn.clicked.connect(self._open_scene_plotlines_picker)
        self.editor.textChanged.connect(self._on_editor_text_changed)
        self.editor.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.editor.customContextMenuRequested.connect(self._show_editor_context_menu)
        scene_layout.addWidget(self.editor, 1)

        chapter_layout = QVBoxLayout(self._editor_stack_chapter)
        chapter_layout.setContentsMargins(0, 0, 0, 0)
        chapter_layout.setSpacing(8)

        info = QHBoxLayout()
        info.addWidget(QLabel(self._msg("chapter_label_title", "Chapter Title:")))
        info.addWidget(self.chapter_title_input, 1)
        chapter_layout.addLayout(info)

        row2 = QHBoxLayout()
        row2.addWidget(QLabel(self._msg("chapter_label_number", "Chapter Number:")))
        row2.addWidget(self.chapter_number_spin)
        row2.addSpacing(16)
        row2.addWidget(QLabel(self._msg("chapter_label_subtitle", "Subtitle:")))
        row2.addWidget(self.chapter_subtitle_input, 1)
        chapter_layout.addLayout(row2)

        row3 = QHBoxLayout()
        row3.addWidget(QLabel(self._msg("chapter_label_status", "Status:")))
        row3.addWidget(self.chapter_status_combo)
        row3.addSpacing(16)
        row3.addWidget(QLabel(self._msg("chapter_label_target_words", "Target (words), 0 = none:")))
        row3.addWidget(self.chapter_target_spin)
        row3.addStretch(1)
        chapter_layout.addLayout(row3)

        chapter_layout.addWidget(QLabel(self._msg("chapter_label_epigraph", "Chapter Epigraph (optional):")))
        chapter_layout.addWidget(self.chapter_epigraph_input)
        chapter_layout.addWidget(QLabel(self._msg("chapter_label_synopsis", "Short Chapter Summary (synopsis):")))
        chapter_layout.addWidget(self.chapter_synopsis_input)
        chapter_layout.addWidget(QLabel(self._msg("chapter_label_notes", "Chapter Notes (author only):")))
        chapter_layout.addWidget(self.chapter_notes_input)
        chapter_rich_text_btn = QPushButton(
            self._msg("chapter_btn_free_editor", "Свободный редактор главы")
        )
        chapter_rich_text_btn.setObjectName("primaryActionButton")
        chapter_rich_text_btn.clicked.connect(self._open_free_chapter_editor)
        chapter_layout.addWidget(chapter_rich_text_btn)

        buttons = QHBoxLayout()
        save_btn = QPushButton(self._msg("editor_action_save", "Save"))
        save_btn.clicked.connect(self.save_project)
        revert_btn = QPushButton(self._msg("button_revert", "Revert"))
        revert_btn.clicked.connect(self._revert_to_last_saved)
        buttons.addStretch(1)
        buttons.addWidget(save_btn)
        buttons.addWidget(revert_btn)
        chapter_layout.addLayout(buttons)

        self.chapter_title_input.textChanged.connect(self._on_chapter_fields_changed)
        self.chapter_number_spin.valueChanged.connect(self._on_chapter_fields_changed)
        self.chapter_subtitle_input.textChanged.connect(self._on_chapter_fields_changed)
        self.chapter_epigraph_input.textChanged.connect(self._on_chapter_fields_changed)
        self.chapter_synopsis_input.textChanged.connect(self._on_chapter_fields_changed)
        self.chapter_notes_input.textChanged.connect(self._on_chapter_fields_changed)
        self.chapter_status_combo.currentIndexChanged.connect(self._on_chapter_fields_changed)
        self.chapter_target_spin.valueChanged.connect(self._on_chapter_fields_changed)

        entity_layout = QVBoxLayout(self._editor_stack_entity)
        e1 = QHBoxLayout()
        e1.addWidget(QLabel(self._msg("entity_label_name", "Name:")))
        e1.addWidget(self.entity_name_input, 1)
        entity_layout.addLayout(e1)
        self._entity_role_label = QLabel(self._msg("entity_label_role", "Role/Type:"))
        e2 = QHBoxLayout()
        e2.addWidget(self._entity_role_label)
        e2.addWidget(self.entity_role_input, 1)
        entity_layout.addLayout(e2)
        self._entity_field2_label = QLabel(self._msg("entity_label_field2", "Characteristic 2:"))
        e22 = QHBoxLayout()
        e22.addWidget(self._entity_field2_label)
        e22.addWidget(self.entity_field2_input, 1)
        entity_layout.addLayout(e22)
        self._entity_field3_label = QLabel(self._msg("entity_label_field3", "Characteristic 3:"))
        e23 = QHBoxLayout()
        e23.addWidget(self._entity_field3_label)
        e23.addWidget(self.entity_field3_input, 1)
        entity_layout.addLayout(e23)
        e3 = QHBoxLayout()
        e3.addWidget(QLabel(self._msg("entity_label_short", "Short Description:")))
        e3.addWidget(self.entity_short_input, 1)
        entity_layout.addLayout(e3)
        self._entity_arc_label = QLabel(self._msg("entity_label_arc", "Character Arc (short):"))
        entity_layout.addWidget(self._entity_arc_label)
        entity_layout.addWidget(self.entity_arc_summary_input)
        entity_layout.addWidget(self.entity_arc_points_btn)
        entity_layout.addWidget(QLabel(self._msg("entity_label_description", "Detailed Description:")))
        entity_layout.addWidget(self.entity_description_input)
        entity_layout.addWidget(QLabel(self._msg("entity_label_notes", "Notes:")))
        entity_layout.addWidget(self.entity_notes_input)
        self.entity_name_input.textChanged.connect(self._on_entity_form_changed)
        self.entity_role_input.textChanged.connect(self._on_entity_form_changed)
        self.entity_field2_input.textChanged.connect(self._on_entity_form_changed)
        self.entity_field3_input.textChanged.connect(self._on_entity_form_changed)
        self.entity_arc_summary_input.textChanged.connect(self._on_entity_form_changed)
        self.entity_arc_points_btn.clicked.connect(self._open_character_arc_manager)
        self.entity_short_input.textChanged.connect(self._on_entity_form_changed)
        self.entity_description_input.textChanged.connect(self._on_entity_form_changed)
        self.entity_notes_input.textChanged.connect(self._on_entity_form_changed)

        editor_layout.addWidget(self._editor_stack_scene, 1)
        editor_layout.addWidget(self._editor_stack_chapter, 1)
        editor_layout.addWidget(self._editor_stack_entity, 1)
        editor_layout.addWidget(self._editor_stack_book, 1)
        self._editor_stack_scene.setVisible(False)
        self._editor_stack_chapter.setVisible(False)
        self._editor_stack_entity.setVisible(False)
        self._editor_stack_book.setVisible(False)
        splitter.addWidget(editor_side)


        splitter.setSizes([280, 920])
        root_layout.addWidget(splitter, 1)

        self.setCentralWidget(root)
        self.apply_theme()

        self._setup_menu()
        self.status.showMessage(self._msg("editor_status_ready", "Готово"))

    def _setup_menu(self) -> None:
        file_menu = self.menuBar().addMenu(self._msg("editor_menu_file", "Файл"))
        save_action = QAction(self._msg("editor_action_save", "Сохранить"), self)
        save_action.setShortcut(QKeySequence.StandardKey.Save)
        save_action.triggered.connect(self.save_project)
        file_menu.addAction(save_action)

        launcher_action = QAction(self._msg("editor_action_return_launcher", "Вернуться в лаунчер"), self)
        launcher_action.setShortcut(QKeySequence("Ctrl+Shift+L"))
        launcher_action.triggered.connect(self._return_to_launcher)
        file_menu.addAction(launcher_action)

        import_action = QAction(self._msg("editor_action_import", "Импорт..."), self)
        import_action.setShortcut(QKeySequence("Ctrl+I"))
        import_action.triggered.connect(self._import_from_file)
        file_menu.addAction(import_action)

        export_menu = file_menu.addMenu(self._msg("editor_menu_export", "Экспорт"))
        for label, fmt in (("TXT", "txt"), ("HTML", "html"), ("DOCX", "docx"), ("PDF", "pdf")):
            action = QAction(label, self)
            action.triggered.connect(lambda checked=False, f=fmt: self._export_project(f))
            export_menu.addAction(action)

        edit_menu = self.menuBar().addMenu(self._msg("editor_menu_edit", "Правка"))
        search_action = QAction(self._msg("editor_action_search_project", "Поиск по проекту"), self)
        search_action.setShortcut(QKeySequence("Ctrl+Shift+F"))
        search_action.triggered.connect(self._open_global_search)
        edit_menu.addAction(search_action)

        view_menu = self.menuBar().addMenu(self._msg("editor_menu_view", "Вид"))
        editor_settings_action = QAction(self._msg("editor_action_editor_settings", "Настройки редактора..."), self)
        editor_settings_action.setShortcut(QKeySequence("Ctrl+,"))
        editor_settings_action.triggered.connect(self._open_editor_settings_dialog)
        view_menu.addAction(editor_settings_action)
        theme_menu = view_menu.addMenu(self._msg("editor_menu_theme", "Тема интерфейса"))
        theme_standard_action = QAction(self._msg("theme_standard_label", "Стандарт"), self)
        theme_standard_action.triggered.connect(
            lambda checked=False: self._switch_theme(THEME_STANDARD)
        )
        theme_menu.addAction(theme_standard_action)
        theme_classic_action = QAction("Classic", self)
        theme_classic_action.triggered.connect(
            lambda checked=False: self._switch_theme(THEME_CLASSIC)
        )
        theme_menu.addAction(theme_classic_action)

        project_menu = self.menuBar().addMenu(self._msg("editor_menu_project", "Проект"))
        add_chapter_action = QAction(self._msg("editor_btn_add_chapter", "Добавить главу"), self)
        add_chapter_action.setShortcut(QKeySequence("Ctrl+Shift+N"))
        add_chapter_action.triggered.connect(self._add_chapter)
        project_menu.addAction(add_chapter_action)
        add_scene_action = QAction(self._msg("editor_btn_add_scene", "Добавить сцену"), self)
        add_scene_action.setShortcut(QKeySequence("Ctrl+N"))
        add_scene_action.triggered.connect(self._add_scene)
        project_menu.addAction(add_scene_action)
        plotlines_action = QAction(self._msg("editor_btn_plotlines", "Сюжетные линии") + "...", self)
        plotlines_action.setShortcut(QKeySequence("Ctrl+Alt+P"))
        plotlines_action.triggered.connect(self._open_plotline_manager)
        project_menu.addAction(plotlines_action)

        tools_menu = self.menuBar().addMenu(self._msg("editor_menu_tools", "Инструменты"))
        library_action = QAction(self._msg("editor_btn_library", "Библиотека"), self)
        library_action.setShortcut(QKeySequence("Ctrl+L"))
        library_action.triggered.connect(self._open_library_manager)
        tools_menu.addAction(library_action)
        links_nav_action = QAction(self._msg("editor_action_links_nav", "Навигация по связям"), self)
        links_nav_action.setShortcut(QKeySequence("Ctrl+J"))
        links_nav_action.triggered.connect(self._open_link_navigator_dialog)
        tools_menu.addAction(links_nav_action)

        help_menu = self.menuBar().addMenu(self._msg("editor_menu_help", "Справка"))
        guide_action = QAction(self._msg("editor_action_guide", "Руководство"), self)
        guide_action.setShortcut(QKeySequence("F1"))
        guide_action.triggered.connect(self._show_help_dialog)
        help_menu.addAction(guide_action)
        shortcuts_action = QAction(self._msg("editor_action_shortcuts", "Горячие клавиши"), self)
        shortcuts_action.setShortcut(QKeySequence("Ctrl+/"))
        shortcuts_action.triggered.connect(self._show_shortcuts_dialog)
        help_menu.addAction(shortcuts_action)
        about_action = QAction(self._msg("editor_action_about", "О программе"), self)
        about_action.triggered.connect(self._show_about_dialog)
        help_menu.addAction(about_action)

    def apply_theme(
        self, theme_override: str | None = None, high_contrast_override: bool | None = None
    ) -> None:
        theme = theme_override
        high_contrast = high_contrast_override
        if theme is None:
            settings = self.settings_service.load()
            theme = settings.theme
            if high_contrast is None:
                high_contrast = bool(settings.high_contrast)
        if high_contrast is None:
            high_contrast = bool(self.settings_service.load().high_contrast)
        self.setStyleSheet(
            stylesheet_for_theme(normalize_theme(theme), bool(high_contrast))
        )

    def _switch_theme(self, theme: str) -> None:
        settings = self.settings_service.load()
        settings.theme = normalize_theme(theme)
        self.settings_service.save(settings)
        self.apply_theme(settings.theme, settings.high_contrast)
        theme_label = "Classic" if normalize_theme(theme) == THEME_CLASSIC else self._msg("theme_standard_label", "Standard")
        self.status.showMessage(self._msg("status_theme_switched", "Theme switched: {theme}").format(theme=theme_label))

    def _open_editor_settings_dialog(self) -> None:
        settings = self.settings_service.load()
        dlg = QDialog(self)
        dlg.setWindowTitle(self._msg("editor_settings_title", "Editor settings"))
        lay = QVBoxLayout(dlg)
        lay.addWidget(QLabel(self._msg("editor_settings_theme_label", "Interface theme")))
        theme_combo = QComboBox()
        theme_combo.addItem("Стандарт", THEME_STANDARD)
        theme_combo.addItem("Classic", THEME_CLASSIC)
        idx = theme_combo.findData(normalize_theme(settings.theme))
        if idx >= 0:
            theme_combo.setCurrentIndex(idx)
        lay.addWidget(theme_combo)
        high_contrast_cb = QCheckBox(self._msg("editor_settings_high_contrast", "High contrast (better readability)"))
        high_contrast_cb.setChecked(bool(settings.high_contrast))
        lay.addWidget(high_contrast_cb)
        spellcheck_cb = QCheckBox(self._msg("editor_settings_spellcheck", "Check spelling while typing"))
        spellcheck_cb.setChecked(bool(settings.spellcheck_enabled))
        lay.addWidget(spellcheck_cb)
        preview_cb = QCheckBox(self._msg("editor_settings_show_focus_frames", "Show explicit focus frames"))
        preview_cb.setChecked(True)
        preview_cb.setEnabled(False)
        lay.addWidget(preview_cb)
        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        lay.addWidget(buttons)
        buttons.accepted.connect(dlg.accept)
        buttons.rejected.connect(dlg.reject)
        if dlg.exec() != int(QDialog.DialogCode.Accepted):
            return
        settings.theme = normalize_theme(str(theme_combo.currentData()))
        settings.high_contrast = high_contrast_cb.isChecked()
        settings.spellcheck_enabled = spellcheck_cb.isChecked()
        self.settings_service.save(settings)
        self.apply_theme(settings.theme, settings.high_contrast)
        self._apply_spellcheck_settings(settings)
        self.status.showMessage(self._msg("status_editor_settings_applied", "Editor settings applied"))

    def _apply_spellcheck_settings(self, settings=None) -> None:
        if settings is None:
            settings = self.settings_service.load()
        enabled = bool(settings.spellcheck_enabled)
        self._live_spell_editor.set_enabled(enabled)

    def _fill_chapter_editor(self, chapter_index: int) -> None:
        self._chapter_fields_blocked = True
        chapter = self.document.content["chapters"][chapter_index]
        self.chapter_title_input.setText(chapter.get("title", ""))
        self.chapter_number_spin.setValue(int(chapter.get("number") or 0))
        self.chapter_subtitle_input.setText(chapter.get("subtitle", ""))
        self.chapter_epigraph_input.setPlainText(chapter.get("epigraph", ""))
        self.chapter_synopsis_input.setPlainText(chapter.get("synopsis", ""))
        self.chapter_notes_input.setPlainText(chapter.get("notes", ""))
        st = chapter.get("status", "in_progress")
        idx = self.chapter_status_combo.findData(st)
        if idx >= 0:
            self.chapter_status_combo.setCurrentIndex(idx)
        self.chapter_target_spin.setValue(int(chapter.get("targetWordCount") or 0))
        self._chapter_fields_blocked = False

    def _on_chapter_fields_changed(self) -> None:
        if self._chapter_fields_blocked:
            return
        selection = self._current_selection()
        if selection is None or selection[0] != "chapter":
            return
        chapter_index = int(selection[1])
        chapter = self.document.content["chapters"][chapter_index]
        chapter["title"] = self.chapter_title_input.text().strip() or chapter.get("title", "")
        chapter["number"] = int(self.chapter_number_spin.value())
        chapter["subtitle"] = self.chapter_subtitle_input.text().strip()
        chapter["epigraph"] = self.chapter_epigraph_input.toPlainText()
        chapter["synopsis"] = self.chapter_synopsis_input.toPlainText()
        chapter["notes"] = self.chapter_notes_input.toPlainText()
        st = self.chapter_status_combo.currentData()
        if isinstance(st, str) and st in CHAPTER_STATUS_FALLBACK:
            chapter["status"] = st
        chapter["targetWordCount"] = int(self.chapter_target_spin.value())
        self._mark_dirty()
        self._refresh_chapter_live_ui(chapter_index)

    def _open_free_chapter_editor(self) -> None:
        selection = self._current_selection()
        if selection is None or selection[0] != "chapter":
            return
        chapter_index = int(selection[1])
        chapter = self.document.content["chapters"][chapter_index]
        chapter_title = str(chapter.get("title", self._default_chapter_title(chapter_index + 1)))
        chapter_html = _chapter_text_html_for_editor(chapter)

        def on_text_changed(new_html: str, new_plain: str) -> None:
            chapter["chapterTextHtml"] = new_html
            chapter["chapterText"] = new_plain
            chapter["chapterTextSections"] = []
            self._mark_dirty()
            self._refresh_chapter_live_ui(chapter_index)
            self.chapter_volume_widget.set_chapters(self.document.content.get("chapters", []))

        app_settings = self.settings_service.load()
        dialog = ChapterTextEditorDialog(
            parent=self,
            chapter_title=chapter_title,
            html_text=chapter_html,
            messages=self.messages,
            on_text_changed=on_text_changed,
            language_code=app_settings.language,
            spellcheck_service=self.spellcheck_service,
            spellcheck_enabled=app_settings.spellcheck_enabled,
            scene_context=self._chapter_scene_context(chapter),
        )
        dialog.exec()

    def _fill_entity_editor(self, kind: str, entity_id: str) -> None:
        found = self._entity_by_id(kind, entity_id)
        if found is None:
            return
        _, entity = found
        if kind == "characters":
            self._entity_role_label.setText("Роль:")
            self._entity_field2_label.setText("Возраст / этап:")
            self._entity_field3_label.setText("Мотивация:")
            self._entity_arc_label.setVisible(True)
            self.entity_arc_summary_input.setVisible(True)
            self.entity_arc_points_btn.setVisible(True)
        elif kind == "locations":
            self._entity_role_label.setText("Тип локации:")
            self._entity_field2_label.setText("Эпоха / время:")
            self._entity_field3_label.setText("Атмосфера:")
            self._entity_arc_label.setVisible(False)
            self.entity_arc_summary_input.setVisible(False)
            self.entity_arc_points_btn.setVisible(False)
        else:
            self._entity_role_label.setText("Тип объекта:")
            self._entity_field2_label.setText("Состояние:")
            self._entity_field3_label.setText("Назначение:")
            self._entity_arc_label.setVisible(False)
            self.entity_arc_summary_input.setVisible(False)
            self.entity_arc_points_btn.setVisible(False)
        self._entity_fields_blocked = True
        self.entity_name_input.setText(entity.get("name", ""))
        self.entity_role_input.setText(entity.get("extra", ""))
        self.entity_field2_input.setText(entity.get("field2", ""))
        self.entity_field3_input.setText(entity.get("field3", ""))
        self.entity_arc_summary_input.setPlainText(entity.get("arcSummary", ""))
        self.entity_short_input.setText(entity.get("short", ""))
        self.entity_description_input.setPlainText(entity.get("description", ""))
        self.entity_notes_input.setPlainText(entity.get("notes", ""))
        self._entity_fields_blocked = False

    def _fill_book_editor(self) -> None:
        book = self.document.content.get("book", {})
        self._book_fields_blocked = True
        self.book_title_input.setText(book.get("title", ""))
        self.book_subtitle_input.setText(book.get("subtitle", ""))
        self.book_author_input.setText(book.get("author", ""))
        self.book_genre_input.setText(book.get("genre", ""))
        self.book_format_input.setText(book.get("format", ""))
        self.book_tone_input.setText(book.get("tone", ""))
        self.book_audience_input.setText(book.get("audience", ""))
        self.book_preface_input.setPlainText(book.get("preface", ""))
        self.book_synopsis_input.setPlainText(book.get("synopsis", ""))
        self._book_fields_blocked = False

    def _on_book_fields_changed(self) -> None:
        if self._book_fields_blocked:
            return
        selection = self._current_selection()
        if selection is None or selection[0] != "book":
            return
        book = self.document.content.setdefault("book", {})
        book["title"] = self.book_title_input.text().strip() or book.get("title", "")
        book["subtitle"] = self.book_subtitle_input.text().strip()
        book["author"] = self.book_author_input.text().strip()
        book["genre"] = self.book_genre_input.text().strip()
        book["format"] = self.book_format_input.text().strip()
        book["tone"] = self.book_tone_input.text().strip()
        book["audience"] = self.book_audience_input.text().strip()
        book["preface"] = self.book_preface_input.toPlainText()
        book["synopsis"] = self.book_synopsis_input.toPlainText()
        self.document.manifest["name"] = book["title"] or self.document.manifest.get("name", "")
        self._mark_dirty()
        self._refresh_book_live_ui()

    def _sync_structure_from_tree(self) -> None:
        content = self.document.content
        old_chapters = content.get("chapters", [])
        chapter_by_id = {c.get("id"): c for c in old_chapters if c.get("id")}
        scene_by_id: dict[str, dict] = {}
        for chapter in old_chapters:
            for scene in chapter.get("scenes", []):
                sid = scene.get("id")
                if sid:
                    scene_by_id[sid] = scene

        new_chapters: list[dict] = []
        volume_ids_in_order: list[str] = []
        for t in range(self.tree.topLevelItemCount()):
            v_item = self.tree.topLevelItem(t)
            if v_item is None or v_item.data(0, Qt.ItemDataRole.UserRole + 1) != "volume":
                continue
            vid = str(v_item.data(0, Qt.ItemDataRole.UserRole))
            volume_ids_in_order.append(vid)
            for ci in range(v_item.childCount()):
                ch_item = v_item.child(ci)
                if ch_item is None or ch_item.data(0, Qt.ItemDataRole.UserRole + 1) != "chapter":
                    continue
                cid = ch_item.data(0, Qt.ItemDataRole.UserRole + 2)
                chapter = chapter_by_id.get(cid)
                if chapter is None:
                    continue
                new_scenes: list[dict] = []
                for si in range(ch_item.childCount()):
                    sc_item = ch_item.child(si)
                    if sc_item is None or sc_item.data(0, Qt.ItemDataRole.UserRole + 1) != "scene":
                        continue
                    sid = sc_item.data(0, Qt.ItemDataRole.UserRole + 2)
                    scene = scene_by_id.get(sid)
                    if scene is not None:
                        new_scenes.append(scene)
                if not new_scenes:
                    new_scenes.append(self._new_scene_dict(self._default_scene_title(1)))
                chapter["scenes"] = new_scenes
                chapter["volumeId"] = vid
                new_chapters.append(chapter)

        if new_chapters:
            volumes = content.get("volumes", [])
            volume_by_id = {v.get("id"): v for v in volumes if v.get("id")}
            content["volumes"] = [volume_by_id[v] for v in volume_ids_in_order if v in volume_by_id]
            for i, volume in enumerate(content["volumes"], start=1):
                volume["number"] = i
            content["chapters"] = new_chapters

        self._sync_scene_appearances()
        self._mark_dirty()
        self._load_from_document()

    def _on_entity_form_changed(self) -> None:
        if self._entity_fields_blocked:
            return
        selection = self._current_selection()
        if selection is None or selection[0] != "library_entity":
            return
        kind, entity_id = selection[1]
        found = self._entity_by_id(kind, entity_id)
        if found is None:
            return
        _, entity = found
        entity["name"] = self.entity_name_input.text().strip() or entity.get("name", "")
        entity["extra"] = self.entity_role_input.text().strip()
        entity["field2"] = self.entity_field2_input.text().strip()
        entity["field3"] = self.entity_field3_input.text().strip()
        entity["arcSummary"] = self.entity_arc_summary_input.toPlainText().strip()
        entity["short"] = self.entity_short_input.text().strip()
        entity["description"] = self.entity_description_input.toPlainText()
        entity["notes"] = self.entity_notes_input.toPlainText()
        self._mark_dirty()
        self._refresh_entity_live_ui(kind, entity_id)

    def _open_character_arc_manager(self) -> None:
        selection = self._current_selection()
        if selection is None or selection[0] != "library_entity":
            return
        kind, entity_id = selection[1]
        if kind != "characters":
            return
        found = self._entity_by_id(kind, entity_id)
        if found is None:
            return
        _, entity = found
        dialog = CharacterArcEditorDialog(
            self,
            str(entity.get("name", "Персонаж")),
            self.document.content.get("chapters", []),
            list(entity.get("arcPoints", [])),
        )
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return
        entity["arcPoints"] = dialog.points()
        self._sync_scene_appearances()
        self._mark_dirty()
        self._load_from_document(("library_entity", (kind, entity_id)))

    def _show_about_dialog(self) -> None:
        QMessageBox.information(
            self,
            self._msg("editor_action_about", "О программе"),
            self._msg("about_body", "Scriborium\nРедактор структуры и сцен проекта."),
        )

    def _show_help_dialog(self) -> None:
        HelpDialog(self, self.settings_service.load().language).exec()

    def _show_shortcuts_dialog(self) -> None:
        shortcuts = [
            ("Ctrl+S", "Сохранить проект"),
            ("Ctrl+Shift+F", "Поиск по проекту"),
            ("Ctrl+I", "Импорт"),
            ("Ctrl+N", "Добавить сцену"),
            ("Ctrl+Shift+N", "Добавить главу"),
            ("Ctrl+Alt+P", "Менеджер сюжетных линий"),
            ("Ctrl+L", "Библиотека"),
            ("Ctrl+J", "Навигация по связям"),
            ("Ctrl+/", "Карта горячих клавиш"),
            ("F1", "Справка"),
        ]
        dlg = QDialog(self)
        dlg.setWindowTitle(self._msg("editor_action_shortcuts", "Горячие клавиши"))
        dlg.resize(520, 420)
        lay = QVBoxLayout(dlg)
        text = QTextEdit()
        text.setReadOnly(True)
        body = ["<h3>Горячие клавиши</h3>", "<table cellpadding='4' cellspacing='0'>"]
        for combo, action in shortcuts:
            body.append(
                f"<tr><td><b>{html.escape(combo)}</b></td><td>{html.escape(action)}</td></tr>"
            )
        body.append("</table>")
        text.setHtml("".join(body))
        lay.addWidget(text, 1)
        btn = QDialogButtonBox(QDialogButtonBox.StandardButton.Close)
        btn.rejected.connect(dlg.reject)
        btn.accepted.connect(dlg.accept)
        lay.addWidget(btn)
        dlg.exec()

    def _open_link_navigator_dialog(self) -> None:
        selection = self._current_selection()
        if selection is None or selection[0] != "scene":
            QMessageBox.information(self, "Навигация по связям", "Сначала выберите сцену.")
            return
        chapter_index, scene_index = selection[1]
        scene = self.document.content["chapters"][chapter_index]["scenes"][scene_index]
        dlg = QDialog(self)
        dlg.setWindowTitle("Навигация по связям")
        dlg.resize(560, 420)
        lay = QVBoxLayout(dlg)
        lay.addWidget(QLabel("Связанные элементы сцены (двойной клик для перехода):"))
        lst = QListWidget()
        for kind, key, prefix in (
            ("characters", "characterIds", "Персонаж"),
            ("locations", "locationIds", "Локация"),
            ("objects", "objectIds", "Объект"),
        ):
            for entity_id in scene.get(key, []):
                name = self._name_by_id(kind, entity_id)
                item = QListWidgetItem(f"{prefix}: {name}")
                item.setData(Qt.ItemDataRole.UserRole, ("library_entity", (kind, entity_id)))
                lst.addItem(item)
        for plotline_id in scene.get("plotlineIds", []) or [scene.get("plotlineId", "plotline_main")]:
            item = QListWidgetItem(f"Линия: {self._plotline_name(plotline_id)}")
            item.setData(Qt.ItemDataRole.UserRole, ("plotline", plotline_id))
            lst.addItem(item)
        if lst.count() == 0:
            lst.addItem("Для этой сцены пока нет связей.")
        lay.addWidget(lst, 1)

        def open_selected() -> None:
            item = lst.currentItem()
            if item is None:
                return
            payload = item.data(Qt.ItemDataRole.UserRole)
            if not payload:
                return
            self._load_from_document(payload)
            dlg.accept()

        lst.itemDoubleClicked.connect(lambda *_: open_selected())
        btns = QDialogButtonBox(QDialogButtonBox.StandardButton.Open | QDialogButtonBox.StandardButton.Close)
        open_btn = btns.button(QDialogButtonBox.StandardButton.Open)
        if open_btn is not None:
            open_btn.clicked.connect(open_selected)
        close_btn = btns.button(QDialogButtonBox.StandardButton.Close)
        if close_btn is not None:
            close_btn.clicked.connect(dlg.reject)
        lay.addWidget(btns)
        dlg.exec()

    def _open_plotline_manager(self) -> None:
        tl = self.document.content.setdefault("timeline", {})
        plotlines = tl.setdefault("plotlines", [])
        if not plotlines:
            plotlines.append({"id": "plotline_main", "name": "Главная сюжетная линия", "color": "#4A7DFF"})

        dlg = QDialog(self)
        dlg.setWindowTitle(self._msg("plotlines_title", "Plotlines"))
        dlg.resize(540, 360)
        lay = QVBoxLayout(dlg)
        lst = QListWidget()
        lay.addWidget(lst, 1)

        def refresh() -> None:
            lst.clear()
            for pl in plotlines:
                item = QListWidgetItem(f"{pl.get('name','')}  [{pl.get('color','#000000')}]")
                item.setData(Qt.ItemDataRole.UserRole, pl.get("id"))
                lst.addItem(item)

        def current_plotline() -> dict | None:
            item = lst.currentItem()
            if item is None:
                return None
            pid = item.data(Qt.ItemDataRole.UserRole)
            for pl in plotlines:
                if pl.get("id") == pid:
                    return pl
            return None

        row = QHBoxLayout()
        add_btn = QPushButton(self._msg("btn_add", "Add"))
        ren_btn = QPushButton(self._msg("btn_rename", "Rename"))
        clr_btn = QPushButton(self._msg("btn_color", "Color"))
        del_btn = QPushButton(self._msg("btn_delete", "Delete"))
        row.addWidget(add_btn)
        row.addWidget(ren_btn)
        row.addWidget(clr_btn)
        row.addWidget(del_btn)
        row.addStretch(1)
        lay.addLayout(row)

        def on_add() -> None:
            name, ok = QInputDialog.getText(
                dlg,
                self._msg("plotline_new_title", "New plotline"),
                self._msg("timeline_name_label", "Name:"),
            )
            if not ok or not name.strip():
                return
            max_n = 0
            for p in plotlines:
                pid = p.get("id", "")
                if pid.startswith("plotline_") and pid.split("_")[-1].isdigit():
                    max_n = max(max_n, int(pid.split("_")[-1]))
            plotlines.append({"id": f"plotline_{max_n + 1:03d}", "name": name.strip(), "color": "#888888"})
            refresh()

        def on_rename() -> None:
            pl = current_plotline()
            if pl is None:
                return
            name, ok = QInputDialog.getText(
                dlg,
                self._msg("plotline_rename_title", "Rename plotline"),
                self._msg("timeline_name_label", "Name:"),
                text=pl.get("name", ""),
            )
            if ok and name.strip():
                pl["name"] = name.strip()
                refresh()

        def on_color() -> None:
            pl = current_plotline()
            if pl is None:
                return
            color = QColorDialog.getColor(parent=dlg)
            if color.isValid():
                pl["color"] = color.name()
                refresh()

        def on_delete() -> None:
            pl = current_plotline()
            if pl is None:
                return
            if pl.get("id") == "plotline_main":
                QMessageBox.information(
                    dlg,
                    self._msg("delete_title", "Delete"),
                    self._msg("plotline_main_cannot_delete", "Main plotline cannot be deleted."),
                )
                return
            answer = QMessageBox.question(
                dlg,
                self._msg("plotline_delete_title", "Delete plotline"),
                self._msg(
                    "plotline_delete_question",
                    "Delete plotline \"{name}\"? All scenes will be moved to Main Plotline.",
                ).format(name=pl.get("name", "")),
            )
            if answer != QMessageBox.StandardButton.Yes:
                return
            pid = pl.get("id")
            plotlines[:] = [x for x in plotlines if x.get("id") != pid]
            for ch in self.document.content.get("chapters", []):
                for sc in ch.get("scenes", []):
                    if sc.get("plotlineId") == pid:
                        sc["plotlineId"] = "plotline_main"
                    ids = [x for x in sc.get("plotlineIds", []) if x != pid]
                    if not ids:
                        ids = [sc.get("plotlineId", "plotline_main")]
                    sc["plotlineIds"] = ids
            refresh()

        add_btn.clicked.connect(on_add)
        ren_btn.clicked.connect(on_rename)
        clr_btn.clicked.connect(on_color)
        del_btn.clicked.connect(on_delete)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(dlg.accept)
        buttons.rejected.connect(dlg.reject)
        lay.addWidget(buttons)

        refresh()
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        self._mark_dirty()
        self._load_from_document(self._current_selection())

    def _setup_autosave(self) -> None:
        settings = self.settings_service.load()
        self.autosave_timer.timeout.connect(self._handle_autosave)
        self.autosave_timer.start(max(1, settings.autosave_minutes) * 60 * 1000)

    def _mark_dirty(self) -> None:
        self.dirty = True
        self.recovery_service.mark_dirty(self.document.path, True)
        self.status.showMessage(self._msg("status_unsaved_changes", "There are unsaved changes"), 1500)

    def _new_id(self, prefix: str, values: list[str]) -> str:
        max_num = 0
        for value in values:
            if not value.startswith(prefix + "_"):
                continue
            tail = value.split("_")[-1]
            if tail.isdigit():
                max_num = max(max_num, int(tail))
        return f"{prefix}_{max_num + 1:03d}"

    def _all_scene_ids(self) -> list[str]:
        chapters = self.document.content.get("chapters", [])
        return [s.get("id", "") for c in chapters for s in c.get("scenes", [])]

    def _all_entity_ids(self, kind: str) -> list[str]:
        return [entity.get("id", "") for entity in self.document.content["library"].get(kind, [])]

    def _entity_by_id(self, kind: str, entity_id: str) -> tuple[int, dict] | None:
        entities = self.document.content["library"][kind]
        for index, entity in enumerate(entities):
            if entity.get("id") == entity_id:
                return index, entity
        return None

    def _sync_scene_appearances(self) -> None:
        library = self.document.content["library"]
        for kind in ENTITY_KEYS:
            for entity in library[kind]:
                entity["sceneAppearances"] = []

        for chapter in self.document.content["chapters"]:
            for scene in chapter.get("scenes", []):
                scene_id = scene.get("id")
                if not scene_id:
                    continue
                for kind, scene_key in (
                    ("characters", "characterIds"),
                    ("locations", "locationIds"),
                    ("objects", "objectIds"),
                ):
                    for entity in library[kind]:
                        if entity.get("id") in scene.get(scene_key, []):
                            appearances = entity.setdefault("sceneAppearances", [])
                            if scene_id not in appearances:
                                appearances.append(scene_id)
        self._sync_character_arc_points()

    def _sync_character_arc_points(self) -> None:
        chapter_scene_map: dict[str, dict] = {}
        for chapter in self.document.content.get("chapters", []):
            for scene in chapter.get("scenes", []):
                scene_id = scene.get("id")
                if scene_id:
                    chapter_scene_map[str(scene_id)] = scene
                    scene["characterArcPoints"] = []

        for character in self.document.content.get("library", {}).get("characters", []):
            character_id = character.get("id")
            if not character_id:
                continue
            normalized_points: list[dict] = []
            for point in character.get("arcPoints", []):
                scene_id = str(point.get("sceneId", "")).strip()
                if not scene_id or scene_id not in chapter_scene_map:
                    continue
                try:
                    arc_value = int(point.get("arcValue", 0))
                except (TypeError, ValueError):
                    arc_value = 0
                normalized = {
                    "characterId": character_id,
                    "sceneId": scene_id,
                    "arcValue": arc_value,
                    "description": str(point.get("description", "")).strip(),
                }
                normalized_points.append(normalized)
                chapter_scene_map[scene_id].setdefault("characterArcPoints", []).append(normalized)
            character["arcPoints"] = normalized_points

    def _load_from_document(self, select: tuple[str, object] | None = None) -> None:
        self.tree.clear()
        content = self.document.content

        book = content.get("book", {})
        book_title = book.get("title", self.document.manifest.get("name", "Без названия"))
        book_item = QTreeWidgetItem([f"Книга: {book_title}"])
        book_item.setData(0, Qt.ItemDataRole.UserRole, None)
        book_item.setData(0, Qt.ItemDataRole.UserRole + 1, "book")
        book_item.setFlags(
            Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable
        )
        self.tree.addTopLevelItem(book_item)

        from scriborium.core.volumes import get_volumes, volume_label

        self._book_item = book_item
        self._volume_items: list[tuple[str, QTreeWidgetItem]] = []
        chapters = content["chapters"]
        # Главы группируются по томам (многотомник).
        for volume in get_volumes(content, fallback_title=book_title):
            volume_item = QTreeWidgetItem([volume_label(volume)])
            volume_item.setData(0, Qt.ItemDataRole.UserRole, volume.get("id", ""))
            volume_item.setData(0, Qt.ItemDataRole.UserRole + 1, "volume")
            volume_item.setData(0, Qt.ItemDataRole.UserRole + 2, volume.get("number", 0))
            volume_item.setFlags(
                Qt.ItemFlag.ItemIsEnabled
                | Qt.ItemFlag.ItemIsSelectable
                | Qt.ItemFlag.ItemIsDropEnabled
            )
            self.tree.addTopLevelItem(volume_item)
            volume_id = volume.get("id", "")
            self._volume_items.append((volume_id, volume_item))
            for chapter_index, chapter in enumerate(chapters):
                if chapter.get("volumeId") != volume_id:
                    continue
                chapter_item = QTreeWidgetItem([f"Глава: {chapter.get('title', self._default_chapter_title(chapter_index + 1))}"])
                chapter_item.setData(0, Qt.ItemDataRole.UserRole, chapter_index)
                chapter_item.setData(0, Qt.ItemDataRole.UserRole + 1, "chapter")
                chapter_item.setData(0, Qt.ItemDataRole.UserRole + 2, chapter.get("id", ""))
                chapter_item.setFlags(
                    Qt.ItemFlag.ItemIsEnabled
                    | Qt.ItemFlag.ItemIsSelectable
                    | Qt.ItemFlag.ItemIsDragEnabled
                    | Qt.ItemFlag.ItemIsDropEnabled
                )
                volume_item.addChild(chapter_item)
                for scene_index, scene in enumerate(chapter.get("scenes", [])):
                    scene_item = QTreeWidgetItem([f"Сцена: {scene.get('title', self._default_scene_title(scene_index + 1))}"])
                    scene_item.setData(0, Qt.ItemDataRole.UserRole, (chapter_index, scene_index))
                    scene_item.setData(0, Qt.ItemDataRole.UserRole + 1, "scene")
                    scene_item.setData(0, Qt.ItemDataRole.UserRole + 2, scene.get("id", ""))
                    scene_item.setFlags(
                        Qt.ItemFlag.ItemIsEnabled
                        | Qt.ItemFlag.ItemIsSelectable
                        | Qt.ItemFlag.ItemIsDragEnabled
                    )
                    chapter_item.addChild(scene_item)

        plotlines_root = QTreeWidgetItem(["Сюжетные линии"])
        plotlines_root.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable)
        self.tree.addTopLevelItem(plotlines_root)
        for pl in content.get("timeline", {}).get("plotlines", []):
            pl_item = QTreeWidgetItem([f"Линия: {pl.get('name', pl.get('id', ''))}"])
            pl_item.setData(0, Qt.ItemDataRole.UserRole, pl.get("id", ""))
            pl_item.setData(0, Qt.ItemDataRole.UserRole + 1, "plotline")
            pl_item.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable)
            plotlines_root.addChild(pl_item)

        library_root = QTreeWidgetItem(["Библиотека"])
        library_root.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable)
        self.tree.addTopLevelItem(library_root)
        for kind in ENTITY_KEYS:
            items = content["library"].get(kind, [])
            group = QTreeWidgetItem([f"{self._entity_label(kind)} ({len(items)})"])
            group.setData(0, Qt.ItemDataRole.UserRole, kind)
            group.setData(0, Qt.ItemDataRole.UserRole + 1, "library_group")
            group.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable)
            library_root.addChild(group)
            for entity in items:
                node = QTreeWidgetItem([entity.get("name", "Без названия")])
                node.setData(0, Qt.ItemDataRole.UserRole, (kind, entity.get("id", "")))
                node.setData(0, Qt.ItemDataRole.UserRole + 1, "library_entity")
                node.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable)
                group.addChild(node)

        self.tree.expandAll()
        picked: QTreeWidgetItem | None = None
        if select is not None:
            stype, payload = select
            if stype == "book":
                picked = book_item
            elif stype == "scene":
                chapter_index, scene_index = payload
                picked = self._find_scene_tree_item(int(chapter_index), int(scene_index))
            elif stype == "chapter":
                picked = self._find_chapter_tree_item(int(payload))
            elif stype == "volume":
                volume_id = str(payload)
                for _vid, _vitem in self._volume_items:
                    if _vid == volume_id:
                        picked = _vitem
                        break
            elif stype == "library_entity":
                kind, entity_id = payload
                group_index = ENTITY_KEYS.index(kind)
                group = library_root.child(group_index) if library_root.childCount() > group_index else None
                if group is not None:
                    for i in range(group.childCount()):
                        child = group.child(i)
                        if child.data(0, Qt.ItemDataRole.UserRole) == (kind, entity_id):
                            picked = child
                            break
            elif stype == "plotline":
                plotline_id = payload
                for i in range(plotlines_root.childCount()):
                    child = plotlines_root.child(i)
                    if child.data(0, Qt.ItemDataRole.UserRole) == plotline_id:
                        picked = child
                        break
        self.timeline.set_content(self.document.content)
        self.chapter_volume_widget.set_chapters(self.document.content.get("chapters", []))
        if picked is None:
            picked = book_item if book_item is not None else None
        if picked is not None:
            self.tree.setCurrentItem(picked)

    def _current_selection(self) -> tuple[str, object] | None:
        item = self.tree.currentItem()
        if item is None:
            return None
        item_type = item.data(0, Qt.ItemDataRole.UserRole + 1)
        data = item.data(0, Qt.ItemDataRole.UserRole)
        if item_type in {"book", "volume", "chapter", "scene", "library_group", "library_entity", "plotline"}:
            return (item_type, data)
        return None

    def _volume_for_id(self, volume_id: object) -> dict | None:
        for volume in self.document.content.get("volumes", []):
            if isinstance(volume, dict) and volume.get("id") == volume_id:
                return volume
        return None

    def _find_chapter_tree_item(self, chapter_index: int) -> QTreeWidgetItem | None:
        for _vid, vitem in self._volume_items:
            for ci in range(vitem.childCount()):
                node = vitem.child(ci)
                if (
                    node is not None
                    and node.data(0, Qt.ItemDataRole.UserRole + 1) == "chapter"
                    and node.data(0, Qt.ItemDataRole.UserRole) == chapter_index
                ):
                    return node
        return None

    def _find_scene_tree_item(self, chapter_index: int, scene_index: int) -> QTreeWidgetItem | None:
        chapter_item = self._find_chapter_tree_item(chapter_index)
        if chapter_item is None or chapter_item.childCount() <= scene_index:
            return None
        return chapter_item.child(scene_index)

    def _resolve_add_volume_id(self) -> str:
        from scriborium.core.volumes import get_volumes

        volumes = get_volumes(self.document.content)
        if not volumes:
            return ""
        sel = self._current_selection()
        if sel is not None:
            stype, payload = sel
            if stype == "volume":
                return str(payload)
            if stype == "chapter":
                idx = int(payload)
                if 0 <= idx < len(self.document.content["chapters"]):
                    return self.document.content["chapters"][idx].get("volumeId") or volumes[0]["id"]
            elif stype == "scene":
                idx = int(payload[0])
                if 0 <= idx < len(self.document.content["chapters"]):
                    return self.document.content["chapters"][idx].get("volumeId") or volumes[0]["id"]
        return volumes[0]["id"]

    def _first_chapter_index_in_volume(self, volume_id: str) -> int | None:
        for i, chapter in enumerate(self.document.content["chapters"]):
            if chapter.get("volumeId") == volume_id:
                return i
        return None

    def _add_volume(self) -> None:
        from scriborium.core.volumes import get_volumes, new_volume

        title, ok = QInputDialog.getText(
            self, self._msg("vol_add_title", "New Volume"), self._msg("vol_name_label", "Volume name:")
        )
        if not ok:
            return
        new_volume(self.document.content, title=title.strip())
        self._mark_dirty()
        created = get_volumes(self.document.content)[-1]
        self._load_from_document(("volume", created["id"]))

    def _rename_volume(self, volume_id: str | None = None) -> None:
        if volume_id is None:
            sel = self._current_selection()
            if sel is None or sel[0] != "volume":
                return
            volume_id = sel[1]
        volume = self._volume_for_id(volume_id)
        if volume is None:
            return
        new_name, ok = QInputDialog.getText(
            self,
            self._msg("vol_rename_title", "Rename Volume"),
            self._msg("vol_name_label", "Volume name:"),
            text=str(volume.get("title", "")),
        )
        if ok and new_name.strip():
            volume["title"] = new_name.strip()
            self._mark_dirty()
            self._load_from_document(("volume", volume_id))

    def _edit_volume_info(self, volume_id: str | None = None) -> None:
        if volume_id is None:
            sel = self._current_selection()
            if sel is None or sel[0] != "volume":
                return
            volume_id = sel[1]
        volume = self._volume_for_id(volume_id)
        if volume is None:
            return
        dlg = QDialog(self)
        dlg.setWindowTitle(self._msg("vol_edit_title", "Volume info"))
        lay = QVBoxLayout(dlg)
        title_edit = QLineEdit(str(volume.get("title", "")))
        syn_edit = QPlainTextEdit(str(volume.get("synopsis", "")))
        syn_edit.setMaximumHeight(160)
        lay.addWidget(QLabel(self._msg("vol_name_label", "Volume name:")))
        lay.addWidget(title_edit)
        lay.addWidget(QLabel(self._msg("vol_annotation_label", "Volume annotation:")))
        lay.addWidget(syn_edit)
        btns = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        btns.accepted.connect(dlg.accept)
        btns.rejected.connect(dlg.reject)
        lay.addWidget(btns)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        new_title = title_edit.text().strip()
        if new_title:
            volume["title"] = new_title
        volume["synopsis"] = syn_edit.toPlainText()
        self._mark_dirty()
        self._load_from_document(("volume", volume_id))

    def _delete_volume(self, volume_id: str | None = None) -> None:
        from scriborium.core.volumes import get_volumes, remove_volume

        if volume_id is None:
            sel = self._current_selection()
            if sel is None or sel[0] != "volume":
                return
            volume_id = sel[1]
        volumes = get_volumes(self.document.content)
        if len(volumes) <= 1:
            QMessageBox.information(
                self,
                self._msg("vol_delete_title", "Delete Volume"),
                self._msg("vol_last_msg", "Cannot delete the last volume."),
            )
            return
        volume = self._volume_for_id(volume_id)
        if volume is None:
            return
        answer = QMessageBox.question(
            self,
            self._msg("vol_delete_title", "Delete Volume"),
            self._msg(
                "vol_delete_question",
                'Delete volume "{title}"? Its chapters will move to the first volume.',
            ).format(title=str(volume.get("title", ""))),
        )
        if answer != QMessageBox.StandardButton.Yes:
            return
        if remove_volume(self.document.content, volume_id):
            for i, v in enumerate(get_volumes(self.document.content), start=1):
                v["number"] = i
        self._sync_scene_appearances()
        self._mark_dirty()
        self._load_from_document(("volume", get_volumes(self.document.content)[0]["id"]))

    def _populate_volume_move_menu(self, menu: QMenu, chapter_index: int) -> None:
        from scriborium.core.volumes import get_volumes

        chapters = self.document.content["chapters"]
        if not (0 <= chapter_index < len(chapters)):
            return
        current = chapters[chapter_index].get("volumeId")
        for volume in get_volumes(self.document.content):
            vid = volume.get("id")
            if vid == current:
                continue
            label = self._msg("vol_label_named", "Volume {number}: {title}").format(
                number=int(volume.get("number") or 0), title=str(volume.get("title", ""))
            )
            action = menu.addAction(label)
            action.triggered.connect(
                lambda _checked=False, vv=vid, ci=chapter_index: self._move_chapter_to_volume(ci, vv)
            )

    def _move_chapter_to_volume(self, chapter_index: int, volume_id: str) -> None:
        from scriborium.core.volumes import assign_chapter_volume, ensure_volumes

        ensure_volumes(self.document.content)
        if not (0 <= chapter_index < len(self.document.content["chapters"])):
            return
        assign_chapter_volume(self.document.content, chapter_index, volume_id)
        self._mark_dirty()
        self._load_from_document(("chapter", chapter_index))

    def _set_current_tree_item_text(self, text: str) -> None:
        item = self.tree.currentItem()
        if item is not None:
            item.setText(0, text)

    def _refresh_window_title(self) -> None:
        title = self.document.manifest.get("name", self.document.path.stem)
        self.setWindowTitle(f"Scriborium {APP_VERSION_LABEL} — {title}.scri")

    def _refresh_book_live_ui(self) -> None:
        book = self.document.content.get("book", {})
        book_title = book.get("title", self.document.manifest.get("name", "Без названия"))
        self._set_current_tree_item_text(f"Книга: {book_title}")
        self.meta_label.setText(self._msg("meta_book", "Book: {title}").format(title=book_title))
        self._refresh_window_title()

    def _refresh_chapter_live_ui(self, chapter_index: int) -> None:
        chapter = self.document.content["chapters"][chapter_index]
        title = chapter.get("title", self._default_chapter_title(chapter_index + 1))
        self._set_current_tree_item_text(f"Глава: {title}")
        scene_count = len(chapter.get("scenes", []))
        words = count_chapter_free_words(chapter)
        target = int(chapter.get("targetWordCount") or 0)
        tail = f"{words} / {target}" if target > 0 else f"{words}"
        self.meta_label.setText(
            self._msg(
                "meta_chapter",
                "Chapter: {title} | Scenes: {scenes} | Free editor: {words} words",
            ).format(title=title, scenes=scene_count, words=tail)
        )
        self.chapter_volume_widget.set_chapters(self.document.content.get("chapters", []))

    def _refresh_scene_live_ui(self, chapter_index: int, scene_index: int) -> None:
        chapter = self.document.content["chapters"][chapter_index]
        scene = chapter["scenes"][scene_index]
        scene_title = scene.get("title", self._default_scene_title(scene_index + 1))
        self._set_current_tree_item_text(f"Сцена: {scene_title}")
        self.meta_label.setText(
            f"Раздел: {chapter.get('title', '')} / {scene_title} | Персонажи: {len(scene.get('characterIds', []))}, "
            f"Локации: {len(scene.get('locationIds', []))}, Объекты: {len(scene.get('objectIds', []))}"
        )
        self._refresh_scene_links_label(scene)
        self._refresh_timeline_preserving_focus(chapter_index, scene_index)

    def _refresh_entity_live_ui(self, kind: str, entity_id: str) -> None:
        found = self._entity_by_id(kind, entity_id)
        if found is None:
            return
        _, entity = found
        name = entity.get("name", "")
        self._set_current_tree_item_text(name or "Без названия")
        self.meta_label.setText(
            f"{self._entity_label(kind)}: {name} | Встречается в сценах: {len(entity.get('sceneAppearances', []))}"
        )

    def _add_chapter(self, volume_id: str | None = None) -> None:
        content = self.document.content
        from scriborium.core.volumes import ensure_volumes

        volumes = ensure_volumes(content)
        target_volume_id = volume_id or self._resolve_add_volume_id()
        if target_volume_id not in {v.get("id") for v in volumes}:
            target_volume_id = volumes[0]["id"]
        chapters = content["chapters"]
        chapter_index = len(chapters)
        new_chapter = self._new_chapter_dict(self._default_chapter_title(chapter_index + 1))
        new_chapter["volumeId"] = target_volume_id
        chapters.append(new_chapter)
        self._mark_dirty()
        self._load_from_document(("chapter", chapter_index))

    def _add_scene(self, chapter_index: int | None = None) -> None:
        content = self.document.content
        chapters = content["chapters"]
        if chapter_index is None:
            selection = self._current_selection()
            if selection is not None:
                item_type, payload = selection
                if item_type == "chapter":
                    chapter_index = int(payload)
                elif item_type == "scene":
                    chapter_index = int(payload[0])
                elif item_type == "volume":
                    volume_id = str(payload)
                    found = self._first_chapter_index_in_volume(volume_id)
                    if found is None:
                        self._add_chapter(volume_id=volume_id)
                        return
                    chapter_index = found
        if chapter_index is None:
            chapter_index = 0
        if not chapters or not (0 <= chapter_index < len(chapters)):
            self._add_chapter()
            return
        chapter = chapters[chapter_index]
        scenes = chapter.setdefault("scenes", [])
        scene_index = len(scenes)
        scenes.append(self._new_scene_dict(self._default_scene_title(scene_index + 1)))
        self._mark_dirty()
        self._load_from_document(("scene", (chapter_index, scene_index)))

    def _rename_selected(self) -> None:
        selection = self._current_selection()
        if selection is None:
            QMessageBox.information(
                self,
                self._msg("rename_title", "Rename"),
                self._msg("msg_select_item", "Select an item."),
            )
            return
        item_type, payload = selection
        if item_type == "volume":
            self._rename_volume(payload)
            return
        if item_type == "chapter":
            chapter_index = int(payload)
            current_name = self.document.content["chapters"][chapter_index].get("title", "")
            new_name, ok = QInputDialog.getText(
                self,
                self._msg("rename_chapter_title", "Rename chapter"),
                self._msg("rename_new_name", "New name:"),
                text=current_name,
            )
            if ok and new_name.strip():
                self.document.content["chapters"][chapter_index]["title"] = new_name.strip()
                self._mark_dirty()
                self._load_from_document(("chapter", chapter_index))
            return
        if item_type == "scene":
            chapter_index, scene_index = payload
            current_name = self.document.content["chapters"][chapter_index]["scenes"][scene_index].get("title", "")
            new_name, ok = QInputDialog.getText(
                self,
                self._msg("rename_scene_title", "Rename scene"),
                self._msg("rename_new_name", "New name:"),
                text=current_name,
            )
            if ok and new_name.strip():
                self.document.content["chapters"][chapter_index]["scenes"][scene_index]["title"] = new_name.strip()
                self._mark_dirty()
                self._load_from_document(("scene", (chapter_index, scene_index)))
            return
        if item_type == "library_entity":
            kind, entity_id = payload
            found = self._entity_by_id(kind, entity_id)
            if found is None:
                return
            _, entity = found
            dialog = EntityEditDialog(self, kind, entity, self.messages)
            if dialog.exec() == QDialog.DialogCode.Accepted:
                entity.update(dialog.to_entity())
                self._mark_dirty()
                self._load_from_document(("library_entity", (kind, entity_id)))
            return
        QMessageBox.information(
            self,
            self._msg("rename_title", "Rename"),
            self._msg("msg_cannot_rename_item", "This item cannot be renamed."),
        )

    def _delete_selected(self) -> None:
        selection = self._current_selection()
        if selection is None:
            QMessageBox.information(
                self,
                self._msg("delete_title", "Delete"),
                self._msg("msg_select_item", "Select an item."),
            )
            return
        item_type, payload = selection
        if item_type == "volume":
            self._delete_volume(payload)
            return
        chapters = self.document.content["chapters"]

        if item_type == "chapter":
            chapter_index = int(payload)
            answer = QMessageBox.question(
                self,
                self._msg("delete_chapter_title", "Delete chapter"),
                self._msg("delete_chapter_question", "Delete selected chapter with all scenes?"),
            )
            if answer != QMessageBox.StandardButton.Yes:
                return
            chapters.pop(chapter_index)
            if not chapters:
                self.document.content["chapters"] = []
                self._add_chapter()
                return
            self._mark_dirty()
            self._sync_scene_appearances()
            self._load_from_document(("chapter", max(0, chapter_index - 1)))
            return

        if item_type == "scene":
            chapter_index, scene_index = payload
            answer = QMessageBox.question(
                self,
                self._msg("delete_scene_title", "Delete scene"),
                self._msg("delete_scene_question", "Delete selected scene?"),
            )
            if answer != QMessageBox.StandardButton.Yes:
                return
            scenes = chapters[chapter_index]["scenes"]
            scenes.pop(scene_index)
            if not scenes:
                scenes.append(self._new_scene_dict(self._default_scene_title(1)))
                target_scene = 0
            else:
                target_scene = max(0, scene_index - 1)
            self._mark_dirty()
            self._sync_scene_appearances()
            self._load_from_document(("scene", (chapter_index, target_scene)))
            return

        if item_type == "library_entity":
            kind, entity_id = payload
            found = self._entity_by_id(kind, entity_id)
            if found is None:
                return
            _, entity = found
            appearances = entity.get("sceneAppearances", [])
            strategy = self._ask_entity_delete_strategy(entity.get("name", ""), len(appearances))
            if strategy == "cancel":
                return
            self.document.content["library"][kind] = [
                e for e in self.document.content["library"][kind] if e.get("id") != entity_id
            ]
            if strategy == "remove_links":
                scene_key = {"characters": "characterIds", "locations": "locationIds", "objects": "objectIds"}[kind]
                for chapter in chapters:
                    for scene in chapter.get("scenes", []):
                        scene[scene_key] = [x for x in scene.get(scene_key, []) if x != entity_id]
            self._mark_dirty()
            self._sync_scene_appearances()
            self._load_from_document()
            return

        QMessageBox.information(
            self,
            self._msg("delete_title", "Delete"),
            self._msg("msg_cannot_delete_item", "This item cannot be deleted."),
        )

    def _ask_entity_delete_strategy(self, entity_name: str, used_in: int) -> str:
        if used_in <= 0:
            answer = QMessageBox.question(
                self,
                self._msg("delete_entity_title", "Delete entity"),
                self._msg("delete_entity_question", "Delete entity \"{name}\"?").format(name=entity_name),
            )
            return "remove_links" if answer == QMessageBox.StandardButton.Yes else "cancel"

        dialog = QMessageBox(self)
        dialog.setWindowTitle(self._msg("delete_entity_title", "Delete entity"))
        dialog.setText(
            self._msg(
                "delete_entity_used_message",
                "Entity \"{name}\" is used in {count} scenes.\nChoose deletion mode:",
            ).format(name=entity_name, count=used_in)
        )
        remove_links_btn = dialog.addButton(
            self._msg("delete_entity_remove_links", "Delete entity and remove it from all scenes"),
            QMessageBox.ButtonRole.AcceptRole,
        )
        keep_orphans_btn = dialog.addButton(
            self._msg("delete_entity_keep_orphans", "Delete entity, keep inactive links"),
            QMessageBox.ButtonRole.DestructiveRole,
        )
        cancel_btn = dialog.addButton(self._msg("btn_cancel", "Cancel"), QMessageBox.ButtonRole.RejectRole)
        dialog.exec()

        clicked = dialog.clickedButton()
        if clicked == remove_links_btn:
            return "remove_links"
        if clicked == keep_orphans_btn:
            return "keep_orphans"
        if clicked == cancel_btn:
            return "cancel"
        return "cancel"

    def _open_library_manager(self) -> None:
        dialog = LibraryManagerDialog(self, self.document.content["library"], self.messages)
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return
        self._sync_scene_appearances()
        self._mark_dirty()
        self._load_from_document(dialog.select_hint)

    def _open_scene_link_dialog(self) -> None:
        selection = self._current_selection()
        if selection is None or selection[0] != "scene":
            QMessageBox.information(
                self,
                self._msg("dlg_link_title", "Link"),
                self._msg("msg_select_scene_first", "Select a scene first."),
            )
            return
        chapter_index, scene_index = selection[1]
        scene = self.document.content["chapters"][chapter_index]["scenes"][scene_index]
        dialog = SceneLinkDialog(self, self.document.content["library"], scene, self.messages)
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return
        scene["characterIds"] = dialog.selected_ids("characters")
        scene["locationIds"] = dialog.selected_ids("locations")
        scene["objectIds"] = dialog.selected_ids("objects")
        self._sync_scene_appearances()
        self._mark_dirty()
        self._load_from_document(("scene", (chapter_index, scene_index)))

    def _show_tree_context_menu(self, pos) -> None:
        item = self.tree.itemAt(pos)
        menu = QMenu(self)

        if item is None:
            add_chapter_action = menu.addAction(self._msg("editor_btn_add_chapter", "Add Chapter"))
            add_chapter_action.triggered.connect(self._add_chapter)
            add_volume_action = menu.addAction(self._msg("ctx_add_volume", "Add Volume"))
            add_volume_action.triggered.connect(self._add_volume)
            menu.exec(self.tree.viewport().mapToGlobal(pos))
            return

        item_type = item.data(0, Qt.ItemDataRole.UserRole + 1)

        if item_type == "volume":
            volume_id = str(item.data(0, Qt.ItemDataRole.UserRole))
            add_chapter_action = menu.addAction(self._msg("editor_btn_add_chapter", "Add Chapter"))
            add_chapter_action.triggered.connect(lambda: self._add_chapter(volume_id=volume_id))
            rename_action = menu.addAction(self._msg("vol_rename_title", "Rename Volume"))
            rename_action.triggered.connect(lambda: self._rename_volume(volume_id))
            edit_action = menu.addAction(self._msg("ctx_edit_volume", "Edit Volume"))
            edit_action.triggered.connect(lambda: self._edit_volume_info(volume_id))
            delete_action = menu.addAction(self._msg("ctx_delete_volume", "Delete Volume"))
            delete_action.triggered.connect(lambda: self._delete_volume(volume_id))
        elif item_type == "book":
            add_volume_action = menu.addAction(self._msg("ctx_add_volume", "Add Volume"))
            add_volume_action.triggered.connect(self._add_volume)
        elif item_type == "chapter":
            add_scene_action = menu.addAction(self._msg("editor_btn_add_scene", "Add Scene"))
            add_scene_action.triggered.connect(self._add_scene)
            rename_action = menu.addAction(self._msg("editor_btn_rename", "Rename"))
            rename_action.triggered.connect(self._rename_selected)
            move_menu = menu.addMenu(self._msg("ctx_move_to_volume", "Move to Volume..."))
            self._populate_volume_move_menu(move_menu, int(item.data(0, Qt.ItemDataRole.UserRole)))
            delete_action = menu.addAction(self._msg("ctx_delete_chapter", "Delete Chapter"))
            delete_action.triggered.connect(self._delete_selected)
        elif item_type == "scene":
            link_action = menu.addAction(self._msg("editor_action_link_entities", "Link entities to scene"))
            link_action.triggered.connect(self._open_scene_link_dialog)
            rename_action = menu.addAction(self._msg("editor_btn_rename", "Rename"))
            rename_action.triggered.connect(self._rename_selected)
            delete_action = menu.addAction(self._msg("ctx_delete_scene", "Delete Scene"))
            delete_action.triggered.connect(self._delete_selected)
        elif item_type == "library_group":
            manage_action = menu.addAction(self._msg("library_manage_title", "Library manager"))
            manage_action.triggered.connect(self._open_library_manager)
        elif item_type == "library_entity":
            edit_action = menu.addAction(self._msg("ctx_edit_card", "Edit card"))
            edit_action.triggered.connect(self._rename_selected)
            delete_action = menu.addAction(self._msg("ctx_delete_entity", "Delete entity"))
            delete_action.triggered.connect(self._delete_selected)
        else:
            manage_action = menu.addAction(self._msg("library_manage_title", "Library manager"))
            manage_action.triggered.connect(self._open_library_manager)

        menu.exec(self.tree.viewport().mapToGlobal(pos))

    def _on_tree_item_changed(
        self, current: QTreeWidgetItem | None, _previous: QTreeWidgetItem | None
    ) -> None:
        if current is None:
            return
        item_type = current.data(0, Qt.ItemDataRole.UserRole + 1)
        data = current.data(0, Qt.ItemDataRole.UserRole)

        if item_type == "volume":
            volume_id = str(data)
            volume = self._volume_for_id(volume_id)
            if volume is None:
                return
            number = int(volume.get("number") or 0)
            title = str(volume.get("title", ""))
            label = title if not number else self._msg(
                "vol_label_named", "Volume {number}: {title}"
            ).format(number=number, title=title)
            chapters_count = sum(
                1
                for ch in self.document.content["chapters"]
                if ch.get("volumeId") == volume_id
            )
            self.meta_label.setText(
                self._msg("meta_volume", "Volume: {title} | Chapters: {count}").format(
                    title=label, count=chapters_count
                )
            )
            self.editor.blockSignals(True)
            self.editor.setReadOnly(True)
            body = label
            synopsis = str(volume.get("synopsis", "")).strip()
            if synopsis:
                body += "\n\n" + self._msg("vol_annotation_label", "Volume annotation:") + "\n" + synopsis
            self.editor.setPlainText(body)
            self.editor.moveCursor(QTextCursor.MoveOperation.Start)
            self.editor.blockSignals(False)
            self._plotline_label.setVisible(False)
            self.plotline_combo.setVisible(False)
            self._scene_extra.setVisible(False)
            self._editor_stack_scene.setVisible(False)
            self._editor_stack_chapter.setVisible(False)
            self._editor_stack_entity.setVisible(False)
            self._editor_stack_book.setVisible(False)
            self.timeline.set_active_scene(-1, -1)
            return

        if item_type == "book":
            book = self.document.content.get("book", {})
            self.meta_label.setText(self._msg("meta_book", "Book: {title}").format(title=book.get("title", "")))
            self._plotline_label.setVisible(False)
            self.plotline_combo.setVisible(False)
            self._scene_extra.setVisible(False)
            self._editor_stack_scene.setVisible(False)
            self._editor_stack_chapter.setVisible(False)
            self._editor_stack_entity.setVisible(False)
            self._editor_stack_book.setVisible(True)
            self._fill_book_editor()
            self.timeline.set_active_scene(-1, -1)
            return

        if item_type == "scene":
            chapter_index, scene_index = data
            scene = self.document.content["chapters"][chapter_index]["scenes"][scene_index]
            self.editor.blockSignals(True)
            self.editor.setReadOnly(True)
            scene_text = str(scene.get("text", ""))
            hint = self._msg(
                "scene_text_free_editor_hint",
                "(Текст сцены редактируется в свободном редакторе главы)",
            )
            self.editor.setPlainText((scene_text + "\n" + hint).strip())
            self.editor.moveCursor(QTextCursor.MoveOperation.Start)
            self.editor.blockSignals(False)
            self.meta_label.setText(
                self._msg(
                    "meta_scene",
                    "Section: {chapter} / {scene} | Characters: {characters}, Locations: {locations}, Objects: {objects}",
                ).format(
                    chapter=self.document.content["chapters"][chapter_index].get("title", ""),
                    scene=scene.get("title", ""),
                    characters=len(scene.get("characterIds", [])),
                    locations=len(scene.get("locationIds", [])),
                    objects=len(scene.get("objectIds", [])),
                )
            )
            self._plotline_label.setVisible(True)
            self.plotline_combo.setVisible(True)
            self._fill_plotline_combo_for_scene(chapter_index, scene_index)
            self._fill_scene_fields_for_scene(chapter_index, scene_index)
            self._scene_extra.setVisible(True)
            self._editor_stack_scene.setVisible(True)
            self._editor_stack_chapter.setVisible(False)
            self._editor_stack_entity.setVisible(False)
            self._editor_stack_book.setVisible(False)
            self.timeline.set_active_scene(chapter_index, scene_index)
            return

        if item_type == "chapter":
            chapter_index = int(data)
            chapter = self.document.content["chapters"][chapter_index]
            scene_count = len(chapter.get("scenes", []))
            words = count_chapter_free_words(chapter)
            target = int(chapter.get("targetWordCount") or 0)
            tail = f"{words} / {target}" if target > 0 else f"{words}"
            self.meta_label.setText(
                self._msg(
                    "meta_chapter",
                    "Chapter: {title} | Scenes: {scenes} | Free editor: {words} words",
                ).format(title=chapter.get("title", ""), scenes=scene_count, words=tail)
            )
            self._plotline_label.setVisible(False)
            self.plotline_combo.setVisible(False)
            self._scene_extra.setVisible(False)
            self._editor_stack_scene.setVisible(False)
            self._editor_stack_chapter.setVisible(True)
            self._editor_stack_entity.setVisible(False)
            self._editor_stack_book.setVisible(False)
            self._fill_chapter_editor(chapter_index)
            self.timeline.set_active_scene(-1, -1)
            return

        if item_type == "plotline":
            pid = str(data)
            plotline = None
            for pl in self.document.content.get("timeline", {}).get("plotlines", []):
                if pl.get("id") == pid:
                    plotline = pl
                    break
            if plotline is None:
                return
            linked = 0
            for ch in self.document.content.get("chapters", []):
                for sc in ch.get("scenes", []):
                    ids = sc.get("plotlineIds", []) or [sc.get("plotlineId", "plotline_main")]
                    if pid in ids:
                        linked += 1
            self.meta_label.setText(
                self._msg(
                    "meta_plotline",
                    "Plotline: {name} | Color: {color} | Scenes: {count}",
                ).format(name=plotline.get("name", ""), color=plotline.get("color", ""), count=linked)
            )
            self.editor.blockSignals(True)
            self.editor.setReadOnly(True)
            self.editor.setPlainText(
                self._msg("plotline_editor_hint", "Plotline editing: Project -> Plotlines...")
                + "\n"
                + self._msg("field_id", "ID:")
                + f" {plotline.get('id', '')}\n"
                + self._msg("timeline_name_label", "Name:")
                + f" {plotline.get('name', '')}\n"
                + self._msg("btn_color", "Color")
                + f": {plotline.get('color', '')}\n"
            )
            self.editor.blockSignals(False)
            self._plotline_label.setVisible(False)
            self.plotline_combo.setVisible(False)
            self._scene_extra.setVisible(False)
            self._editor_stack_scene.setVisible(True)
            self._editor_stack_chapter.setVisible(False)
            self._editor_stack_entity.setVisible(False)
            self._editor_stack_book.setVisible(False)
            self.timeline.set_active_scene(-1, -1)
            return

        if item_type == "library_entity":
            kind, entity_id = data
            found = self._entity_by_id(kind, entity_id)
            if found is None:
                return
            _, entity = found
            self._fill_entity_editor(kind, entity_id)
            self.meta_label.setText(
                self._msg(
                    "meta_entity_usage",
                    "{kind}: {name} | Used in scenes: {count}",
                ).format(
                    kind=self._entity_label(kind),
                    name=entity.get("name", ""),
                    count=len(entity.get("sceneAppearances", [])),
                )
            )
            self._plotline_label.setVisible(False)
            self.plotline_combo.setVisible(False)
            self._scene_extra.setVisible(False)
            self._editor_stack_scene.setVisible(False)
            self._editor_stack_chapter.setVisible(False)
            self._editor_stack_entity.setVisible(True)
            self._editor_stack_book.setVisible(False)
            self.timeline.set_active_scene(-1, -1)
            return

        self.editor.blockSignals(True)
        self.editor.setReadOnly(True)
        self.editor.setPlainText("")
        self.editor.blockSignals(False)
        self.meta_label.setText(self._msg("meta_select_scene", "Select a scene to edit"))
        self._plotline_label.setVisible(False)
        self.plotline_combo.setVisible(False)
        self._scene_extra.setVisible(False)
        self._editor_stack_scene.setVisible(False)
        self._editor_stack_chapter.setVisible(False)
        self._editor_stack_entity.setVisible(False)
        self._editor_stack_book.setVisible(False)
        self.timeline.set_active_scene(-1, -1)

    def _on_editor_text_changed(self) -> None:
        selection = self._current_selection()
        if selection is None or selection[0] != "scene":
            return
        # Текст сцены редактируется только в свободном редакторе главы
        self._mark_dirty()
        self._timeline_word_timer.start()
        text = self.editor.toPlainText()
        event_bus.emit("text:changed", {"text": text, "document_id": str(self.document.path)})
        self.chapter_volume_widget.set_chapters(self.document.content.get("chapters", []))

    def _select_scene(self, chapter_index: int, scene_index: int) -> None:
        scene_item = self._find_scene_tree_item(int(chapter_index), int(scene_index))
        if scene_item is not None:
            self.tree.setCurrentItem(scene_item)

    def _highlight_range(self, start: int, end: int) -> None:
        cursor = self.editor.textCursor()
        cursor.setPosition(start)
        cursor.setPosition(end, QTextCursor.MoveMode.KeepAnchor)
        self.editor.setTextCursor(cursor)
        self.editor.setFocus()

    def _open_global_search(self) -> None:
        dialog = GlobalSearchDialog(self, self.search_service, self.document, self.messages)
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return
        if dialog.selected_result is None:
            return
        picked = dialog.selected_result
        self._select_scene(picked.chapter_index, picked.scene_index)
        self._highlight_range(picked.start, picked.end)

    def save_project(self) -> None:
        self._persist(trigger="manual")

    def _return_to_launcher(self) -> None:
        if self.dirty:
            self._persist(trigger="manual")
        launcher = None
        for widget in QApplication.topLevelWidgets():
            if widget is self:
                continue
            if widget.__class__.__name__ == "LauncherWindow":
                launcher = widget
                break
        if launcher is None:
            from scriborium.ui.launcher_window import LauncherWindow

            launcher = LauncherWindow()
        launcher.setGeometry(self.geometry())
        launcher.show()
        launcher.raise_()
        launcher.activateWindow()
        self.close()

    def _handle_autosave(self) -> None:
        if self.dirty:
            self._persist(trigger="autosave")

    def _persist(self, trigger: str) -> None:
        settings = self.settings_service.load()
        backup_dir = Path(settings.backup_dir)
        try:
            if trigger == "manual" and settings.create_backup_on_save and self.document.path.exists():
                self.backup_service.create_backup(self.document.path, backup_dir, "manual")
            if trigger == "autosave" and settings.create_backup_before_autosave and self.document.path.exists():
                self.backup_service.create_backup(self.document.path, backup_dir, "autosave")
            self.project_service.save_document(self.document)
            self.backup_service.cleanup(
                backup_dir=backup_dir,
                project_stem=self.document.path.stem,
                max_backups=max(1, settings.max_backups_per_project),
                delete_old=settings.delete_old_backups,
                max_age_days=max(1, settings.delete_backups_older_than_days),
            )
            if trigger == "autosave":
                self.recovery_service.update_autosave_snapshot(self.document.path)
            self.dirty = False
            self.recovery_service.mark_dirty(self.document.path, False)
            sel = self._current_selection()
            self.timeline.set_content(self.document.content)
            if sel and sel[0] == "scene":
                self.timeline.set_active_scene(sel[1][0], sel[1][1])
            else:
                self.timeline.set_active_scene(-1, -1)
            self.status.showMessage(
                self._msg("status_project_saved", "Project saved")
                if trigger == "manual"
                else self._msg("status_autosave_done", "Autosave completed"),
                2500,
            )
        except OSError as err:
            QMessageBox.critical(
                self,
                self._msg("msg_error", "Error"),
                self._msg("msg_save_project_failed", "Failed to save project:") + f"\n{err}",
            )



    def _on_timeline_plotline_change(self, chapter_index: int, scene_index: int, plotline_id: str) -> None:
        chapters = self.document.content.get("chapters", [])
        if chapter_index < 0 or chapter_index >= len(chapters):
            return
        scenes = chapters[chapter_index].get("scenes", [])
        if scene_index < 0 or scene_index >= len(scenes):
            return
        scene = scenes[scene_index]
        if scene.get("plotlineId") == plotline_id:
            return
        scene["plotlineId"] = plotline_id
        ids = list(scene.get("plotlineIds", []))
        if plotline_id not in ids:
            ids.insert(0, plotline_id)
        scene["plotlineIds"] = ids
        self._mark_dirty()
        self._refresh_timeline_preserving_focus(chapter_index, scene_index)
        # Update plotline combo if this scene is selected
        sel = self._current_selection()
        if sel and sel[0] == "scene" and sel[1] == (chapter_index, scene_index):
            self._fill_plotline_combo_for_scene(chapter_index, scene_index)
        self.status.showMessage(self._msg("status_scene_plotline_updated", "Scene plotline updated"), 2000)


    def _open_import_dialog(self) -> None:
        path, _ = QFileDialog.getOpenFileName(
            self,
            self._msg("import_file_title", "Import file"),
            "",
            self._msg("import_supported_formats", "Supported formats (*.txt *.md *.docx);;All files (*)"),
        )
        if not path:
            return

        from PySide6.QtWidgets import QRadioButton

        dlg = QDialog(self)
        dlg.setWindowTitle(self._msg("import_params_title", "Import parameters"))
        dlg.resize(360, 200)
        lay = QVBoxLayout(dlg)
        lay.addWidget(QLabel(self._msg("import_split_mode", "Split mode:")))
        rb_head = QRadioButton(self._msg("import_mode_headings", "By headings (H1/H2)"))
        rb_head.setChecked(True)
        rb_delim = QRadioButton(self._msg("import_mode_delimiter", "By delimiter ###"))
        rb_single = QRadioButton(self._msg("import_mode_single", "One big scene"))
        lay.addWidget(rb_head)
        lay.addWidget(rb_delim)
        lay.addWidget(rb_single)
        cb_entities = QCheckBox(self._msg("import_auto_character_cards", "Create character cards automatically"))
        lay.addWidget(cb_entities)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(dlg.accept)
        buttons.rejected.connect(dlg.reject)
        lay.addWidget(buttons)

        if dlg.exec() != QDialog.DialogCode.Accepted:
            return

        mode = "headings" if rb_head.isChecked() else ("delimiter" if rb_delim.isChecked() else "single")
        options = ImportOptions(split_mode=mode, create_entity_cards=cb_entities.isChecked())
        try:
            result = self.import_service.import_file(Path(path), options)
        except Exception as err:
            QMessageBox.critical(self, self._msg("msg_import_error", "Import error"), str(err))
            return

        answer = QMessageBox.question(
            self,
            self._msg("label_import", "Import"),
            self._msg(
                "import_summary_with_counts",
                "Imported scenes: {scenes}, chapters: {chapters}.\n\n{question}",
            ).format(
                scenes=sum(len(c.get("scenes", [])) for c in result.content["chapters"]),
                chapters=len(result.content["chapters"]),
                question=self._msg("import_replace_content_question", "Replace current project content?"),
            ),
        )
        if answer != QMessageBox.StandardButton.Yes:
            return

        self.document.content.update(result.content)
        self._ensure_base_structure()
        self._mark_dirty()
        self._load_from_document()
        self.status.showMessage(self._msg("status_import_done", "Import completed: {path}").format(path=path), 4000)

    def _import_from_file(self) -> None:
        # Backward-compatible alias used by menu actions.
        self._open_import_dialog()

    def _export_project(self, fmt: str) -> None:
        dialog = ExportDialog(self, self.document.path, self.document.content, self.export_service, fmt)
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return
        out = dialog.selected_output()
        if out is not None:
            self.status.showMessage(self._msg("status_export_done", "Export completed: {path}").format(path=out), 4000)

    def _show_editor_context_menu(self, pos) -> None:
        """Show context menu with spell suggestions when right-clicking a misspelled word."""
        menu = self.editor.createStandardContextMenu()
        cursor = self.editor.cursorForPosition(pos)
        word_info = self._live_spell_editor.word_under_cursor(cursor)
        if word_info:
            word, start, end = word_info
            if self._live_spell_editor.is_misspelled(word):
                block_pos = cursor.block().position()
                suggestions = self._live_spell_editor.suggestions(word)
                menu.addSeparator()
                if suggestions:
                    for sugg in suggestions[:16]:
                        action = menu.addAction(f"✏ {sugg}")
                        action.triggered.connect(
                            lambda checked=False, s=sugg, bp=block_pos, st=start, en=end: self._replace_word(bp, st, en, s)
                        )
                else:
                    menu.addAction(self._msg("spell_no_suggestions", "Нет вариантов — исправьте вручную")).setEnabled(False)
                menu.addSeparator()
                add_action = menu.addAction(self._msg("spell_add_to_dict", "➕ Add to dictionary"))
                add_action.triggered.connect(lambda: self._add_word_to_dictionary(word))
        menu.exec(self.editor.viewport().mapToGlobal(pos))

    def _replace_word(self, block_pos: int, start: int, end: int, replacement: str) -> None:
        """Replace a word inside a QTextBlock."""
        cursor = self.editor.textCursor()
        pos = block_pos + start
        cursor.setPosition(pos)
        cursor.setPosition(pos + (end - start), QTextCursor.MoveMode.KeepAnchor)
        cursor.insertText(replacement)
        self.editor.setTextCursor(cursor)
        self._mark_dirty()
        self._live_spell_editor.after_correction()

    def _add_word_to_dictionary(self, word: str) -> None:
        """Add a word to the custom dictionary and refresh highlighter."""
        lang = self.settings_service.load().language
        self.spellcheck_service.add_custom_word(lang, word)
        self._live_spell_editor.reload_dictionary()
        self.status.showMessage(self._msg("spell_word_added", "Word added to dictionary: {word}").format(word=word), 3000)

    def closeEvent(self, event) -> None:  # type: ignore[override]
        if self.dirty:
            self._persist(trigger="manual")
        self.recovery_service.end_session(self.document.path)
        super().closeEvent(event)




