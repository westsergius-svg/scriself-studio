from pathlib import Path
import sys


def app_data_dir() -> Path:
    base = Path.home() / "AppData" / "Roaming"
    path = base / "Scriborium"
    path.mkdir(parents=True, exist_ok=True)
    return path


def settings_path() -> Path:
    return app_data_dir() / "settings.json"


def resources_root() -> Path:
    if getattr(sys, "frozen", False) and hasattr(sys, "_MEIPASS"):
        return Path(getattr(sys, "_MEIPASS")) / "scriborium" / "resources"
    return Path(__file__).resolve().parent.parent / "resources"


def app_icon_path() -> Path:
    return resources_root() / "icons" / "app.ico"
