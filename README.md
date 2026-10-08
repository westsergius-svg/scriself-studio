# ScriSelf Studio

Офлайн-редактор писательских проектов для Windows: книги → главы → сцены, текстовый
редактор, карта структуры, сюжетные линии, автосохранение и восстановление.

Стек: C# 12, .NET 8 (LTS), Avalonia 11 (MVVM), Microsoft.Data.Sqlite, System.Text.Json.

Формат проекта `.scri` определяется по содержимому файла: собственный (SQLite) и старый
контейнер (`manifest.json` + `content.json`) читаются и записываются в том же формате,
поэтому старые проекты продолжают открываться (см. `docs/DATA_FORMAT.md`, ADR 009).

## Структура решения

| Проект | Назначение |
| --- | --- |
| `ScriSelfStudio.Contracts` | Публичные интерфейсы, DTO-снимки, ошибки, события (ТЗ п. 7, 22, 25, 26) |
| `ScriSelfStudio.Core` | Доменная модель проекта, каноническая модель текста, схема данных (ТЗ п. 10, 29) |
| `ScriSelfStudio.Persistence` | SQLite-хранилище `.scri`, миграции, атомарная запись, резервные копии (ТЗ п. 13) |
| `ScriSelfStudio.BookArchitecture` | Книги, главы, сцены, порядок, связи, валидация (ТЗ п. 16, этап 2) |
| `ScriSelfStudio.EditorEngine` | Текстовый буфер, история, поиск, статистика (ТЗ п. 11, 21) |
| `ScriSelfStudio.Presentation` | Общие UI-сервисы, базовые ViewModel, ресурсы и темы (ТЗ п. 33) |
| `ScriSelfStudio.Launcher` | Стартовый экран и настройки: создание, открытие, последние проекты, тема, шрифт, автосохранение (ТЗ п. 27) |
| `ScriSelfStudio.EditorUI` | AXAML редактора сцены (ТЗ п. 27, этап 6) |
| `ScriSelfStudio.ArchitectureUI` | AXAML карты книги и дерева структуры, переключение видов (ТЗ п. 27, этап 7) |
| `ScriSelfStudio.StoryUI` | Экраны персонажей и сюжетных линий: карточка из 14 разделов, шаги арки, этапы линий (ТЗ п. 27; ADR 010, 011) |
| `ScriSelfStudio.App` | Композиция зависимостей и запуск приложения |

## Сборка и проверка

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet run --project "src/ScriSelfStudio.App"
```

Открыть конкретный проект (в том числе из проводника Windows, если сопоставить
расширение `.scri` с приложением):

```powershell
dotnet run --project "src/ScriSelfStudio.App" -- "D:\Книги\Рукопись.scri"
artifacts\publish\win-x64\ScriSelfStudio.exe "D:\Книги\Рукопись.scri"
```

Публикация одиночного exe:

```powershell
dotnet publish src\ScriSelfStudio.App\ScriSelfStudio.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false `
  -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\publish\win-x64
```

## Статус

Готово: ядро и модель, SQLite-хранилище `.scri` с атомарной записью и восстановлением,
архитектура книги, движок редактора сцены, карта книги, персонажи с арками и этапами
сюжетных линий, стартовый экран, темы, настройки.

В работе: диалог восстановления в UI, ручная приёмка на реальных файлах.

Вне текущей поставки: импорт ранних модульных SQLite-проектов (формат `entities`/`meta`),
орфография, экспорт (ТЗ п. 31).

Текущее состояние работ и передача контекста — `docs/HANDOFF.md`.

## Документация

- `ТЗ.txt` — исходное техническое задание (единственный источник требований).
- `docs/ARCHITECTURE.md` — архитектура и правила зависимостей.
- `docs/CONTRACTS.md` — публичные контракты.
- `docs/DATA_FORMAT.md` — формат `.scri`.
- `docs/AI_WORKFLOW.md` — правила работы агентов.
- `docs/DECISIONS.md` — журнал архитектурных решений.
- `docs/TESTING.md` — стратегия проверок.
- `docs/CHANGELOG.md` — история изменений.
- `docs/decisions/` — записи ADR.
- `docs/HANDOFF.md` — передача контекста между сессиями (ТЗ п. 42, 50).

## Правила, которые нельзя нарушать

1. `Contracts`, `Core` и `EditorEngine` не зависят от Avalonia.
2. UI не ссылается на реализацию `Persistence`; регистрация — только в `App`.
3. Публичный контракт меняет только владелец контрактов (архитектор базы).
4. Старый файл проекта не перезаписывается без успешной проверки и резервной копии.
5. Задача не считается выполненной только потому, что проект компилируется.
