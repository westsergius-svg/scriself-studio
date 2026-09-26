from __future__ import annotations

import logging
import sys
from datetime import datetime
from pathlib import Path


def _log_dir() -> Path:
    """Определяет папку рядом с exe (или рабочей директорией при запуске из исходников)."""
    if getattr(sys, "frozen", False):
        # Запущено из PyInstaller bundle
        base = Path(sys.executable).parent
    else:
        base = Path.cwd()
    log_dir = base / "logs"
    log_dir.mkdir(parents=True, exist_ok=True)
    return log_dir


def setup_logging() -> logging.Logger:
    """Настраивает логгер с записью в файл рядом с exe."""
    log_dir = _log_dir()
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    log_file = log_dir / f"scriborium_{timestamp}.log"

    logger = logging.getLogger("scriborium")
    logger.setLevel(logging.DEBUG)

    if logger.handlers:
        return logger

    file_handler = logging.FileHandler(log_file, encoding="utf-8")
    file_handler.setLevel(logging.DEBUG)
    file_formatter = logging.Formatter(
        "%(asctime)s [%(levelname)s] %(name)s - %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )
    file_handler.setFormatter(file_formatter)

    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setLevel(logging.INFO)
    console_formatter = logging.Formatter("%(levelname)s: %(message)s")
    console_handler.setFormatter(console_formatter)

    logger.addHandler(file_handler)
    logger.addHandler(console_handler)

    logger.info("Logging started. Log file: %s", log_file)
    return logger
