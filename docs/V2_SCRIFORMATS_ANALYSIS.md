# Разбор форматов `.scri` (новая версия ScriSelf Studio)

Дата: 2026-10-06
Источник данных: `C:\Users\Papa\Documents\Scriborium` (реальные файлы), код
`src/ScriSelfStudio.Persistence`, спецификация `docs/DATA_FORMAT.md`.

Цель документа — зафиксировать все встреченные поколения файлов `.scri`, их схемы,
различия в создании/чтении и пробелы текущего ридера перед стартом новой версии.

---

## 1. Что лежит в папке Scriborium

| Файл | Формат | Поколение |
| --- | --- | --- |
| `1 (2).scri`, `1 (3).scri`, `Первый раз.scri` | SQLite | A — модульный `scriborium` v2 |
| `2.scri`, `курс.scri` | ZIP (`manifest.json` + `content.json`) | B — контейнер `scri` v1 |
| `1.scri` | SQLite (module=`core`) | C1 — `scri.p 0.1.1` |
| `It''s my best book, even if it''s my first..scri` | SQLite (module=`core`) | C2 — `ScriSelf Studio beta 0.1`, schema 5 |
| `3.scri`, `Коты.scri`, `Новый проект.scri` | SQLite | D — новый бета-формат, schema 3 |
| `Backups/*.backup` | SQLite/ZIP (снимки) | копии A/B |

Определять формат нужно по содержимому, а не по расширению: у всех одно `.scri`.

---

## 2. Поколение A — модульный `scriborium` v2

Файлы: `1 (2).scri`, `1 (3).scri`, `Первый раз.scri`.
Признак: SQLite, таблицы `entities` + `meta`, в `meta` `format=scriborium`, `formatVersion=2`.

Таблицы: `entities`, `meta`, `modules`, `module_data`, `relations`, `texts`.

| Таблица | Колонки |
| --- | --- |
| `entities` | `id, type, module, data, name, chapter_id, ord, created_at, updated_at` |
| `meta` | `key, value` |
| `modules` | `id, version, enabled, installed_at` |
| `module_data` | `module_id, data, updated_at` |
| `relations` | `id, from_id, to_id, rel_type, meta, created_at` |
| `texts` | `id, content(BLOB), compressed, raw_len, words, chars, chars_ns, paragraphs, updated_at` |

`meta`: `format=scriborium`, `formatVersion=2`, `title`, `author`, `createdAt`, `modifiedAt`,
`importedFrom`.
`modules`: `architecture, arcs, characters, editor, export, library, plotlines, timeline` (все `v2.0.0`).

Типы сущностей и ключи их `data`:

| type | ключи `data` |
| --- | --- |
| `book` | annotation, audience, author, coverMediaId, genre, language, preface, subtitle, title, tone, year |
| `chapter` | arcId, description, epigraph, eventType, notes, number, order, place, status, subtitle, targetWords, textId, time, title |
| `scene` | chapterId, conflict, dateTime, goal, order, result, status, targetWords, textId, title |
| `plotline` | color, description, name, order |
| `story_arc` | color, description, order, title |
| `location` | description, name, order |
| `character` | (в этих файлах встречается редко) |

Текст сцены: `texts.id = "txt_" + sceneId`, `content` — сырой UTF-8 (при `compressed=1` — deflate).
Форматирования в тексте нет (плоский текст).
Связи: `relations.rel_type = "chapter:plotline"`.

---

## 3. Поколение B — ZIP-контейнер `scri` v1

Файлы: `2.scri`, `курс.scri`. Признак: заголовок `PK`.
Внутри — `manifest.json` и `content.json`.

`manifest.json`: `format="scri"`, `version=1`, `name`, `template`, `createdAt`, `updatedAt`.

`content.json` (у `курс.scri`): `book`, `chapters`, `library`, `timeline`, `volumes`.
У `2.scri` дополнительно есть раздел `scriself` — это уже наша надстройка (ADR 011).

- `book`: title, synopsis, preface, subtitle, author, genre, format, tone, audience.
- `chapters[]`: id, title, number, subtitle, epigraph, synopsis, notes, status, targetWordCount,
  `scenes[]`, volumeId, `chapterText`, `chapterTextHtml`, `chapterTextSections`.
- `chapters[].scenes[]`: id, title, text, characterIds, locationIds, objectIds, plotlineId,
  plotlineIds, characterArcPoints, status, targetWordCount, datetime, goal, conflict, result, dateTime.
- `timeline.plotlines[]`: id, name, color.
- `library`: characters / locations / objects. Пример персонажа: id, name, description, extra,
  field2, field3, short, notes, sceneAppearances, arcSummary, arcPoints.
- `volumes[]`: id, number, title, synopsis.
- `scriself` (наша надстройка): version, `characters[]` (projectId, name, role, importance,
  status, tags, arcType, draftStatus, relations, arcSteps, id, sourceId, order, audit, isDeleted),
  `plotlines[]`, `documents{ sceneId: RichDocument }`.

Текст сцены — плоский (`scene.text`), плюс наш `scriself.documents` с RichDocument v1.
Неизвестные поля (`library`, `volumes`, служебные поля глав) при записи сохраняются
(`Project.SourceDocumentJson`, ADR 009).

---

## 4. Поколение C — ранние `core`-сборки (SQLite)

Признак: SQLite, таблицы `entities` + `meta`, но нет `format`/`formatVersion`,
а `meta.appVersion` = `scri.p 0.1.1` (C1) или `ScriSelf Studio beta 0.1` (C2).
Все сущности имеют `module = "core"`. Таблица `modules` пустая.

### C1 — `scri.p 0.1.1` (`1.scri`)

Типы и ключи `data`:

| type | ключи `data` |
| --- | --- |
| `book` | annotation, author, order, subtitle, title |
| `chapter` | bookId, description, number, status, textId, title |
| `scene` | chapterId, description, isKey, order, status, textId, title |
| `story_arc` | color, description, order, status, title — используется как сюжетная линия |
| `character` | motivation, name |
| `plotline_stage` | description, isFinal, name, order, plotlineId |

`relations`: `stage_participant` (участники этапа), `scene_plotline` (сцены линии).
`texts`: колонки как в A, но без `format`; `texts.id = "text-" + sceneId`.

### C2 — `ScriSelf Studio beta 0.1` (`It''s my best book...scri`, schema 5)

Дополнительно к C1: `meta.schema_version=5`, `meta.designId`, `meta.template`.
Новые типы: `character_stage` (arcStep), `plotline_stage` с `isFinal`.
`book` получает `volume`; `character` — 17 полей карточки (age, appearance, externalGoal,
falseBelief, family, internalNeed, keyTraits, mainFear, motivation, name, occupation, quirks,
secrets, speech, temperament, trauma, values).

`texts` получает колонки `format` (`plain` | `rich`) и `format_version`:
- `plain` — обычный текст;
- `rich` — JSON вида `{version, blocks:[{style:{style}, alignment, runs:[{text, marks:[{mark,value}]}]}]}`
  (это другой формат разметки, не наш `RichDocument`).

---

## 5. Поколение D — новый бета-SQLite (текущий, schema 3)

Файлы: `3.scri`, `Коты.scri`, `Новый проект.scri`.
Признак: SQLite с таблицами `project` + `schema_info`.

Таблицы: `project`, `series`, `book`, `chapter`, `scene`, `scene_document`, `character`,
`plot_line`, `plot_line_stage`, `schema_info`.

Особенности:
- У всех сущностей есть `is_deleted` / `deleted_at` (soft delete), `order_index`;
  читать нужно только строки с `is_deleted = 0`.
- `scene.revision` — счётчик ревизий (конфликты сохранения), `scene.metadata_json`
  (`isKey`, `isLocked`, `tags[]`).
- `scene_document.document_json` — RichDocument v1:
  `{schemaVersion, blocks:[{id, kind, alignment, indentLevel, spans:[{text, style:{flags,
  foreground, background, isDefault}}]}]}`. `style.flags` — строковый enum (`Bold`, None…).
- `project.settings_json` — тема, шрифт, кегль, автосохранение, бэкапы, язык.
- `schema_info.app_version` (напр. `0.1.0.0`), `version`.
- У `book` только `title`: метаданные книги (author, genre, annotation, subtitle…) в этом
  поколении отсутствуют — при миграции из A/B их нужно брать из источника.
- `character.data_json` — карточка (role, importance, status, tags, arcType, draftStatus,
  relations, arcSteps[{kind, title, id, order, audit, isDeleted}]).
- `plot_line.scene_ids_json`, `plot_line_stage.data_json`
  (participants[{characterId, characterArcStepId}], sceneIds, isFinal, icon).

---

## 6. Поколения разметки текста (их три!)

| Поколение | Где | Формат текста |
| --- | --- | --- |
| A, B | `texts.content`, `scene.text` | плоский текст / UTF-8 BLOB (deflate) |
| C2 | `texts.content` при `format="rich"` | `{version, blocks[{style.style, alignment, runs[{text, marks[{mark,value}]}]}]}` |
| B (`scriself`), D | `scriself.documents`, `scene_document.document_json` | RichDocument v1: `blocks[{kind, alignment, indentLevel, spans[{text, style{flags, foreground, background}}]}]` |

Итого читателю нужно уметь минимум три «языка» текста и приводить их к одной модели.

---

## 7. Сводка различий (что менялось от поколения к поколению)

| Аспект | A (scriborium v2) | B (ZIP scri v1) | C (core 0.1.x/0.1.1) | D (beta schema 3) |
| --- | --- | --- | --- | --- |
| Контейнер | SQLite | ZIP JSON | SQLite | SQLite |
| Идентификация | `meta.format=scriborium` | `PK` + manifest | `meta.appVersion` | `schema_info` |
| Книга | сущность `book` + метаданные | `book` в JSON | `book` (мало полей) | таблица `book`, только title |
| Главы/сцены | entities + `chapter_id` | вложены в chapters | entities + `bookId`/`chapterId` | таблицы |
| Текст | `texts` (BLOB) | `scene.text` | `texts` (`text-…`) | `scene_document` |
| Разметка | нет | наш `scriself` | `format=rich` JSON | RichDocument v1 |
| Линии | `plotline` + `story_arc` | `timeline.plotlines` | `story_arc` как линия + `plotline_stage` | `plot_line` + `plot_line_stage` |
| Персонажи | `character` | `library.characters` | `character` + `character_stage` | `character` |
| Связи | `relations` (`chapter:plotline`) | поля сцены | `relations` (`scene_plotline`, `stage_participant`) | `scene_ids_json`, `participants` |
| Удаление | физическое | физическое | физическое | soft delete |

---

## 8. Что уже умеет текущий код и где пробелы

Чтение (`src/ScriSelfStudio.Persistence`):
- `RoutingProjectStore` определяет ZIP по сигнатуре, иначе SQLite; если `entities`+`meta` —
  пробует `LegacyModularImporter`, иначе отдаёт понятную ошибку (`LegacyModularFormat.Describe`).
- `LegacyModularImporter` поддерживает типы book, chapter, scene, character, plotline,
  plotline_stage; читает `texts.content` (deflate при `compressed=1`).
- `ScriZipProjectStore`/`ScriZipMapper` читают/пишут ZIP и сохраняют `scriself` и чужие поля.
- `SqliteProjectReader`/`Writer` работают с поколением D.

Пробелы (важно для новой версии):
1. `story_arc` не поддерживается — а в A это арка, в C1/C2 это и есть сюжетная линия.
2. `character_stage` не поддерживается — теряются шаги арки персонажа (C2).
3. `location`/`object` не поддерживаются — теряется библиотека локаций/объектов (A, B).
4. Rich-text JSON в `texts` (C2) читается как текст — в сцену попадёт сырой JSON
   вида `{"version":2,"blocks":…}`.
5. Метаданные книги (author/genre/annotation/subtitle/year/tone/audience) из A/B/C
   не переносятся в D-модель (у `book` их нет) — нужен отдельный слот метаданных.
6. soft delete (D) нужно учитывать при чтении и записи (`is_deleted=0`).
7. Нужна единая модель сведения трёх разметок (см. §6) и маппинг ссылок
   (`textId` → сцена, `plotlineId`/`sceneIds` → линия, `characterArcStepId` → шаг).

---

## 9. Рекомендация для новой версии

1. Один вход — `ProjectLoader`, который по содержимому выбирает конвертер:
   A|C → LegacyModularConverter, B → ZipConverter, D → NativeConverter.
2. Внутренняя модель — наименьший общий знаменатель + «хвосты»: книга/серия, главы, сцены,
   плоский текст, персонажи (+шаги арки), линии (+этапы+участники), локации/объекты
   (опционально), метаданные книги, настройки, source-поля для round-trip.
3. Текст сцены в новой версии — плоский (см. отказ от своего текстового движка).
   При импорте разметку C2/D сохранять в «исходную разметку сцены» на случай экспорта,
   но в редакторе показывать/extract только `text`.
4. Каждый импорт возвращает `warnings` (что не перенесено) и никогда не перезаписывает
   оригинал до успешной проверки и `.bak` (ТЗ п. 13).

---

## 10. Приложение: артефакты разбора

- `artifacts/_scri_dump.txt` — таблицы/колонки/счётчики по всем файлам.
- `artifacts/_scri_deep.txt` — полные DDL и образцы строк по A/C/D и ZIP.
- `artifacts/_scri_diff.txt` — `rel_type`, `modules`, `meta`, форматы текста по файлам.
