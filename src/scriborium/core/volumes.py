"""Поддержка многотомника (тома) в структуре проекта.

Структура контента:
    content["book"]      — сведения о серии / произведении в целом
                           {title, synopsis, preface, ...}
    content["volumes"]   — список томов
                           [{id, number, title, synopsis}]
    content["chapters"]  — по-прежнему плоский упорядоченный список глав.
                           Каждая глава дополнительно несёт "volumeId" —
                           id тома, которому она принадлежит.
"""

from __future__ import annotations

_DEFAULT_VOLUME_ID = "volume_001"


def default_volume(book: dict | None = None, fallback_title: str = "Книга 1") -> dict:
    """Том по умолчанию для старых/одиночных проектов."""
    title = fallback_title
    if isinstance(book, dict):
        book_title = str(book.get("title") or "").strip()
        if book_title:
            title = book_title
    return {
        "id": _DEFAULT_VOLUME_ID,
        "number": 1,
        "title": title,
        "synopsis": "",
    }


def ensure_volumes(content: dict, fallback_title: str = "Книга 1") -> list[dict]:
    """Гарантирует наличие volumes и присваивает volumeId каждой главе.

    Если проекта тома не было (старый формат) — создаётся один том по умолчанию,
    в который помещаются все главы. Функция изменяет content на месте и
    возвращает упорядоченный список томов.
    """
    book = content.get("book", {})
    volumes = content.get("volumes")
    if not isinstance(volumes, list) or not volumes:
        volumes = [default_volume(book, fallback_title)]
        content["volumes"] = volumes

    # Нормализуем том: заполняем обязательные поля.
    for index, volume in enumerate(volumes, start=1):
        if not isinstance(volume, dict):
            volume = {}
            volumes[index - 1] = volume
        volume.setdefault("id", _new_volume_id(volumes))
        volume.setdefault("number", index)
        volume.setdefault("title", fallback_title)
        volume.setdefault("synopsis", "")

    first_volume_id = volumes[0]["id"]
    volume_ids = {v["id"] for v in volumes}
    for chapter in content.get("chapters", []):
        if not isinstance(chapter, dict):
            continue
        vid = chapter.get("volumeId")
        if vid not in volume_ids:
            chapter["volumeId"] = first_volume_id
    return volumes


def get_volumes(content: dict, fallback_title: str = "Книга 1") -> list[dict]:
    """Упорядоченный список томов (после нормализации)."""
    ensure_volumes(content, fallback_title)
    return content["volumes"]


def new_volume(content: dict, title: str = "", fallback_title: str = "Книга") -> dict:
    """Добавляет новый том в конец и возвращает его."""
    volumes = ensure_volumes(content, fallback_title)
    number = max((int(v.get("number") or 0) for v in volumes), default=0) + 1
    volume = {
        "id": _new_volume_id(volumes),
        "number": number,
        "title": title.strip() or f"{fallback_title} {number}",
        "synopsis": "",
    }
    volumes.append(volume)
    return volume


def remove_volume(content: dict, volume_id: str) -> bool:
    """Удаляет том и переносит его главы в первый оставшийся том.

    Возвращает True, если том удалён. Последний том удалить нельзя.
    """
    volumes = ensure_volumes(content)
    if len(volumes) <= 1:
        return False
    index = next((i for i, v in enumerate(volumes) if v.get("id") == volume_id), None)
    if index is None:
        return False
    volumes.pop(index)
    first_volume_id = volumes[0]["id"]
    for chapter in content.get("chapters", []):
        if isinstance(chapter, dict) and chapter.get("volumeId") == volume_id:
            chapter["volumeId"] = first_volume_id
    return True


def volume_label(volume: dict) -> str:
    """Короткая подпись тома, напр. «Том 1 · Название»."""
    number = int(volume.get("number") or 0)
    title = str(volume.get("title") or "").strip()
    if number and title:
        return f"Том {number} · {title}"
    if number:
        return f"Том {number}"
    return title or "Том"


def chapters_of(content: dict, volume_id: str) -> list[dict]:
    """Список глав указанного тома (в общем порядке глава за главой)."""
    ensure_volumes(content)
    return [ch for ch in content.get("chapters", []) if ch.get("volumeId") == volume_id]


def chapters_of_indexed(content: dict, volume_id: str) -> list[tuple[int, dict]]:
    """Пары (индекс главы в общем списке, глава) для тома."""
    ensure_volumes(content)
    return [(i, ch) for i, ch in enumerate(content.get("chapters", [])) if ch.get("volumeId") == volume_id]


def volume_of_chapter(chapter: dict, content: dict) -> dict | None:
    """Возвращает том, которому принадлежит глава (или первый том)."""
    volumes = ensure_volumes(content)
    vid = chapter.get("volumeId")
    for volume in volumes:
        if volume.get("id") == vid:
            return volume
    return volumes[0] if volumes else None


def iter_volumes_with_chapters(content: dict):
    """Идёт по томам в их порядке, отдаёт (volume, [chapters]) для каждого."""
    volumes = ensure_volumes(content)
    out: list[tuple[dict, list[dict]]] = []
    for volume in volumes:
        out.append((volume, chapters_of(content, volume["id"])))
    return out


def assign_chapter_volume(content: dict, chapter_index: int, volume_id: str) -> None:
    """Присваивает главе (по индексу) заданный том."""
    ensure_volumes(content)
    chapters = content.get("chapters", [])
    if 0 <= chapter_index < len(chapters):
        chapters[chapter_index]["volumeId"] = volume_id


def _new_volume_id(volumes: list[dict]) -> str:
    used = {v.get("id", "") for v in volumes}
    n = len(volumes) + 1
    while f"volume_{n:03d}" in used:
        n += 1
    return f"volume_{n:03d}"

