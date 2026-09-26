from __future__ import annotations

from dataclasses import dataclass

from scriborium_modular.modules.launcher.window import LauncherWindow


@dataclass
class LauncherModule:
    id: str = "launcher"
    name: str = "Launcher"
    version: str = "1.0.0"

    def initialize(self, context) -> None:
        self._context = context
        self.window = LauncherWindow(context)

    def show(self) -> None:
        self.window.show()
