import sys
import traceback

from PySide6.QtCore import Qt
from PySide6.QtGui import QIcon
from PySide6.QtWidgets import QApplication, QMessageBox

from scriborium.app.app_context import AppContext
from scriborium.core.app_logger import setup_logging
from scriborium.core.paths import app_icon_path
from scriborium.core.spell_backend import warmup_languages


def _set_windows_app_user_model_id() -> None:
    """Задает AppUserModelID, чтобы в панели задач
    отображалась иконка приложения."""
    try:
        import ctypes

        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("Scriborium.Scriborium")
    except Exception:  # noqa: BLE001 — Windows-only, некритично
        pass


def main() -> int:
    logger = setup_logging()
    _set_windows_app_user_model_id()
    try:
        app = QApplication(sys.argv)
        app.setLayoutDirection(Qt.LayoutDirection.LeftToRight)

        icon_path = app_icon_path()
        if icon_path.exists():
            app.setWindowIcon(QIcon(str(icon_path)))

        logger.info("App starting...")
        warmup_languages("ru", "en", "de", "es", "fr")
        context = AppContext()
        context.module_loader.initialize_all()
        logger.info("Modules initialized.")

        launcher_module = context.module_loader.get_module("launcher")
        launcher_module.show()
        logger.info("Launcher shown.")

        return app.exec()
    except Exception as exc:
        logger.exception("Fatal error in main: %s", exc)
        # Показываем QMessageBox только если QApplication создан
        if QApplication.instance() is not None:
            QMessageBox.critical(
                None,
                "Scriborium — Critical Error",
                f"A fatal error occurred:\n{exc}\n\n"
                f"Details saved to log file.\n"
                f"Please check the logs folder next to the application.",
            )
        raise


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception:
        traceback.print_exc()
        raise SystemExit(1)
