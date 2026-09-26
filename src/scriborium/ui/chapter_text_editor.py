from __future__ import annotations

from collections.abc import Callable

from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QAction, QFont, QTextBlockFormat, QTextCursor, QTextListFormat
from PySide6.QtWidgets import (
    QColorDialog,
    QComboBox,
    QDialog,
    QFontComboBox,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QScrollArea,
    QSpinBox,
    QSplitter,
    QTextEdit,
    QToolBar,
    QVBoxLayout,
    QWidget,
)

from scriborium.core.spellcheck import SpellcheckService
from scriborium.ui.spell_highlighter import LiveSpellHighlighter

CHAPTER_TEXT_EDITOR_STYLESHEET = """
            QWidget#chapterTextEditorRoot {
                background: #f5ecd9;
            }
            QLabel#chapterTextHint, QLabel#chapterTextStatus {
                color: #5b4424;
                font-family: 'Segoe UI';
                font-size: 11pt;
                font-weight: 600;
            }
            QLabel#chapterTextStatus {
                color: #7b6646;
            }
            QToolBar {
                background: #fbf7ef;
                border: 1px solid #cdb893;
                border-radius: 8px;
                spacing: 6px;
                padding: 6px;
            }
            QToolButton {
                background: #fffdf8;
                color: #3c2b17;
                border: 1px solid #c8b08a;
                border-radius: 6px;
                padding: 6px 10px;
                font-family: 'Segoe UI';
                font-size: 10.5pt;
                font-weight: 600;
            }
            QToolButton:checked, QToolButton:hover {
                background: #ece0c8;
                border-color: #a98959;
            }
            QComboBox, QFontComboBox, QSpinBox {
                background: #fffdf8;
                color: #2d2114;
                border: 1px solid #c8b08a;
                border-radius: 6px;
                padding: 5px 8px;
                min-height: 30px;
            }
            QTextEdit {
                background: #fffefb;
                color: #1f1a15;
                border: 1px solid #c9b58e;
                border-radius: 10px;
                padding: 24px 30px;
                selection-background-color: #cfb98c;
                font-family: 'Times New Roman';
                font-size: 12pt;
            }
            QWidget#chapterScenePanel {
                background: #fbf7ef;
                border: 1px solid #cdb893;
                border-radius: 10px;
            }
            QLabel#chapterSceneTitle {
                color: #5b4424;
                font-family: 'Segoe UI';
                font-size: 11pt;
                font-weight: 700;
            }
            QLabel#chapterSceneHint {
                color: #6b5536;
                font-family: 'Segoe UI';
                font-size: 9.5pt;
            }
            QPushButton#chapterSceneButton {
                text-align: left;
                background: #fffdf8;
                color: #3c2b17;
                border: 1px solid #c8b08a;
                border-radius: 7px;
                padding: 6px 10px;
                font-family: 'Segoe UI';
                font-size: 10pt;
                font-weight: 600;
            }
            QPushButton#chapterSceneButton:hover, QPushButton#chapterSceneButton:checked {
                background: #ece0c8;
                border-color: #a98959;
            }
            QTextEdit#chapterSceneDetails {
                background: #fffdf8;
                border: 1px solid #c9b58e;
                border-radius: 8px;
                color: #2f2418;
                font-family: 'Segoe UI';
                font-size: 10pt;
                padding: 8px;
            }
            QPushButton#chapterTextAction {
                background: #fffaf0;
                color: #3d2e1d;
                border: 1px solid #baa074;
                border-radius: 8px;
                padding: 8px 14px;
                min-width: 140px;
                font-family: 'Segoe UI';
                font-size: 10.5pt;
                font-weight: 600;
            }
            QPushButton#chapterTextAction:hover {
                background: #efe1c4;
            }
            """


class ChapterTextEditorWidget(QWidget):
    close_requested = Signal()
    cancel_requested = Signal()

    def __init__(
        self,
        parent: QWidget | None,
        chapter_title: str,
        html_text: str,
        messages: dict[str, str] | None = None,
        on_text_changed: Callable[[str, str], None] | None = None,
        language_code: str = "en",
        spellcheck_service: SpellcheckService | None = None,
        spellcheck_enabled: bool = True,
        scene_context: list[dict[str, object]] | None = None,
        *,
        show_scene_panel: bool = True,
        show_footer_actions: bool = True,
        show_top_hint: bool = True,
    ) -> None:
        super().__init__(parent)
        self._messages = messages or {}
        self._on_text_changed = on_text_changed
        self._language_code = language_code
        self._spellcheck_service = spellcheck_service or SpellcheckService()
        self._scene_context = list(scene_context or [])
        self._scene_buttons: list[QPushButton] = []
        self._show_scene_panel = show_scene_panel

        self.setObjectName("chapterTextEditorRoot")
        self.setStyleSheet(CHAPTER_TEXT_EDITOR_STYLESHEET)

        root = QVBoxLayout(self)
        root.setContentsMargins(14, 14, 14, 14)
        root.setSpacing(10)

        self.status_label = QLabel(self._msg("chapter_text_status_live", "Autosave enabled"))
        self.status_label.setObjectName("chapterTextStatus")
        if show_top_hint:
            top = QHBoxLayout()
            hint = QLabel(
                self._msg(
                    "free_chapter_editor_hint",
                    "Free chapter editor: continuous chapter text, separate from the project scene editor.",
                )
            )
            hint.setObjectName("chapterTextHint")
            top.addWidget(hint)
            top.addStretch(1)
            top.addWidget(self.status_label)
            root.addLayout(top)
        else:
            top = QHBoxLayout()
            top.addStretch(1)
            top.addWidget(self.status_label)
            root.addLayout(top)

        self.toolbar = QToolBar()
        self.toolbar.setMovable(False)
        self.toolbar.setFloatable(False)
        self.toolbar.setToolButtonStyle(Qt.ToolButtonStyle.ToolButtonTextOnly)
        root.addWidget(self.toolbar)

        self.editor = QTextEdit()
        self.editor.setAcceptRichText(True)
        self.editor.setHtml(html_text or "")
        self.editor.setPlaceholderText(chapter_title)
        content_splitter = QSplitter(Qt.Orientation.Horizontal)
        content_splitter.setChildrenCollapsible(False)

        content_splitter.addWidget(self.editor)
        if show_scene_panel:
            content_splitter.addWidget(self._build_scene_panel())
            content_splitter.setStretchFactor(0, 4)
            content_splitter.setStretchFactor(1, 2)
            content_splitter.setSizes([860, 340])
        else:
            content_splitter.setStretchFactor(0, 1)
            content_splitter.setSizes([1200])
        root.addWidget(content_splitter, 1)
        self._live_spell = LiveSpellHighlighter(
            self.editor.document(),
            language_getter=lambda: self._language_code,
            spellcheck_service=self._spellcheck_service,
        )
        self._live_spell.set_enabled(spellcheck_enabled)
        self._live_spell.rehighlight()
        self.editor.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.editor.customContextMenuRequested.connect(self._show_context_menu)

        self._build_toolbar()

        self.editor.textChanged.connect(self._handle_text_changed)
        self.editor.cursorPositionChanged.connect(self._sync_toolbar_state)
        self._sync_toolbar_state()

        if show_footer_actions:
            actions = QHBoxLayout()
            actions.addStretch(1)
            self.save_button = QPushButton(self._msg("editor_action_save", "Save"))
            self.save_button.setObjectName("chapterTextAction")
            self.save_button.clicked.connect(self._save_now)
            self.close_button = QPushButton(self._msg("button_close", "Close"))
            self.close_button.setObjectName("chapterTextAction")
            self.close_button.clicked.connect(self.close_requested.emit)
            self.cancel_button = QPushButton(self._msg("btn_cancel", "Cancel"))
            self.cancel_button.setObjectName("chapterTextAction")
            self.cancel_button.clicked.connect(self.cancel_requested.emit)
            actions.addWidget(self.save_button)
            actions.addWidget(self.close_button)
            actions.addWidget(self.cancel_button)
            root.addLayout(actions)
        if self._scene_context and show_scene_panel:
            self._select_scene(0)

    def _msg(self, key: str, default: str) -> str:
        return str(self._messages.get(key, default))

    def _build_toolbar(self) -> None:
        self.save_act = QAction(self._msg("editor_action_save", "Save"), self)
        self.save_act.triggered.connect(self._save_now)
        self.toolbar.addAction(self.save_act)

        self.fullscreen_act = QAction(self._msg("button_fullscreen", "Fullscreen"), self)
        self.fullscreen_act.triggered.connect(self._toggle_fullscreen)
        self.toolbar.addAction(self.fullscreen_act)
        self.toolbar.addSeparator()

        self.undo_act = QAction(self._msg("button_undo", "Undo"), self)
        self.undo_act.triggered.connect(self.editor.undo)
        self.toolbar.addAction(self.undo_act)

        self.redo_act = QAction(self._msg("button_redo", "Redo"), self)
        self.redo_act.triggered.connect(self.editor.redo)
        self.toolbar.addAction(self.redo_act)
        self.toolbar.addSeparator()

        self.style_combo = QComboBox()
        self.style_combo.addItem(self._msg("format_style_normal", "Normal text"), "normal")
        self.style_combo.addItem("H1", "h1")
        self.style_combo.addItem("H2", "h2")
        self.style_combo.addItem("H3", "h3")
        self.style_combo.currentIndexChanged.connect(self._apply_style_preset)
        self.toolbar.addWidget(self.style_combo)

        self.font_combo = QFontComboBox()
        self.font_combo.currentFontChanged.connect(self._set_font_family)
        self.toolbar.addWidget(self.font_combo)

        self.size_combo = QSpinBox()
        self.size_combo.setRange(8, 72)
        self.size_combo.setValue(12)
        self.size_combo.valueChanged.connect(self._set_font_size)
        self.toolbar.addWidget(self.size_combo)

        self.bold_act = QAction("B", self)
        self.bold_act.setToolTip(self._msg("format_bold", "Bold"))
        self.bold_act.setCheckable(True)
        self.bold_act.triggered.connect(self._toggle_bold)
        self.toolbar.addAction(self.bold_act)

        self.italic_act = QAction("I", self)
        self.italic_act.setToolTip(self._msg("format_italic", "Italic"))
        self.italic_act.setCheckable(True)
        self.italic_act.triggered.connect(self._toggle_italic)
        self.toolbar.addAction(self.italic_act)

        self.underline_act = QAction("U", self)
        self.underline_act.setToolTip(self._msg("format_underline", "Underline"))
        self.underline_act.setCheckable(True)
        self.underline_act.triggered.connect(self._toggle_underline)
        self.toolbar.addAction(self.underline_act)

        self.color_act = QAction(self._msg("button_text_color", "Text color"), self)
        self.color_act.triggered.connect(self._set_text_color)
        self.toolbar.addAction(self.color_act)
        self.toolbar.addSeparator()

        self.align_combo = QComboBox()
        self.align_combo.addItem(self._msg("format_align_left", "Left"), Qt.AlignmentFlag.AlignLeft)
        self.align_combo.addItem(self._msg("format_align_center", "Center"), Qt.AlignmentFlag.AlignHCenter)
        self.align_combo.addItem(self._msg("format_align_right", "Right"), Qt.AlignmentFlag.AlignRight)
        self.align_combo.addItem(self._msg("format_align_justify", "Justify"), Qt.AlignmentFlag.AlignJustify)
        self.align_combo.currentIndexChanged.connect(self._set_alignment)
        self.toolbar.addWidget(self.align_combo)

        self.bullets_act = QAction(self._msg("format_bulleted_list", "Bulleted list"), self)
        self.bullets_act.triggered.connect(lambda: self._toggle_list(False))
        self.toolbar.addAction(self.bullets_act)

        self.numbered_act = QAction(self._msg("format_numbered_list", "Numbered list"), self)
        self.numbered_act.triggered.connect(lambda: self._toggle_list(True))
        self.toolbar.addAction(self.numbered_act)

    def _build_scene_panel(self) -> QWidget:
        panel = QWidget()
        panel.setObjectName("chapterScenePanel")
        panel.setMinimumWidth(300)

        layout = QVBoxLayout(panel)
        layout.setContentsMargins(10, 10, 10, 10)
        layout.setSpacing(8)

        title = QLabel(self._msg("chapter_scene_panel_title", "Scenes in chapter"))
        title.setObjectName("chapterSceneTitle")
        hint = QLabel(self._msg("chapter_scene_panel_hint", "Pick a scene to view full details below."))
        hint.setObjectName("chapterSceneHint")
        hint.setWordWrap(True)
        layout.addWidget(title)
        layout.addWidget(hint)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        buttons_holder = QWidget()
        self._scene_buttons_layout = QVBoxLayout(buttons_holder)
        self._scene_buttons_layout.setContentsMargins(0, 0, 0, 0)
        self._scene_buttons_layout.setSpacing(6)

        if not self._scene_context:
            empty = QLabel(self._msg("chapter_scene_panel_empty", "No scenes in this chapter yet."))
            empty.setWordWrap(True)
            empty.setObjectName("chapterSceneHint")
            self._scene_buttons_layout.addWidget(empty)
        else:
            for index, scene in enumerate(self._scene_context):
                scene_title = str(scene.get("title") or "")
                button = QPushButton(f"{index + 1}. {scene_title}")
                button.setObjectName("chapterSceneButton")
                button.setCheckable(True)
                button.clicked.connect(lambda _checked=False, idx=index: self._select_scene(idx))
                self._scene_buttons.append(button)
                self._scene_buttons_layout.addWidget(button)

        self._scene_buttons_layout.addStretch(1)
        scroll.setWidget(buttons_holder)
        layout.addWidget(scroll, 1)

        details_title = QLabel(self._msg("chapter_scene_details_title", "Scene details"))
        details_title.setObjectName("chapterSceneTitle")
        layout.addWidget(details_title)

        self.scene_details = QTextEdit()
        self.scene_details.setObjectName("chapterSceneDetails")
        self.scene_details.setReadOnly(True)
        self.scene_details.setHtml(
            f"<p>{self._msg('chapter_scene_panel_empty', 'No scenes in this chapter yet.')}</p>"
        )
        layout.addWidget(self.scene_details, 2)

        return panel

    def _select_scene(self, index: int) -> None:
        if index < 0 or index >= len(self._scene_context):
            return
        for btn_index, button in enumerate(self._scene_buttons):
            button.setChecked(btn_index == index)
        self.scene_details.setHtml(self._scene_details_html(self._scene_context[index]))

    def _scene_details_html(self, scene: dict[str, object]) -> str:
        def value_text(value: object) -> str:
            text = str(value or "").strip()
            return text if text else "—"

        def list_text(values: object) -> str:
            if isinstance(values, list):
                cleaned = [str(v).strip() for v in values if str(v).strip()]
                if cleaned:
                    return ", ".join(cleaned)
            return "—"

        rows = [
            (
                self._msg("scene_label_status", "Status").rstrip(":"),
                value_text(scene.get("statusLabel") or scene.get("status")),
            ),
            (
                self._msg("scene_label_datetime", "Date/Time").rstrip(":"),
                value_text(scene.get("dateTime") or scene.get("datetime")),
            ),
            (
                self._msg("label_characters", "Characters"),
                list_text(scene.get("characters")),
            ),
            (
                self._msg("label_locations", "Locations"),
                list_text(scene.get("locations")),
            ),
            (
                self._msg("label_objects", "Objects"),
                list_text(scene.get("objects")),
            ),
            (
                self._msg("label_plotlines", "Plotlines"),
                list_text(scene.get("plotlines")),
            ),
            (
                self._msg("scene_label_goal", "Goal").rstrip(":"),
                value_text(scene.get("goal")),
            ),
            (
                self._msg("scene_label_conflict", "Conflict").rstrip(":"),
                value_text(scene.get("conflict")),
            ),
            (
                self._msg("scene_label_result", "Result").rstrip(":"),
                value_text(scene.get("result")),
            ),
            (
                self._msg("entity_label_description", "Detailed description").rstrip(":"),
                value_text(scene.get("description") or scene.get("synopsis")),
            ),
            (
                self._msg("entity_label_notes", "Notes").rstrip(":"),
                value_text(scene.get("notes")),
            ),
            (
                self._msg("chapter_scene_label_text", "Scene text").rstrip(":"),
                value_text(scene.get("text")),
            ),
        ]
        html_rows = "".join(
            f"<p><b>{label}:</b><br>{text.replace(chr(10), '<br>')}</p>" for label, text in rows
        )
        return html_rows

    def _merge_char_format(self, mutator: Callable[[QFont], None]) -> None:
        cursor = self.editor.textCursor()
        fmt = cursor.charFormat()
        font = fmt.font()
        mutator(font)
        fmt.setFont(font)
        cursor.mergeCharFormat(fmt)
        self.editor.mergeCurrentCharFormat(fmt)

    def _set_font_family(self, font: QFont) -> None:
        self._merge_char_format(lambda current: current.setFamily(font.family()))

    def _set_font_size(self, size: int) -> None:
        self._merge_char_format(lambda current: current.setPointSize(size))

    def _toggle_bold(self, _checked: bool = False) -> None:
        self._merge_char_format(lambda current: current.setBold(self.bold_act.isChecked()))

    def _toggle_italic(self, _checked: bool = False) -> None:
        self._merge_char_format(lambda current: current.setItalic(self.italic_act.isChecked()))

    def _toggle_underline(self, _checked: bool = False) -> None:
        self._merge_char_format(lambda current: current.setUnderline(self.underline_act.isChecked()))

    def _set_text_color(self) -> None:
        color = QColorDialog.getColor(parent=self)
        if not color.isValid():
            return
        fmt = self.editor.currentCharFormat()
        fmt.setForeground(color)
        self.editor.textCursor().mergeCharFormat(fmt)
        self.editor.mergeCurrentCharFormat(fmt)

    def _set_alignment(self, _index: int = -1) -> None:
        align = self.align_combo.currentData()
        if align is not None:
            self.editor.setAlignment(Qt.Alignment(align))

    def _toggle_list(self, numbered: bool) -> None:
        cursor = self.editor.textCursor()
        if cursor.currentList() is not None:
            block_fmt = cursor.blockFormat()
            block_fmt.setObjectIndex(-1)
            cursor.setBlockFormat(block_fmt)
            self.editor.setTextCursor(cursor)
            return
        list_fmt = QTextListFormat()
        list_fmt.setStyle(QTextListFormat.Style.ListDecimal if numbered else QTextListFormat.Style.ListDisc)
        cursor.createList(list_fmt)
        self.editor.setTextCursor(cursor)

    def _apply_style_preset(self, _index: int = -1) -> None:
        preset = str(self.style_combo.currentData() or "normal")
        cursor = self.editor.textCursor()
        char_fmt = cursor.charFormat()
        block_fmt = QTextBlockFormat()
        font = char_fmt.font()

        if preset == "h1":
            font.setPointSize(22)
            font.setBold(True)
            block_fmt.setTopMargin(12)
            block_fmt.setBottomMargin(6)
        elif preset == "h2":
            font.setPointSize(18)
            font.setBold(True)
            block_fmt.setTopMargin(10)
            block_fmt.setBottomMargin(4)
        elif preset == "h3":
            font.setPointSize(15)
            font.setBold(True)
            block_fmt.setTopMargin(8)
            block_fmt.setBottomMargin(4)
        else:
            font.setPointSize(12)
            font.setBold(False)
            block_fmt.setTopMargin(0)
            block_fmt.setBottomMargin(0)

        char_fmt.setFont(font)
        cursor.mergeCharFormat(char_fmt)
        cursor.mergeBlockFormat(block_fmt)
        self.editor.mergeCurrentCharFormat(char_fmt)
        self.editor.setTextCursor(cursor)
        self._sync_toolbar_state()

    def _toggle_fullscreen(self) -> None:
        window = self.window()
        if window is None:
            return
        if window.isFullScreen():
            window.showNormal()
        else:
            window.showMaximized()

    def load_editor_content(self, chapter_title: str, html_text: str) -> None:
        self.editor.blockSignals(True)
        self.editor.setHtml(html_text or "")
        self.editor.setPlaceholderText(chapter_title)
        self.editor.blockSignals(False)
        self._live_spell.rehighlight()
        self._sync_toolbar_state()
        self.status_label.setText(self._msg("chapter_text_status_live", "Autosave enabled"))

    def _handle_text_changed(self) -> None:
        self.status_label.setText(self._msg("chapter_text_status_saved", "Saved"))
        if self._on_text_changed is not None:
            self._on_text_changed(self.html_text(), self.plain_text())

    def _save_now(self) -> None:
        if self._on_text_changed is not None:
            self._on_text_changed(self.html_text(), self.plain_text())
        self.status_label.setText(self._msg("chapter_text_status_saved", "Saved"))

    def _sync_toolbar_state(self) -> None:
        fmt = self.editor.currentCharFormat()
        font = fmt.font()

        self.bold_act.blockSignals(True)
        self.italic_act.blockSignals(True)
        self.underline_act.blockSignals(True)
        self.bold_act.setChecked(font.bold())
        self.italic_act.setChecked(font.italic())
        self.underline_act.setChecked(font.underline())
        self.bold_act.blockSignals(False)
        self.italic_act.blockSignals(False)
        self.underline_act.blockSignals(False)

        if font.pointSize() > 0:
            self.size_combo.blockSignals(True)
            self.size_combo.setValue(font.pointSize())
            self.size_combo.blockSignals(False)

    def html_text(self) -> str:
        return self.editor.toHtml()

    def plain_text(self) -> str:
        return self.editor.toPlainText()

    # ------------------------------------------------------------------
    # Spell-check context menu
    # ------------------------------------------------------------------
    def _show_context_menu(self, pos) -> None:
        menu = self.editor.createStandardContextMenu()
        cursor = self.editor.cursorForPosition(pos)
        word_info = self._live_spell.word_under_cursor(cursor)
        if word_info:
            word, start, end = word_info
            if self._live_spell.is_misspelled(word):
                block_pos = cursor.block().position()
                suggestions = self._live_spell.suggestions(word)
                menu.addSeparator()
                if suggestions:
                    for sugg in suggestions:
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
        cursor = self.editor.textCursor()
        pos = block_pos + start
        cursor.setPosition(pos)
        cursor.setPosition(pos + (end - start), QTextCursor.MoveMode.KeepAnchor)
        cursor.insertText(replacement)
        self.editor.setTextCursor(cursor)
        self._live_spell.after_correction()
        self._handle_text_changed()

    def _add_word_to_dictionary(self, word: str) -> None:
        if self._spellcheck_service is not None:
            self._spellcheck_service.add_custom_word(self._language_code, word)
            self._live_spell.reload_dictionary()


class ChapterTextEditorDialog(QDialog):
    def __init__(
        self,
        parent: QWidget,
        chapter_title: str,
        html_text: str,
        messages: dict[str, str] | None = None,
        on_text_changed: Callable[[str, str], None] | None = None,
        language_code: str = "en",
        spellcheck_service: SpellcheckService | None = None,
        spellcheck_enabled: bool = True,
        scene_context: list[dict[str, object]] | None = None,
    ) -> None:
        super().__init__(
            parent,
            Qt.WindowType.Window
            | Qt.WindowType.WindowTitleHint
            | Qt.WindowType.WindowSystemMenuHint
            | Qt.WindowType.WindowMinimizeButtonHint
            | Qt.WindowType.WindowMaximizeButtonHint
            | Qt.WindowType.WindowCloseButtonHint,
        )
        self._widget = ChapterTextEditorWidget(
            self,
            chapter_title=chapter_title,
            html_text=html_text,
            messages=messages,
            on_text_changed=on_text_changed,
            language_code=language_code,
            spellcheck_service=spellcheck_service,
            spellcheck_enabled=spellcheck_enabled,
            scene_context=scene_context,
            show_scene_panel=True,
            show_footer_actions=True,
            show_top_hint=True,
        )
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.addWidget(self._widget)
        self.setWindowTitle(
            str((messages or {}).get("free_chapter_editor_title", "Free chapter editor: {title}")).format(
                title=chapter_title
            )
        )
        self.resize(1220, 860)
        self._widget.close_requested.connect(self.accept)
        self._widget.cancel_requested.connect(self.reject)
