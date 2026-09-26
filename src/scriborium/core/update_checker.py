"""Проверка и скачивание обновлений Scriborium.

Порядок поиска обновления:
1) файл-манифест latest.json в каталоге (рекомендуется);
2) если его нет — список файлов в каталоге (автоиндекс сервера).
"""
from __future__ import annotations

import json
import re
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Optional

# Корневой каталог с инсталляторами.
UPDATE_BASE_URL = "https://scriborium.ru/uploads/scrisoft/"
# Имя манифеста в каталоге.
UPDATE_MANIFEST_NAME = "latest.json"

# Имена, которые не являются полными дистрибутивами.
_SKIP_TOKENS = ("modular", "lite", "mini", "legacy")


@dataclass
class UpdateInfo:
    version: tuple  # числовые части (major, minor, ...)
    file_name: str
    url: str
    notes: str = ""
    source: str = "manifest"
    display: str = ""

    def __post_init__(self) -> None:
        if not self.display:
            self.display = ".".join(str(x) for x in self.version)


def parse_version(text: object) -> tuple:
    """Извлекает версию '1.2.3.4' из любой строки."""
    raw = str(text or "")
    m = re.search(r"(\d+(?:\.\d+)+)", raw)
    if not m:
        return ()
    return tuple(int(x) for x in m.group(1).split("."))


def compare_versions(a: tuple, b: tuple) -> int:
    """-1/0/1: a < b, a == b, a > b. Отсутствующие части считаем 0."""
    if not a and not b:
        return 0
    if not a:
        return -1
    if not b:
        return 1
    length = max(len(a), len(b))
    a2 = a + (0,) * (length - len(a))
    b2 = b + (0,) * (length - len(b))
    if a2 < b2:
        return -1
    if a2 > b2:
        return 1
    return 0


def is_newer(candidate: tuple, current: tuple) -> bool:
    return compare_versions(candidate, current) > 0


def current_version() -> tuple:
    from scriborium.core.version import APP_VERSION

    return parse_version(APP_VERSION)


def _request(url: str, method: str = "GET", timeout: int = 25):
    req = urllib.request.Request(
        url,
        method=method,
        headers={
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ScriboriumUpdater/1.0",
            "Accept": "*/*",
        },
    )
    return urllib.request.urlopen(req, timeout=timeout)


def _read_bytes(url: str, timeout: int = 25) -> bytes:
    with _request(url, timeout=timeout) as resp:
        return resp.read()


def _join_url(base: str, file_name: str) -> str:
    if "://" in file_name:
        return file_name
    base = base.rstrip("/") + "/"
    if "%" in file_name:
        # Название уже URL-закодировано (u0438з списка файлов).
        return base + file_name.lstrip("/")
    return base + urllib.parse.quote(file_name)


def fetch_manifest(base_url: str = UPDATE_BASE_URL, timeout: int = 25) -> Optional[UpdateInfo]:
    """Читает latest.json в каталоге."""
    manifest_url = _join_url(base_url, UPDATE_MANIFEST_NAME)
    try:
        data = _read_bytes(manifest_url, timeout=timeout)
    except Exception:
        return None
    try:
        obj = json.loads(data.decode("utf-8", errors="replace"))
    except Exception:
        return None
    if not isinstance(obj, dict):
        return None
    version_text = obj.get("version") or obj.get("version_label")
    file_name = obj.get("file") or obj.get("file_name") or obj.get("setup")
    if not file_name:
        return None
    version = parse_version(version_text)
    if not version:
        version = parse_version(file_name)
    return UpdateInfo(
        version=version,
        file_name=str(file_name),
        url=_join_url(base_url, str(file_name)),
        notes=str(obj.get("notes") or ""),
        source="manifest",
        display=str(version_text or ""),
    )


def parse_listing(html: str, base_url: str = UPDATE_BASE_URL) -> list:
    """Разбирает HTML автоиндекса каталога."""
    results: list = []
    pattern = re.compile(r'href=["\']([^"\'#?]+?\.exe)["\']', re.IGNORECASE)
    for href in pattern.findall(html):
        name = urllib.parse.unquote(href.split("/")[-1])
        lower = name.lower()
        if not lower.startswith("scriborium-setup"):
            continue
        if any(tok in lower for tok in _SKIP_TOKENS):
            continue
        version = parse_version(name)
        if not version:
            continue
        results.append(
            UpdateInfo(
                version=version,
                file_name=name,
                url=_join_url(base_url, href) if "://" not in href else href,
                source="listing",
                display=name,
            )
        )
    return results


def fetch_listing(base_url: str = UPDATE_BASE_URL, timeout: int = 25) -> list:
    """Скачивает список файлов каталога (если доступен)."""
    url = base_url if base_url.endswith("/") else base_url + "/"
    html = _read_bytes(url, timeout=timeout).decode("utf-8", errors="replace")
    return parse_listing(html, base_url)


def newest_updates(infos: list) -> Optional[UpdateInfo]:
    if not infos:
        return None
    return max(infos, key=lambda i: i.version)


def find_latest(base_url: str = UPDATE_BASE_URL, timeout: int = 25):
    """Возвращает (latest|None, error_text|None)."""
    info = fetch_manifest(base_url, timeout=timeout)
    if info is not None:
        return info, None
    try:
        infos = fetch_listing(base_url, timeout=timeout)
    except Exception as exc:
        return None, "listing: " + str(exc)
    if not infos:
        return None, "empty"
    return newest_updates(infos), None


def download(url: str, dest: Path, progress_cb: Optional[Callable[[int, int], None]] = None) -> Path:
    """Стримингово скачивает файл в dest. Возвращает dest."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_name(dest.name + ".part")
    with _request(url, timeout=120) as resp:
        total = 0
        try:
            total = int(resp.headers.get("Content-Length") or 0)
        except (TypeError, ValueError):
            total = 0
        done = 0
        with open(tmp, "wb") as fh:
            while True:
                chunk = resp.read(65536)
                if not chunk:
                    break
                fh.write(chunk)
                done += len(chunk)
                if progress_cb is not None:
                    progress_cb(done, total or 0)
    tmp.replace(dest)
    return dest