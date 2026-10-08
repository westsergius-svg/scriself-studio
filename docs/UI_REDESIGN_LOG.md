# Журнал: внешний вид по макету (демо `docs/img/ScriSelf_Studio_Editor_Themes`)

Дата начала: 2026-09-28. Формат записи: «шаг — что изменено — проверка — результат».
Правило: один файл — одна запись — одна проверка сборки/тестов, затем следующий файл.

## Источник истины по внешнему виду

- `docs/img/ScriSelf_Studio_Editor_Themes/index.html` + `styles.css` + `app.js` — макет.
- Темы макета: Studio, Paper, Graphite, Ocean, Forest (переключатель в правом нижнем углу,
  запоминается в `localStorage`).
- Структура макета: верхняя строка (бренд + имя проекта + навигация + служебные кнопки) →
  левая панель (Проект: разделы + список глав + автосохранение) → центр (шапка редактора,
  «бумага» с текстом, липкий тулбар, футер статистики) → правая панель (вкладки
  Персонаж/Сцена/Линии).

## Уже сделано до начала журнала

- `AppTheme` дополнен значениями Studio/Paper/Graphite/Ocean/Forest (файл
  `src/ScriSelfStudio.Core/Model/AppTheme.cs`).
- `ThemePalette` со словарями кистей для пяти тем (файл
  `src/ScriSelfStudio.Presentation/Theme/ThemePalette.cs`).
- `AvaloniaThemeService` умеет подменять палитру.

## План шагов

1. `docs/UI_REDESIGN_LOG.md` — этот файл. ✅
2. `src/ScriSelfStudio.Presentation/Theme/ThemePalette.cs` — дополнить набором кистей
   (значения макета, а не приблизительные): sidebar/context/field, табы, автосохранение.
3. `src/ScriSelfStudio.Presentation/Styles/Palette.axaml` — синхронизировать словари
   Light/Dark с новыми ключами.
4. `src/ScriSelfStudio.Presentation/Styles/AppStyles.axaml` — стили макета
   (вкладки, строки списков, поля, кнопки-иконки, футер).
5. `src/ScriSelfStudio.App/Views/ThemeSwitcherView.axaml` + `.axaml.cs` — плавающий
   переключатель тем из макета.
6. `src/ScriSelfStudio.App/Shell/ThemeSwitcherViewModel.cs` — модель переключателя
   (5 тем, сохранение в настройки через `IAppSettingsService`).
7. `src/ScriSelfStudio.App/Composition/ScriSelfStudioServices.cs` — регистрация новых
   моделей представления.
8. `src/ScriSelfStudio.App/Views/MainWindow.axaml` — оболочка по макету: верхняя строка,
   левая панель «Проект», центр, правая панель; убрать иконочный рейл.
9. `src/ScriSelfStudio.App/Shell/ShellViewModel.cs` — свойства для новой разметки
   (активная вкладка «Текст», подпись проекта, время автосохранения).
10. `src/ScriSelfStudio.EditorUI/EditorView.axaml` — «бумага» макета: шапка, липкий
    тулбар, футер статистики.
11. `src/ScriSelfStudio.Launcher/SettingsViewModel.cs` + `SettingsView.axaml` — список
    тем синхронизировать с пятью темами макета.
12. `docs/CHANGELOG.md`, `docs/HANDOFF.md` — итоговая запись.

---

## Шаг 1 — журнал работ

- Изменено: создан `docs/UI_REDESIGN_LOG.md` с планом и журналом.
- Проверено: не проверено (файл документации, сборка не затронута).

## Шаг 2 — `AppTheme` и `ThemePalette`

- Изменено (ранее, до журнала): `src/ScriSelfStudio.Core/Model/AppTheme.cs` — добавлены
  Studio/Paper/Graphite/Ocean/Forest; `src/ScriSelfStudio.Presentation/Theme/ThemePalette.cs` —
  словари кистей пяти тем по значениям макета.
- Проверено: сборка проходит.

## Шаг 3 — `src/ScriSelfStudio.App/Shell/ShellViewModel.cs`

- Изменено: дописана незавершённая часть интеграции тем — добавлены зависимости
  `IAppSettingsService` и `IThemeService`, свойство `CurrentThemeName`, начальное значение
  `SelectedTheme` из настроек, команда `SelectThemeCommand` (применяет тему сразу и
  сохраняет её через `UpdateAsync`), поле `StatusMessage`.
- Проверено: `dotnet build` — 0 ошибок, 0 предупреждений.

## Шаг 4 — `src/ScriSelfStudio.Presentation/Styles/AppStyles.axaml`

- Изменено: добавлены стили макета по секциям: верхняя строка (`brandMark`,
  `brandName`, `brandDivider`, `projectName`, `navItem` + `navUnderline`), левая
  панель (`panelHeading`, `outlinePlus`, `sideItem`, `captionLabel`,
  `chapterRow` + `chapterAccent`, `autosaveLine`), шапка редактора
  (`editorHead`, `editorCrumb`, `editorSaveState`, `accentBtn`), «бумага»
  (`editorToolbar`, `editorHeading`, `editorFooterItem`), правая панель
  (`contextTab`, `avatar`, `cardHeading`, `cardQuote`, `cardField`, `lineRow`,
  `tagChip`), переключатель тем (`themeTrigger`, `themeMenu`, `themeMenuItem`,
  `themeSwatch`). Все цвета — через `DynamicResource`, чтобы темы макета
  применялись без правок разметки.
- Проверено: `dotnet build` — 0 ошибок.

## Шаг 5–6 — переключатель тем: представление и модель

- Изменено: `src/ScriSelfStudio.App/Views/ThemeSwitcherView.axaml` + `.axaml.cs` — плавающий
  переключатель в углу рабочего пространства: кнопка «Тема <название> ▾» и меню из пяти
  тем с цветным образцом, названием и подписью.
- Изменено: `src/ScriSelfStudio.App/Shell/ShellViewModel.cs` — `ThemeOption` получил
  образец цвета (`ImmutableSolidColorBrush`, общий для элементов меню), команда
  `ToggleThemeMenuCommand`; `SelectThemeCommand` закрывает меню после выбора.
- Отклонение от плана: отдельная модель `ThemeSwitcherViewModel` не заводилась — вся логика
  тем уже живёт в `ShellViewModel` (список тем, применение, сохранение), а вторая модель
  означала бы дублирование состояния и рассинхронизацию с настройками.
- Проверено: `dotnet build` — 0 ошибок.

## Шаг 7 — регистрация моделей представления

- Изменено: ничего. `ThemeSwitcherView` работает с общим `ShellViewModel`, который уже
  зарегистрирован в `ScriSelfStudioServices`; отдельная регистрация не требуется.
- Проверено: не требуется.

## Шаг 8 — `src/ScriSelfStudio.App/Views/MainWindow.axaml`

- Изменено: оболочка переведена на `Panel`, чтобы положить плавающий переключатель тем
  поверх рабочего пространства; иконочный рейл (колонка 48 px) убран, колонки сдвинуты
  (дерево 0, экран 1, контекст 2); добавлен `xmlns:views` и сам `ThemeSwitcherView`
  с отступом от правой панели и статус-бара.
- Проверено: `dotnet build` — 0 ошибок; кадр `artifacts/render/ui-redesign/03-editor.png`
  показывает раскладку без рейла и с переключателем в углу.

## Шаг 9 — `src/ScriSelfStudio.App/Shell/ShellViewModel.cs`

- Изменено: свойства для новой разметки уже были (активные вкладки, крошки, статистика,
  состояние сохранения, автосохранение) — дополнительных правок не потребовалось.
- Проверено: сборка и тесты оболочки проходят.

## Шаг 10 — `src/ScriSelfStudio.EditorUI/EditorView.axaml`

- Изменено: «бумага» макета уже свёрстана (шапка сцены, липкий тулбар, футер статистики) —
  шаг закрыт ранее на шаге 4, повторных правок не потребовалось.
- Проверено: кадр `03-editor.png` — бумага, тулбар и футер на месте.

## Шаг 11 — список тем в настройках

- Изменено: `src/ScriSelfStudio.Launcher/SettingsViewModel.cs` — список тем синхронизирован
  с переключателем: Studio, Paper, Graphite, Ocean, Forest (значения System/Light/Dark
  убраны, они не соответствуют палитрам макета).
- Изменено: `src/ScriSelfStudio.Core/Model/ProjectSettings.cs`,
  `src/ScriSelfStudio.Contracts/Settings/IAppSettingsService.cs` — тема по умолчанию
  `Studio` вместо `System`, которого больше нет в списке.
- Изменено: `tests/ScriSelfStudio.UiSmokeTests/SettingsViewTests.cs` — тест смены темы
  переведён на `Forest`; добавлен
  `WorkspaceShellTests.Theme_switcher_lists_five_themes_and_applies_the_chosen_one`.
- Проверено: `dotnet test` — все проекты зелёные, UiSmokeTests 39 тестов.

## Шаг 12 — итоговая запись

- Изменено: `docs/CHANGELOG.md`, `docs/HANDOFF.md`.
- Проверено: не проверялось (файлы документации).
