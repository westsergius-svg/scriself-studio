from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CHECK_EXTENSIONS = {".py", ".md", ".toml", ".json", ".txt", ".css", ".js", ".ps1", ".nsi"}
SKIP_DIRS = {".venv", ".git", "__pycache__", "dist", "build"}


def should_check(path: Path) -> bool:
    if path.suffix.lower() not in CHECK_EXTENSIONS:
        return False
    return not any(part in SKIP_DIRS for part in path.parts)


def is_utf8_without_bom(data: bytes) -> bool:
    if data.startswith(b"\xef\xbb\xbf"):
        return False
    try:
        data.decode("utf-8")
    except UnicodeDecodeError:
        return False
    return True


def main() -> int:
    failed: list[Path] = []
    for file_path in ROOT.rglob("*"):
        if not file_path.is_file() or not should_check(file_path):
            continue
        data = file_path.read_bytes()
        if not is_utf8_without_bom(data):
            failed.append(file_path)

    if failed:
        print("Encoding check failed. Files must be UTF-8 without BOM:")
        for path in failed:
            print(f" - {path}")
        return 1

    print("Encoding check passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
