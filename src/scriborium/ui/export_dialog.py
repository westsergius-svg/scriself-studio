from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QDialog,
    QFileDialog,
    QFormLayout,
    QHBoxLayout,
    QInputDialog,
    QLabel,
    QListWidget,
    QListWidgetItem,
    QMessageBox,
    QPushButton,
    QSpinBox,
    QDoubleSpinBox,
    QTextBrowser,
    QVBoxLayout,
    QWidget,
)

from scriborium.core.import_export import ExportOptions, ExportService, ExportTemplate, ExportTemplateService


class ExportDialog(QDialog):
    def __init__(
        self,
        parent: QWidget,
        document_path: Path,
        content: dict,
        export_service: ExportService,
        initial_format: str = "docx",
    ) -> None:
        super().__init__(parent)
        self._document_path = document_path
        self._content = content
        self._export_service = export_service
        self._template_service = ExportTemplateService()
        self._messages = getattr(parent, "messages", {}) if parent is not None else {}
        self._format_state = {fmt: (label, enabled, hint) for label, fmt, enabled, hint in export_service.available_formats()}
        self._selected_output: Path | None = None

        self.setWindowTitle(
            self._msg("export_window_title", "Export project - {name}").format(name=document_path.name)
        )
        self.resize(1080, 720)
        self.setObjectName("exportDialog")

        root = QHBoxLayout(self)
        left = QVBoxLayout()
        right = QVBoxLayout()
        root.addLayout(left, 1)
        root.addLayout(right, 1)

        self.template_combo = QComboBox()
        self.template_combo.currentIndexChanged.connect(self._apply_template_from_combo)
        left.addWidget(QLabel(self._msg("export_template_label", "Export template")))
        left.addWidget(self.template_combo)

        self.format_combo = QComboBox()
        for fmt, (label, enabled, hint) in self._format_state.items():
            suffix = "" if enabled else f" ({hint})"
            self.format_combo.addItem(f"{label}{suffix}", fmt)
            index = self.format_combo.count() - 1
            model = self.format_combo.model()
            item = model.item(index) if hasattr(model, "item") else None
            if item is not None:
                item.setEnabled(enabled)
        idx = max(0, self.format_combo.findData(initial_format if initial_format in self._format_state else "docx"))
        self.format_combo.setCurrentIndex(idx)
        self.format_combo.currentIndexChanged.connect(self._update_preview)
        left.addWidget(QLabel(self._msg("export_format_label", "Format")))
        left.addWidget(self.format_combo)

        self.include_preface = QCheckBox(self._msg("export_include_preface", "Include preface"))
        self.include_preface.setChecked(True)
        self.include_preface.toggled.connect(self._update_preview)
        left.addWidget(self.include_preface)

        self.include_synopsis = QCheckBox(self._msg("export_include_synopsis", "Include synopsis"))
        self.include_synopsis.setChecked(True)
        self.include_synopsis.toggled.connect(self._update_preview)
        left.addWidget(self.include_synopsis)

        self.include_scene_meta = QCheckBox(self._msg("export_include_scene_meta", "Show scene metadata"))
        self.include_scene_meta.setChecked(True)
        self.include_scene_meta.toggled.connect(self._update_preview)
        left.addWidget(self.include_scene_meta)

        self.include_plotlines = QCheckBox(self._msg("export_include_plotlines", "Show plotlines"))
        self.include_plotlines.setChecked(True)
        self.include_plotlines.toggled.connect(self._update_preview)
        left.addWidget(self.include_plotlines)

        self.include_chapter_notes = QCheckBox(self._msg("export_include_chapter_notes", "Include chapter notes"))
        self.include_chapter_notes.toggled.connect(self._update_preview)
        left.addWidget(self.include_chapter_notes)

        self.include_chapter_metadata = QCheckBox(
            self._msg("export_include_chapter_metadata", "Include chapter metadata (subtitle, epigraph, synopsis)")
        )
        self.include_chapter_metadata.setChecked(True)
        self.include_chapter_metadata.toggled.connect(self._update_preview)
        left.addWidget(self.include_chapter_metadata)

        self.include_book_metadata = QCheckBox(
            self._msg("export_include_book_metadata", "Include book metadata (author, genre, tone)")
        )
        self.include_book_metadata.setChecked(True)
        self.include_book_metadata.toggled.connect(self._update_preview)
        left.addWidget(self.include_book_metadata)

        left.addWidget(QLabel(self._msg("export_library_section_label", "Library appendix")))
        self.include_library_characters = QCheckBox(self._msg("export_include_library_characters", "Characters (full dossiers)"))
        self.include_library_characters.setChecked(True)
        self.include_library_characters.toggled.connect(self._update_preview)
        left.addWidget(self.include_library_characters)

        self.include_library_locations = QCheckBox(self._msg("export_include_library_locations", "Locations (full dossiers)"))
        self.include_library_locations.setChecked(True)
        self.include_library_locations.toggled.connect(self._update_preview)
        left.addWidget(self.include_library_locations)

        self.include_library_objects = QCheckBox(self._msg("export_include_library_objects", "Objects (full dossiers)"))
        self.include_library_objects.setChecked(True)
        self.include_library_objects.toggled.connect(self._update_preview)
        left.addWidget(self.include_library_objects)

        self.include_library_plotlines = QCheckBox(self._msg("export_include_library_plotlines", "Plotlines reference"))
        self.include_library_plotlines.setChecked(True)
        self.include_library_plotlines.toggled.connect(self._update_preview)
        left.addWidget(self.include_library_plotlines)

        self.include_toc = QCheckBox(self._msg("export_include_toc", "Add table of contents"))
        self.include_toc.setChecked(True)
        self.include_toc.toggled.connect(self._update_preview)
        left.addWidget(self.include_toc)

        self.include_header_footer = QCheckBox(self._msg("export_include_header_footer", "Add header/footer"))
        self.include_header_footer.toggled.connect(self._update_preview)
        left.addWidget(self.include_header_footer)

        self.include_page_numbers = QCheckBox(self._msg("export_include_page_numbers", "Add page numbers"))
        self.include_page_numbers.toggled.connect(self._update_preview)
        left.addWidget(self.include_page_numbers)

        self.character_arcs_combo = QComboBox()
        self.character_arcs_combo.addItem(self._msg("export_arcs_none", "Do not include arcs"), "none")
        self.character_arcs_combo.addItem(self._msg("export_arcs_summary", "Arc summary at start"), "summary")
        self.character_arcs_combo.addItem(self._msg("export_arcs_annotations", "Annotations before scenes"), "annotations")
        self.character_arcs_combo.currentIndexChanged.connect(self._update_preview)
        left.addWidget(QLabel(self._msg("export_character_arcs_label", "Character arcs")))
        left.addWidget(self.character_arcs_combo)

        form = QFormLayout()
        self.font_family_combo = QComboBox()
        for family in ("Times New Roman", "Georgia", "Cambria", "Arial", "Segoe UI"):
            self.font_family_combo.addItem(family)
        self.font_family_combo.currentIndexChanged.connect(self._update_preview)
        form.addRow(self._msg("export_font_label", "Font"), self.font_family_combo)

        self.font_size_spin = QSpinBox()
        self.font_size_spin.setRange(8, 24)
        self.font_size_spin.setValue(12)
        self.font_size_spin.valueChanged.connect(self._update_preview)
        form.addRow(self._msg("export_font_size_label", "Size"), self.font_size_spin)

        self.line_spacing_spin = QDoubleSpinBox()
        self.line_spacing_spin.setRange(1.0, 2.5)
        self.line_spacing_spin.setSingleStep(0.1)
        self.line_spacing_spin.setValue(1.3)
        self.line_spacing_spin.valueChanged.connect(self._update_preview)
        form.addRow(self._msg("export_line_spacing_label", "Line spacing"), self.line_spacing_spin)
        left.addLayout(form)

        left.addWidget(QLabel(self._msg("export_document_content_label", "Document content")))
        self.scene_list = QListWidget()
        self.scene_list.itemChanged.connect(self._update_preview)
        self._fill_scene_list()
        left.addWidget(self.scene_list, 1)

        buttons_row = QHBoxLayout()
        save_template_button = QPushButton(self._msg("export_save_template_button", "Save as template..."))
        save_template_button.clicked.connect(self._save_template)
        buttons_row.addWidget(save_template_button)
        clear_button = QPushButton(self._msg("export_select_all_button", "Select all"))
        clear_button.clicked.connect(self._select_all_scenes)
        buttons_row.addWidget(clear_button)
        left.addLayout(buttons_row)

        right.addWidget(QLabel(self._msg("export_preview_label", "Preview")))
        self.preview = QTextBrowser()
        self.preview.setObjectName("exportPreview")
        right.addWidget(self.preview, 1)

        self.status_label = QLabel("")
        self.status_label.setWordWrap(True)
        right.addWidget(self.status_label)

        action_row = QHBoxLayout()
        action_row.addStretch(1)
        export_button = QPushButton(self._msg("btn_export", "Export"))
        export_button.setObjectName("primaryActionButton")
        export_button.clicked.connect(self._export)
        action_row.addWidget(export_button)
        cancel_button = QPushButton(self._msg("btn_cancel", "Cancel"))
        cancel_button.clicked.connect(self.reject)
        action_row.addWidget(cancel_button)
        right.addLayout(action_row)

        self._reload_templates()
        self._update_preview()

    def _msg(self, key: str, default: str) -> str:
        return str(self._messages.get(key, default))

    def selected_output(self) -> Path | None:
        return self._selected_output

    def _fill_scene_list(self) -> None:
        self.scene_list.clear()
        for chapter in self._content.get("chapters", []):
            chapter_item = QListWidgetItem(chapter.get("title", self._msg("chapter_fallback", "Chapter")))
            chapter_item.setFlags(Qt.ItemFlag.ItemIsEnabled)
            chapter_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            self.scene_list.addItem(chapter_item)
            for scene in chapter.get("scenes", []):
                item = QListWidgetItem(f"  {scene.get('title', self._msg('scene_fallback', 'Scene'))}")
                item.setData(Qt.ItemDataRole.UserRole, scene.get("id"))
                item.setFlags(item.flags() | Qt.ItemFlag.ItemIsUserCheckable)
                item.setCheckState(Qt.CheckState.Checked)
                self.scene_list.addItem(item)

    def _select_all_scenes(self) -> None:
        for index in range(self.scene_list.count()):
            item = self.scene_list.item(index)
            if item.data(Qt.ItemDataRole.UserRole):
                item.setCheckState(Qt.CheckState.Checked)

    def _selected_scene_ids(self) -> list[str]:
        out: list[str] = []
        for index in range(self.scene_list.count()):
            item = self.scene_list.item(index)
            scene_id = item.data(Qt.ItemDataRole.UserRole)
            if scene_id and item.checkState() == Qt.CheckState.Checked:
                out.append(str(scene_id))
        return out

    def _current_format(self) -> str:
        return str(self.format_combo.currentData())

    def _current_options(self) -> ExportOptions:
        return ExportOptions(
            include_preface=self.include_preface.isChecked(),
            include_synopsis=self.include_synopsis.isChecked(),
            selected_scene_ids=self._selected_scene_ids(),
            include_scene_meta=self.include_scene_meta.isChecked(),
            include_plotlines=self.include_plotlines.isChecked(),
            include_chapter_notes=self.include_chapter_notes.isChecked(),
            include_chapter_metadata=self.include_chapter_metadata.isChecked(),
            include_book_metadata=self.include_book_metadata.isChecked(),
            include_library_characters=self.include_library_characters.isChecked(),
            include_library_locations=self.include_library_locations.isChecked(),
            include_library_objects=self.include_library_objects.isChecked(),
            include_library_plotlines=self.include_library_plotlines.isChecked(),
            include_character_arcs=str(self.character_arcs_combo.currentData()),
            include_toc=self.include_toc.isChecked(),
            include_header_footer=self.include_header_footer.isChecked(),
            include_page_numbers=self.include_page_numbers.isChecked(),
            font_family=self.font_family_combo.currentText(),
            font_size=int(self.font_size_spin.value()),
            line_spacing=float(self.line_spacing_spin.value()),
        )

    def _reload_templates(self) -> None:
        self.template_combo.blockSignals(True)
        self.template_combo.clear()
        self.template_combo.addItem(self._msg("export_no_template", "No template"), "")
        for template in self._template_service.load_all():
            self.template_combo.addItem(f"{template.name} ({template.fmt.upper()})", template.name)
        self.template_combo.blockSignals(False)

    def _apply_template_from_combo(self) -> None:
        template_name = self.template_combo.currentData()
        if not template_name:
            return
        for template in self._template_service.load_all():
            if template.name != template_name:
                continue
            fmt_index = self.format_combo.findData(template.fmt)
            if fmt_index >= 0:
                self.format_combo.setCurrentIndex(fmt_index)
            options = template.options
            self.include_preface.setChecked(options.include_preface)
            self.include_synopsis.setChecked(options.include_synopsis)
            self.include_scene_meta.setChecked(options.include_scene_meta)
            self.include_plotlines.setChecked(options.include_plotlines)
            self.include_chapter_notes.setChecked(options.include_chapter_notes)
            self.include_chapter_metadata.setChecked(options.include_chapter_metadata)
            self.include_book_metadata.setChecked(options.include_book_metadata)
            self.include_library_characters.setChecked(options.include_library_characters)
            self.include_library_locations.setChecked(options.include_library_locations)
            self.include_library_objects.setChecked(options.include_library_objects)
            self.include_library_plotlines.setChecked(options.include_library_plotlines)
            arc_index = self.character_arcs_combo.findData(options.include_character_arcs)
            if arc_index >= 0:
                self.character_arcs_combo.setCurrentIndex(arc_index)
            self.include_toc.setChecked(options.include_toc)
            self.include_header_footer.setChecked(options.include_header_footer)
            self.include_page_numbers.setChecked(options.include_page_numbers)
            font_index = self.font_family_combo.findText(options.font_family)
            if font_index >= 0:
                self.font_family_combo.setCurrentIndex(font_index)
            self.font_size_spin.setValue(options.font_size)
            self.line_spacing_spin.setValue(options.line_spacing)
            selected = set(options.selected_scene_ids)
            for index in range(self.scene_list.count()):
                item = self.scene_list.item(index)
                scene_id = item.data(Qt.ItemDataRole.UserRole)
                if not scene_id:
                    continue
                state = Qt.CheckState.Checked if not selected or scene_id in selected else Qt.CheckState.Unchecked
                item.setCheckState(state)
            self._update_preview()
            return

    def _save_template(self) -> None:
        name, ok = QInputDialog.getText(
            self,
            self._msg("export_template_dialog_title", "Export template"),
            self._msg("export_template_name_prompt", "Template name:"),
        )
        if not ok or not name.strip():
            return
        template = ExportTemplate(name=name.strip(), fmt=self._current_format(), options=self._current_options())
        self._template_service.save_template(template)
        self._reload_templates()
        index = self.template_combo.findData(template.name)
        if index >= 0:
            self.template_combo.setCurrentIndex(index)

    def _update_preview(self) -> None:
        fmt = self._current_format()
        label, enabled, hint = self._format_state.get(fmt, (fmt.upper(), True, ""))
        self.status_label.setText(
            ""
            if enabled
            else self._msg("export_format_unavailable", "{label} temporarily unavailable: {hint}").format(
                label=label, hint=hint
            )
        )
        try:
            html_preview = self._export_service.render_preview(self._content, fmt, self._current_options())
        except Exception as err:
            self.preview.setPlainText(str(err))
            return
        self.preview.setHtml(html_preview)

    def _export(self) -> None:
        fmt = self._current_format()
        label, enabled, hint = self._format_state.get(fmt, (fmt.upper(), True, ""))
        if not enabled:
            QMessageBox.warning(
                self,
                self._msg("export_unavailable_title", "Export unavailable"),
                self._msg("export_unavailable_message", "{label} is unavailable: {hint}").format(
                    label=label, hint=hint
                ),
            )
            return
        path, _ = QFileDialog.getSaveFileName(
            self,
            self._msg("export_save_dialog_title", "Export project"),
            self._document_path.stem + f".{fmt}",
            {
                "txt": "TXT (*.txt)",
                "html": "HTML (*.html)",
                "docx": "Word (*.docx)",
                "pdf": "PDF (*.pdf)",
            }.get(fmt, "All files (*)"),
        )
        if not path:
            return
        target = Path(path)
        if target.suffix.lower() != f".{fmt}":
            target = target.with_suffix(f".{fmt}")
        try:
            self._export_service.export_document(self._content, fmt, target, self._current_options())
        except Exception as err:
            QMessageBox.critical(self, self._msg("export_error_title", "Export error"), str(err))
            return
        self._selected_output = target
        self.accept()
