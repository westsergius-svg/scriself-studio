from __future__ import annotations

from typing import Any

from scriborium_modular.modules.editor.module import EditorModule
from scriborium_modular.modules.error_checker.module import ErrorCheckerModule
from scriborium_modular.modules.launcher.module import LauncherModule
from scriborium_modular.modules.timeline.module import TimelineModule


class ModuleLoader:
    def __init__(self, context: Any) -> None:
        self._context = context
        self._modules: dict[str, Any] = {}

    def initialize_all(self) -> None:
        for module_cls in (TimelineModule, ErrorCheckerModule, EditorModule, LauncherModule):
            module = module_cls()
            module.initialize(self._context)
            self._modules[module.id] = module

    def get_module(self, module_id: str) -> Any:
        return self._modules[module_id]
