from __future__ import annotations


def apply_scene_move(
    content: dict,
    from_chapter: int,
    from_scene: int,
    to_chapter: int,
    insert_at: int,
) -> tuple[int, int] | None:
    """
    Переносит сцену в другую позицию. insert_at — индекс вставки в целевой главе (0 = в начало).
    Возвращает (новая_глава, новый_индекс_сцены) или None при ошибке.
    """
    chapters = content.get("chapters")
    if not isinstance(chapters, list) or not chapters:
        return None
    n = len(chapters)
    if from_chapter < 0 or from_chapter >= n or to_chapter < 0 or to_chapter >= n:
        return None

    src_scenes = chapters[from_chapter].setdefault("scenes", [])
    if from_scene < 0 or from_scene >= len(src_scenes):
        return None

    moving = src_scenes.pop(from_scene)

    if from_chapter == to_chapter:
        cur = chapters[to_chapter]["scenes"]
        if from_scene < insert_at:
            insert_at -= 1
        insert_at = max(0, min(insert_at, len(cur)))
        cur.insert(insert_at, moving)
        return (to_chapter, insert_at)

    if not src_scenes:
        if len(chapters) <= 1:
            src_scenes.append(moving)
            return None
        chapters.pop(from_chapter)
        if to_chapter > from_chapter:
            to_chapter -= 1

    dst_scenes = chapters[to_chapter].setdefault("scenes", [])
    insert_at = max(0, min(insert_at, len(dst_scenes)))
    dst_scenes.insert(insert_at, moving)
    return (to_chapter, insert_at)
