from __future__ import annotations

from scriborium_modular.app.module_loader import ModuleLoader
from scriborium_modular.core.i18n_service import I18nService
from scriborium_modular.core.project_service import ProjectService
from scriborium_modular.core.settings_service import SettingsService
from scriborium_modular.shared.signals.event_bus import EventBus


class AppContext:
    def __init__(self) -> None:
        self.event_bus = EventBus()
        self.settings = SettingsService()
        self.i18n = I18nService(self.settings)
        self.project_service = ProjectService()
        self.module_loader = ModuleLoader(self)
