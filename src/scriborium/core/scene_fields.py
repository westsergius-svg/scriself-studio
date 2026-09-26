from __future__ import annotations

# Значения в JSON (стабильные ключи)
STATUS_DRAFT = "draft"
STATUS_IN_PROGRESS = "in_progress"
STATUS_DONE = "done"

SCENE_STATUS_ORDER = (STATUS_DRAFT, STATUS_IN_PROGRESS, STATUS_DONE)

SCENE_STATUS_LABEL_RU: dict[str, str] = {
    STATUS_DRAFT: "Черновик",
    STATUS_IN_PROGRESS: "В работе",
    STATUS_DONE: "Готово",
}

# Цвет левой полосы карточки на таймлайне (ТЗ: цвет от статуса)
SCENE_STATUS_BORDER: dict[str, str] = {
    STATUS_DRAFT: "#64748b",
    STATUS_IN_PROGRESS: "#2563eb",
    STATUS_DONE: "#16a34a",
}

SCENE_STATUS_BG: dict[str, tuple[str, str]] = {
    STATUS_DRAFT: ("#f8fafc", "#e2e8f0"),
    STATUS_IN_PROGRESS: ("#eff6ff", "#dbeafe"),
    STATUS_DONE: ("#f0fdf4", "#dcfce7"),
}


def normalize_scene_status(raw: object) -> str:
    if isinstance(raw, str) and raw in SCENE_STATUS_LABEL_RU:
        return raw
    return STATUS_DRAFT


def default_scene_dict() -> dict[str, object]:
    """Поля новой сцены (дополняют остальные ключи в редакторе)."""
    return {
        "status": STATUS_DRAFT,
        "targetWordCount": 0,
    }
