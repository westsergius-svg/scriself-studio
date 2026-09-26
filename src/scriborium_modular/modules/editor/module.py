from __future__ import annotations

from dataclasses import dataclass

from scriborium_modular.modules.editor.window import EditorWindow


@dataclass
class EditorModule:
    id: str = "editor"
    name: str = "Editor"
    version: str = "1.0.0"

    def initialize(self, context) -> None:
        self._context = context

    def open_editor(self) -> None:
        project_model = self._context.project_service.create_demo_project()
        self.window = EditorWindow(self._context, project_model)
        self.window.show()
