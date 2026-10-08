# Архитектура ScriSelf Studio

Документ описывает целевую архитектуру решения и правила, которые нельзя нарушать.
Источник требований — `ТЗ.txt` (ТЗ п. 4, 7, 22, 30, 32, 33, 34).

## Слои и проекты

| Проект | Роль | Ссылается на |
| --- | --- | --- |
| `ScriSelfStudio.Contracts` | Публичные интерфейсы, DTO-снимки, ошибки, события | `Core` |
| `ScriSelfStudio.Core` | Доменная модель, каноническая модель текста, сериализация, версия схемы, события | — |
| `ScriSelfStudio.Persistence` | SQLite-хранилище `.scri`, миграции, атомарная запись, recovery, настройки | `Core`, `Contracts` |
| `ScriSelfStudio.BookArchitecture` | Правила структуры: серии → книги → главы → сцены | `Core`, `Contracts` |
| `ScriSelfStudio.EditorEngine` | Каноническая модель текста, история отмены, поиск, статистика | `Core`, `Contracts` |
| `ScriSelfStudio.Presentation` | Общие UI-сервисы, базовые ViewModel, темы и ресурсы | `Core`, `Contracts` |
| `ScriSelfStudio.Launcher` | Стартовый экран и настройки | `Core`, `Contracts`, `Presentation` |
| `ScriSelfStudio.EditorUI` | Экраны редактора сцены | `Core`, `Contracts`, `Presentation` |
| `ScriSelfStudio.ArchitectureUI` | Экраны структуры книги и карты глав | `Core`, `Contracts`, `Presentation` |
| `ScriSelfStudio.App` | Единственная точка регистрации реализаций и запуска | все |

## Правила зависимостей

1. `Core` не зависит ни от чего: ни от Avalonia, ни от DI, ни от Persistence. Абстракции,
   которые реализует ядро (`ISystemClock`, `IEventBus`), объявлены в `Core`.
2. `Contracts` зависит только от `Core`. Это единственная разрешённая ссылка.
3. `Contracts`, `Core`, `EditorEngine`, `BookArchitecture`, `Persistence` не знают об Avalonia.
4. UI-модули (`Presentation`, `Launcher`, `EditorUI`, `ArchitectureUI`) не ссылаются на
   `Persistence`: работу с хранилищем они видят только через интерфейсы `Contracts`.
5. Только `App` знает конкретные реализации и регистрирует их в DI.
6. Диалоги, навигация и темы объявлены в `Contracts`/`Presentation` и подменяются
   в тестах (`IFileDialogService`, `INavigationService`, `IThemeService`).

Правила 1–5 проверяются автоматически (`tests/ScriSelfStudio.IntegrationTests/ArchitectureRulesTests.cs`).

## Владение состоянием

- **Открытый проект** принадлежит `IProjectService`. UI получает снимок `ProjectDto`
  и отправляет команды; прямого доступа к модели у UI нет (ТЗ п. 22).
- **Текст сцены** в процессе редактирования принадлежит `IEditorEngine`
  (`SceneEditorEngine`). Движок хранит каноническую модель `RichDocument`,
  единственную историю отмены и передаёт изменения владельцу проекта через commit.
- **История отмены** имеет одного владельца — движок. Avalonia-контрол не ведёт
  собственную историю (ТЗ п. 22).
- **Правила структуры** (порядок, уникальность названий, мягкое удаление) живут только
  в `IBookArchitectureService`; UI их не дублирует.

## Поток данных

```
LauncherViewModel / EditorViewModel / ArchitectureViewModel   (UI, Avalonia)
        │  команды
        ▼
IProjectService ── IBookArchitectureService ── IEditorEngine  (Contracts)
        │                                              │
        ▼                                              ▼
ProjectService (Persistence)                    SceneEditorEngine
        │                                              │
        ▼                                              ▼
IProjectStore → SQLite .scri                    RichDocument (Core)
```

События (`Contracts/Events`) обновляют независимые модули: проект, структура, документ,
сохранение, recovery, тема. Событие содержит только идентификаторы и короткие значения.

## Формат и версия

- Файл `.scri` — база SQLite. Схема и правила описаны в `docs/DATA_FORMAT.md`.
- Версия схемы обязательна (`SchemaVersion`). Несовместимые версии обрабатываются
  миграцией; будущая версия отклоняется с понятной ошибкой.
- Каждое сохранение — полный снимок во временный файл с последующей заменой
  (решение зафиксировано в `docs/decisions/002-sqlite-write-strategy.md`).

## Модульность (ТЗ п. 34)

Экраны регистрируются в `ScriSelfStudioServices.RegisterModules`. Отключение модуля —
это отсутствие его регистрации: экран становится недоступен в `INavigationService`,
команды и пункты меню его не показывают. Функция не должна вести в отсутствующий экран.

## Известные отклонения от буквы ТЗ

| Пункт ТЗ | Отклонение | Причина |
| --- | --- | --- |
| п. 33, роль ядра | `IEventBus` и `ISystemClock` объявлены в `Core`, а не в `Contracts` | Это абстракции, которые ядро *реализует*; вынос их в `Contracts` дал бы `Core → Contracts` и цикл. Зафиксировано в `docs/decisions/001-core-abstractions.md`. |
| п. 21 | Форматированный текст отображается обычным текстовым контролом; разметка хранится в модели | Компонент форматированного текста не выбран окончательно; см. `docs/decisions/003-text-editor-component.md`. |

## Что осталось на следующий этап

- Экраны `EditorUI` и `ArchitectureUI` (этапы 6 и 7 роадмапа).
- Автосохранение по таймеру и диалог восстановления в UI (сервисы готовы).
- Сюжетные линии и таймлайн (вне первой поставки, ТЗ п. 31).
