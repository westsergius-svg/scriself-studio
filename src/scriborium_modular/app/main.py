import sys

from PySide6.QtGui import QIcon
from PySide6.QtWidgets import QApplication

from scriborium_modular.app.app_context import AppContext
from scriborium_modular.core.paths import resource_path


def run() -> None:
    app = QApplication(sys.argv)
    app.setApplicationName("Scriborium Modular")

    icon = QIcon(str(resource_path("icons/app.ico")))
    if not icon.isNull():
        app.setWindowIcon(icon)

    context = AppContext()
    context.module_loader.initialize_all()

    launcher = context.module_loader.get_module("launcher")
    launcher.show()

    sys.exit(app.exec())
