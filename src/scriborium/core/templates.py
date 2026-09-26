from __future__ import annotations


TEMPLATE_GROUPS: dict[str, list[str]] = {
    "ХУДОЖЕСТВЕННАЯ ПРОЗА": [
        "Роман (базовый)",
        "Роман (путь героя)",
        "Роман (Save the Cat)",
        "Повесть / Рассказ",
        "Детектив",
        "Любовный роман",
        "Хоррор / Триллер",
        "Фэнтези / Научная фантастика",
    ],
    "ПОЭЗИЯ И ФОЛЬКЛОР": [
        "Сборник стихов",
        "Народная сказка (как Колобок)",
        "Волшебная сказка (Пропп)",
        "Басня",
    ],
    "СЦЕНАРИСТИКА": [
        "Сценарий полнометражного фильма",
        "Сценарий сериала (пилот)",
        "Сценарий ситкома",
        "Сценарий короткометражного фильма",
        "Сценарий видеоигры",
    ],
    "НОН-ФИКШН И ПРОЧЕЕ": [
        "Научно-популярная книга",
        "Автобиография / Мемуары",
        "Настольная ролевая игра (приключение)",
        "Библия вселенной (для серии)",
    ],
}


def all_templates() -> list[str]:
    items: list[str] = []
    for values in TEMPLATE_GROUPS.values():
        items.extend(values)
    return items


def build_project_content(name: str, template: str) -> dict:
    from scriborium.core.volumes import ensure_volumes

    template_key = (template or "").strip().lower()
    title = name or "Новый проект"

    if not template_key:
        content = _blank_content(title)
    elif "стих" in template_key or "поэз" in template_key:
        content = _poetry_content(title)
    elif "нон-фик" in template_key or "научно-популяр" in template_key or "мемуар" in template_key:
        content = _nonfiction_content(title)
    elif "сценар" in template_key or "пилот" in template_key or "ситком" in template_key:
        content = _screenplay_content(title)
    elif "повесть" in template_key or "рассказ" in template_key or "басня" in template_key:
        content = _short_story_content(title)
    elif "сказк" in template_key:
        content = _fairy_tale_content(title)
    elif "роман" in template_key or "детектив" in template_key or "хоррор" in template_key or "фэнтези" in template_key:
        content = _novel_content(title)
    else:
        content = _blank_content(title)
    # Новый проект всегда начинается как минимум с одного тома (Книга 1).
    ensure_volumes(content, fallback_title=title)
    return content


def _blank_content(title: str) -> dict:
    return {
        "book": {
            "title": title,
            "synopsis": "",
            "preface": "",
        },
        "chapters": [
            _chapter(
                "chapter_001",
                "Глава 1",
                [
                    _scene("scene_001", "Сцена 1"),
                ],
            )
        ],
        "library": {
            "characters": [],
            "locations": [],
            "objects": [],
        },
        "timeline": {
            "plotlines": [_plotline()],
        },
    }


def _novel_content(title: str) -> dict:
    return {
        "book": {
            "title": title,
            "synopsis": "История с несколькими сюжетными поворотами, героями и развитием конфликтов.",
            "preface": "",
            "genre": "Роман",
        },
        "chapters": [
            _chapter("chapter_001", "Глава 1. Завязка", [_scene("scene_001", "Открывающая сцена"), _scene("scene_002", "Зов к переменам")]),
            _chapter("chapter_002", "Глава 2. Усложнение", [_scene("scene_003", "Первое препятствие"), _scene("scene_004", "Союзники и риски")]),
            _chapter("chapter_003", "Глава 3. Перелом", [_scene("scene_005", "Кризис"), _scene("scene_006", "Решение и выход")]),
        ],
        "library": {
            "characters": [
                _character("char_001", "Главный герой", "Протагонист"),
                _character("char_002", "Союзник", "Поддержка героя"),
                _character("char_003", "Антагонист", "Источник конфликта"),
            ],
            "locations": [
                _location("loc_001", "Дом героя", "Стартовая точка"),
                _location("loc_002", "Главная локация конфликта", "Место развития основного действия"),
            ],
            "objects": [
                _object("obj_001", "Ключевой предмет", "Сюжетно важный объект"),
            ],
        },
        "timeline": {
            "plotlines": [_plotline()],
        },
    }


def _short_story_content(title: str) -> dict:
    return {
        "book": {
            "title": title,
            "synopsis": "Короткая история с быстрым входом в конфликт и ярким финалом.",
            "preface": "",
            "genre": "Рассказ",
        },
        "chapters": [
            _chapter("chapter_001", "Рассказ", [_scene("scene_001", "Завязка"), _scene("scene_002", "Конфликт"), _scene("scene_003", "Финал")]),
        ],
        "library": {
            "characters": [
                _character("char_001", "Главный персонаж", "Фокус истории"),
                _character("char_002", "Контрастный персонаж", "Источник давления"),
            ],
            "locations": [
                _location("loc_001", "Главная локация", "Единое пространство рассказа"),
            ],
            "objects": [
                _object("obj_001", "Символический предмет", "Объект для акцента"),
            ],
        },
        "timeline": {
            "plotlines": [_plotline()],
        },
    }


def _poetry_content(title: str) -> dict:
    return {
        "book": {
            "title": title,
            "synopsis": "Сборник с циклами, образами и повторяющимися мотивами.",
            "preface": "Здесь можно описать интонацию сборника, ведущие темы и порядок циклов.",
            "genre": "Поэзия",
        },
        "chapters": [
            _chapter("chapter_001", "Цикл 1", [_scene("scene_001", "Стихотворение 1"), _scene("scene_002", "Стихотворение 2")]),
            _chapter("chapter_002", "Цикл 2", [_scene("scene_003", "Стихотворение 3"), _scene("scene_004", "Стихотворение 4")]),
        ],
        "library": {
            "characters": [
                _character("char_001", "Лирический герой", "Голос сборника"),
            ],
            "locations": [
                _location("loc_001", "Природный образ", "Повторяющееся пространство"),
            ],
            "objects": [
                _object("obj_001", "Ключевой образ", "Повторяющийся символ"),
                _object("obj_002", "Мотив", "Сквозной поэтический элемент"),
            ],
        },
        "timeline": {
            "plotlines": [_plotline("plotline_main", "Циклы сборника", "#6C7B95")],
        },
    }


def _nonfiction_content(title: str) -> dict:
    return {
        "book": {
            "title": title,
            "synopsis": "Структурный текст с тезисами, аргументами и выводами.",
            "preface": "Опишите тему, задачу книги и ожидаемую пользу для читателя.",
            "genre": "Нон-фикшн",
        },
        "chapters": [
            _chapter("chapter_001", "Введение", [_scene("scene_001", "Проблема"), _scene("scene_002", "Контекст")]),
            _chapter("chapter_002", "Основная часть", [_scene("scene_003", "Аргумент 1"), _scene("scene_004", "Аргумент 2")]),
            _chapter("chapter_003", "Выводы", [_scene("scene_005", "Итоги"), _scene("scene_006", "Практическое применение")]),
        ],
        "library": {
            "characters": [
                _character("char_001", "Эксперт", "Источник знания"),
                _character("char_002", "Целевая аудитория", "Главный адресат"),
            ],
            "locations": [
                _location("loc_001", "Контекст исследования", "Среда, в которой проявляется тема"),
            ],
            "objects": [
                _object("obj_001", "Ключевая идея", "Основной тезис книги"),
                _object("obj_002", "Кейс / пример", "Поддерживающий материал"),
            ],
        },
        "timeline": {
            "plotlines": [_plotline("plotline_main", "Линия аргументации", "#4A7DFF")],
        },
    }


def _screenplay_content(title: str) -> dict:
    return {
        "book": {
            "title": title,
            "synopsis": "Сценарная структура с актами, ключевыми сценами и персонажами.",
            "preface": "",
            "genre": "Сценарий",
        },
        "chapters": [
            _chapter("chapter_001", "Акт 1", [_scene("scene_001", "Завязка"), _scene("scene_002", "Инцидент")]),
            _chapter("chapter_002", "Акт 2", [_scene("scene_003", "Развитие"), _scene("scene_004", "Срединный поворот")]),
            _chapter("chapter_003", "Акт 3", [_scene("scene_005", "Кульминация"), _scene("scene_006", "Развязка")]),
        ],
        "library": {
            "characters": [
                _character("char_001", "Протагонист", "Главная роль"),
                _character("char_002", "Антагонист", "Противодействующая сила"),
                _character("char_003", "Второстепенный персонаж", "Поддерживающая роль"),
            ],
            "locations": [
                _location("loc_001", "INT. Комната", "Интерьер"),
                _location("loc_002", "EXT. Улица", "Экстерьер"),
            ],
            "objects": [
                _object("obj_001", "Реквизит", "Ключевой предмет сцены"),
            ],
        },
        "timeline": {
            "plotlines": [_plotline()],
        },
    }


def _fairy_tale_content(title: str) -> dict:
    return {
        "book": {
            "title": title,
            "synopsis": "Сказочная история с архетипами, мотивами пути и чудесными элементами.",
            "preface": "",
            "genre": "Сказка",
        },
        "chapters": [
            _chapter("chapter_001", "Начало", [_scene("scene_001", "Нарушение равновесия"), _scene("scene_002", "Отправление")]),
            _chapter("chapter_002", "Испытания", [_scene("scene_003", "Встреча с помощником"), _scene("scene_004", "Главное испытание")]),
            _chapter("chapter_003", "Возвращение", [_scene("scene_005", "Победа"), _scene("scene_006", "Новая гармония")]),
        ],
        "library": {
            "characters": [
                _character("char_001", "Герой", "Центральный персонаж"),
                _character("char_002", "Помощник", "Волшебная поддержка"),
                _character("char_003", "Противник", "Сила препятствия"),
            ],
            "locations": [
                _location("loc_001", "Родной дом", "Точка отправления"),
                _location("loc_002", "Чудесное место", "Пространство испытаний"),
            ],
            "objects": [
                _object("obj_001", "Волшебный предмет", "Сказочный инструмент"),
            ],
        },
        "timeline": {
            "plotlines": [_plotline("plotline_main", "Путь героя", "#4A7DFF")],
        },
    }


def _chapter(chapter_id: str, title: str, scenes: list[dict]) -> dict:
    return {
        "id": chapter_id,
        "title": title,
        "number": 0,
        "subtitle": "",
        "epigraph": "",
        "synopsis": "",
        "notes": "",
        "status": "in_progress",
        "targetWordCount": 0,
        "scenes": scenes,
    }


def _scene(scene_id: str, title: str) -> dict:
    return {
        "id": scene_id,
        "title": title,
        "text": "",
        "characterIds": [],
        "locationIds": [],
        "objectIds": [],
        "plotlineId": "plotline_main",
        "plotlineIds": ["plotline_main"],
        "characterArcPoints": [],
        "status": "draft",
        "targetWordCount": 0,
        "datetime": "",
        "goal": "",
        "conflict": "",
        "result": "",
    }


def _character(entity_id: str, name: str, extra: str) -> dict:
    return {
        "id": entity_id,
        "name": name,
        "description": "",
        "extra": extra,
        "field2": "",
        "field3": "",
        "short": "",
        "notes": "",
        "sceneAppearances": [],
        "arcSummary": "",
        "arcPoints": [],
    }


def _location(entity_id: str, name: str, extra: str) -> dict:
    return {
        "id": entity_id,
        "name": name,
        "description": "",
        "extra": extra,
        "field2": "",
        "field3": "",
        "short": "",
        "notes": "",
        "sceneAppearances": [],
    }


def _object(entity_id: str, name: str, extra: str) -> dict:
    return {
        "id": entity_id,
        "name": name,
        "description": "",
        "extra": extra,
        "field2": "",
        "field3": "",
        "short": "",
        "notes": "",
        "sceneAppearances": [],
    }


def _plotline(plotline_id: str = "plotline_main", name: str = "Главная сюжетная линия", color: str = "#4A7DFF") -> dict:
    return {
        "id": plotline_id,
        "name": name,
        "color": color,
    }

