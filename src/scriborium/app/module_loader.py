from __future__ import annotations

from typing import Any

from scriborium.modules.launcher.module import LauncherModule


class ModuleLoader:
    def __init__(self, context: Any) -> None:
        self._context = context
        self._modules: dict[str, Any] = {}

    def initialize_all(self) -> None:
        module = LauncherModule()
        module.initialize(self._context)
        self._modules[module.id] = module

    def get_module(self, module_id: str) -> Any:
        return self._modules[module_id]
