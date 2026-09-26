from pathlib import Path


def package_root() -> Path:
    return Path(__file__).resolve().parents[1]


def resource_path(relative: str) -> Path:
    return package_root() / "resources" / relative
