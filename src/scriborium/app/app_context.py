from __future__ import annotations

from scriborium.app.module_loader import ModuleLoader


class AppContext:
    def __init__(self) -> None:
        self.module_loader = ModuleLoader(self)
