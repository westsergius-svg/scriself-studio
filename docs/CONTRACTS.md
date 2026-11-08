# Публичные контракты

Все публичные интерфейсы живут в `ScriSelfStudio.Contracts` либо в `Core`
(абстракции, которые реализует ядро; см. `docs/decisions/001-core-abstractions.md`).
Источник требований — ТЗ п. 7, 22, 24, 25, 26, 30, 40.

## Владение и изменение

- Владелец `Contracts` — архитектор базы (одно ответственное лицо).
- Исполнитель не меняет публичный контракт сам и не создаёт локальную копию интерфейса.
  Предложение об изменении оформляется по шаблону из `docs/AI_WORKFLOW.md`.
- UI работает **только** через эти интерфейсы и ViewModel; бизнес-логика в code-behind
  запрещена (ТЗ п. 14).

## Результаты и ошибки

Все операции возвращают `Result` / `Result<T>` (`Contracts/Errors`).

- `Result.IsSuccess`, `Result.Error` (`AppError`: `Code`, `UserMessage`, `TechnicalDetails`, `Exception`).
- Для пользователя — `UserMessage` и следующее действие; техническая причина — в лог.
- Типизированные коды — `ErrorCode` (ТЗ п. 26): `ProjectNotFound`, `ProjectFormatInvalid`,
  `SchemaMigrationFailed`, `StorageReadFailed`, `StorageWriteFailed`, `EntityNotFound`,
  `ValidationFailed`, `EditorOperationFailed`, `EditorOperationConflict`,
  `UnsupportedDocumentVersion` и другие.
- Silent fallback на пустой проект запрещён.

## Хранилище

### `IProjectStore`

```csharp
Task<Result<StoreProbeResult>> ProbeAsync(string filePath, CancellationToken ct = default);
Task<Result<ProjectLoadResult>> LoadAsync(string filePath, CancellationToken ct = default);
Task<Result> SaveAsync(ProjectSaveRequest request, CancellationToken ct = default);
```

Проверка, загрузка и атомарное сохранение `.scri`. `ProjectSaveRequest` содержит
`Project`, `FilePath`, `CreateBackup`.

### `IProjectRecoveryStore`

```csharp
string GetRecoveryPath(string projectFilePath);
Task<Result> WriteAsync(Project project, string projectFilePath, CancellationToken ct = default);
Task<Result<Project>> ReadAsync(RecoveryInfo recovery, CancellationToken ct = default);
Task<Result<RecoveryInfo?>> FindAsync(string projectFilePath, CancellationToken ct = default);
Task<Result> DeleteAsync(string projectFilePath, CancellationToken ct = default);
```

## Проект

### `IProjectService` — владелец открытого проекта

```csharp
ProjectDto? Current { get; }
string? CurrentFilePath { get; }
    bool IsDirty { get; }

    /// <summary>Формат файла проекта, если он не наш (например, «scri-zip-v1»).</summary>
    string? CurrentSourceFormat { get; }

Task<Result<ProjectDto>> CreateAsync(CreateProjectRequest request, CancellationToken ct = default);
Task<Result<ProjectDto>> OpenAsync(string filePath, CancellationToken ct = default);
Task<Result> SaveAsync(CancellationToken ct = default);
Task<Result> SaveAsAsync(string filePath, CancellationToken ct = default);
Task<Result> CloseAsync(bool discardChanges, CancellationToken ct = default);

Result<ProjectDto> Mutate(Func<Project, Result> change);

Result<SceneDocumentDto> GetSceneDocument(Guid sceneId);
Result<IReadOnlyList<SceneVersionInfo>> GetSceneVersions(Guid sceneId);
Result<SceneVersionDocument> GetSceneVersion(Guid sceneId, Guid versionId);
Result<SceneVersionInfo> CreateSceneVersion(Guid sceneId, long baseRevision, string name);
Result<SceneDocumentDto> RestoreSceneVersion(Guid sceneId, Guid versionId, long baseRevision);
Result DeleteSceneVersion(Guid sceneId, Guid versionId);
Task<Result<SceneDocumentDto>> UpdateSceneDocumentAsync(UpdateSceneDocumentRequest request, CancellationToken ct = default);
Task<Result<ProjectDto>> UpdateSceneAsync(UpdateSceneRequest request, CancellationToken ct = default);
Task<Result<ProjectDto>> UpdateSettingsAsync(UpdateProjectSettingsRequest request, CancellationToken ct = default);

Task<Result<RecoveryInfo?>> DetectRecoveryAsync(string filePath, CancellationToken ct = default);
Task<Result<ProjectDto>> RecoverAsync(RecoveryInfo recovery, CancellationToken ct = default);
Task<Result> DiscardRecoveryAsync(RecoveryInfo recovery, CancellationToken ct = default);
```

Правила:

- `Current` — снимок; UI не изменяет модель проекта напрямую.
- `Mutate` применяет доменную операцию в памяти и помечает проект изменённым; запись
  на диск выполняет сохранение.
- `GetSceneDocument` возвращает **копию** документа сцены и её ревизию.
- Версии сцен (ADR 031): список содержит сведения, отдельное чтение — копию
  `RichDocument`. Создание требует текущую ревизию и имя длиной 1–120 символов;
  одинаковые имена допустимы. Восстановление проверяет ревизию, сначала сохраняет
  страховочную копию текущего документа, затем заменяет текст с оформлением,
  увеличивает ревизию и публикует `SceneChanged`. Удаление затрагивает только версию.
  Все три операции меняют память и `IsDirty`; запись выполняется через `SaveAsync`.
  При ошибке записи изменения остаются в памяти, UI предлагает повторное сохранение.
  `ISceneVersionsDialogService.ShowAsync(sceneId)` открывает модальную историю:
  редактор заранее фиксирует всю ленту и останавливает автосохранение до закрытия.
  Просмотр сохраняет Undo; восстановление перечитывает документ и начинает новую
  цепочку Undo. Возврат к прежнему тексту доступен через страховочную версию.
- `UpdateSceneDocumentAsync` требует `BaseRevision`, равную текущей ревизии сцены;
  иначе возвращается `EditorOperationConflict` (защита от потери чужих правок).
  При успехе ревизия увеличивается на 1 и публикуется `SceneChanged`.
- `UpdateSettingsAsync` проверяет значения (размер шрифта `8…72`, интервал автосохранения
  `5…3600`, непустые шрифт и язык), применяет их и сразу записывает файл проекта
  (ADR 007). Без открытого проекта возвращается `ProjectNotOpen`.
- `UpdateSceneAsync` принимает `IsKey` (отметка «ключевая сцена»); `null` — не менять.
- `UpdateSceneRequest.PlotLineStepIds` задаёт связи и локальный порядок шагов сцены
  (ADR 027): `null` — не менять, пустой список — снять все шаги, повторы удаляются.
  Источник связей — `PlotLineStep.SceneIds`; `SceneDto.PlotLineStepOrder` задаёт только
  порядок отображения. Устаревшие ID игнорируются, новые связанные шаги добавляются
  после известных. Перестановка в одной сцене не меняет другие сцены или порядок линий.
- Снимки несут объём для карты книги: `SceneDto.Words`, `ChapterDto.Words` (сумма сцен),
  `BookDto.Words` (сумма глав) и `SceneDto.IsKey` (ADR 008). Текст сцен в снимок не входит.

## Структура книги

### `IBookArchitectureService`

```csharp
IReadOnlyList<BookDto> GetBooks();
IReadOnlyList<SeriesDto> GetSeries();
Task<Result<ProjectDto>> CreateSeriesAsync(CreateSeriesRequest request, CancellationToken ct = default);
Task<Result<ProjectDto>> CreateBookAsync(CreateBookRequest request, CancellationToken ct = default);
Task<Result<ProjectDto>> CreateChapterAsync(CreateChapterRequest request, CancellationToken ct = default);
Task<Result<ProjectDto>> CreateSceneAsync(CreateSceneRequest request, CancellationToken ct = default);
Task<Result<ProjectDto>> RenameEntityAsync(RenameEntityRequest request, CancellationToken ct = default);
    Task<Result<ProjectDto>> DeleteEntityAsync(DeleteEntityRequest request, CancellationToken ct = default);
Task<Result<ProjectDto>> MoveEntityAsync(MoveEntityRequest request, CancellationToken ct = default);
    Result ValidateTitle(EntityKind kind, string? title);
```

### Персонажи и сюжетные линии (ADR 010)

```csharp
IReadOnlyList<CharacterDto> GetCharacters();
IReadOnlyList<PlotLineDto> GetPlotLines();

Task<Result<ProjectDto>> SaveCharacterAsync(CharacterDto character, CancellationToken ct = default);
Task<Result<ProjectDto>> DeleteCharacterAsync(Guid characterId, CancellationToken ct = default);
Task<Result<ProjectDto>> SavePlotLineAsync(PlotLineDto plotLine, CancellationToken ct = default);
Task<Result<ProjectDto>> DeletePlotLineAsync(Guid plotLineId, CancellationToken ct = default);
```

Правила:

- карточка персонажа и линия сохраняются целиком: экран отправляет снимок, сервис проверяет
  названия, порядок, существование участников и обновляет модель;
- пустой `Id` — создание; новый персонаж сразу получает семь канонических шагов арки;
- участник этапа обязан ссылаться на существующего персонажа, иначе `EntityNotFound`;
- удаление персонажа убирает его из этапов всех линий (этап не остаётся со ссылкой в пустоту);
- `ProjectDto.Characters` и `ProjectDto.PlotLines` несут снимки для экранов.

Правила порядка, уникальности и валидации названий живут только здесь. Удаление — мягкое.
`ValidateTitle` проверяет формат (уникальность проверяет команда создания, когда известен
родитель).

Исключение из уникальности — главы: создание и переименование разрешают одинаковые
названия, идентификация остаётся по Guid (ADR 027). `MoveEntityAsync` поддерживает
`EntityKind.PlotLine` и перемещает среди неудалённых элементов с плотной перенумерацией
`Order`; удалённые записи сохраняются. Снимок `PlotLines` отсортирован по `Order`.

## Редактор

### Локальная грамматика (ADR 033)

`IGrammarService.CheckAsync(text, language, token)` возвращает
`Result<IReadOnlyList<GrammarIssue>>`: UTF-16-диапазон, исходный фрагмент, сообщение,
ID правила и варианты замены. Языки: `auto`, `ru`, `en-US`; авто выбирает
преобладающий алфавит абзаца. Отмена бросает OperationCanceledException;
отсутствие runtime, таймаут и повреждённый ответ возвращают ошибку.
`StopAsync` отменяет запросы и закрывает принадлежащий сервису процесс.

Реализация общается только с собственным дочерним LanguageTool на loopback,
без облака и прокси. UI хранит снимок абзаца и поколение запроса, отбрасывает
устаревшие ответы и повторно проверяет текст перед заменой. Замечания визуальные;
исправления проходят через штатный Undo и при необходимости запись правок.
`GrammarCheckEnabled` (по умолчанию false) и `GrammarLanguage` живут в AppPreferences.
Данные книги и номер её схемы не меняются. Качество пунктуации ограничено.

### Правки и комментарии (ADR 032)

`RichDocument.Review` принадлежит Core и включается в глубокое клонирование,
версии сцен, JSON/SQLite/ZIP/recovery. `TextComment` содержит автора, UTC-дату,
цитату, UTF-16-диапазон, ID граничных абзацев, признаки решения и удаления текста.
`TrackedTextChange` хранит исходный документ без вложенной истории и диапазон
замены. Пересекающиеся правки объединяются; независимые можно отклонять отдельно.
`ReviewEditing` изменяет копии в памяти; запись выполняется прежним контрактом
`UpdateSceneDocumentAsync` + `SaveAsync` с проверкой ревизии и отказа записи.

`IReviewEditorSession` связывает команды панели со штатным Undo видимого редактора:
`ReadScenes` возвращает копии, `Selection` — диапазон внутри одной сцены,
`ApplyScene` создаёт один шаг Undo, `SelectRange` переводит к цитате. Жизненный цикл
сессии совпадает с представлением редактора; она не является singleton-сервисом.
Подсветка комментариев и правок — только визуальная; экспорт пишет текущую рукопись.


### `IEditorEngine` — сессия одной сцены

```csharp
Guid? SceneId { get; }
long BaseRevision { get; }
bool IsDirty { get; }
bool CanUndo { get; }
bool CanRedo { get; }
string PlainText { get; }
TextStatisticsDto Statistics { get; }

Task<Result<SceneDocumentDto>> OpenSceneAsync(Guid sceneId, CancellationToken ct = default);
Task<Result> CloseAsync(CancellationToken ct = default);

Result ReplaceRange(TextRange range, string text);
Result InsertText(TextPosition position, string text);
Result DeleteRange(TextRange range);
Result ApplyFormatting(TextRange range, TextFormat format);
Result<TextFormat> GetFormatting(TextRange range);
Result ApplyStyle(
    TextRange range,
    string? fontFamily = null, bool changeFontFamily = false,
    double? fontSize = null, bool changeFontSize = false,
    TextColor? foreground = null, TextColor? highlight = null);
Result<SpanStyleDto> GetStyle(TextRange range);
Result<BlockStyleDto> GetBlockStyleAt(TextPosition position);
Result ApplyBlockStyle(TextRange range, BlockKind? kind = null, TextAlignment? alignment = null, int? indentLevel = null);

Result Undo();
Result Redo();
Result<SearchResult> Search(string query, SearchOptions? options = null);
Result<UpdateSceneDocumentRequest> CreateCommitRequest();
Task<Result<SceneDocumentDto>> CommitAsync(CancellationToken ct = default);
```

Правила (ТЗ п. 21, 22, 23, 37):

- **Единицы смещений** — `TextOffsetUnit.Utf16CodeUnit` от начала плоского текста.
  Все операции используют одни и те же единицы.
- **Один владелец истории отмены** — движок. Контрол не ведёт отдельную историю.
- `ApplyFormatting` переключает флаги: если все символы диапазона уже имеют их — снимает,
  иначе устанавливает. Форматирование **не меняет** число и порядок блоков.
- `ApplyStyle` задаёт только переданные поля оформления (шрифт, кегль, цвета) и
  не трогает символьные флаги, поэтому панель вызывает её независимо от
  переключателей начертания. Параметры `change*` отличают «не задавать поле» от
  «задать поле значением `null`» — так снимается явный шрифт или кегль и
  оформление возвращается к настройкам проекта. `TextColor.None` снимает явный
  цвет, и он наследуется от темы.
- `GetStyle` отдаёт `SpanStyleDto` для панели: незаданные цвета приходят пустой
  строкой, а не `#00000000`, иначе снятие цвета было бы неотличимо от его выбора.
- Сравнение и объединение соседних спанов идёт по всему `SpanStyle`: два
  фрагмента с одинаковыми флагами, но разными цветами остаются разными
  (ТЗ п. 43).
- `ApplyBlockStyle` с пустым диапазоном меняет блок под курсором; иначе — все блоки,
  пересекающиеся с диапазоном.
- `CommitAsync` передаёт документ владельцу проекта, обновляет `BaseRevision`, сбрасывает
  `IsDirty` и сохраняет историю отмены. Конфликт ревизии возвращает
  `EditorOperationConflict`.
- `CreateCommitRequest` — запрос для сохранения сторонним вызывающим кодом.
- Восстановление форматирования при откате обеспечивается снимком (см.
  `docs/decisions/003-text-editor-component.md`).

## Настройки

### Локальные инструменты текста (ADR 030)

`ITextToolsService` находится в `Contracts/Editor`:

```csharp
Task<IReadOnlyList<SpellingIssue>> CheckAsync(string text, CancellationToken cancellationToken = default);
Task<IReadOnlyList<string>> SuggestAsync(string word, CancellationToken cancellationToken = default);
Task<IReadOnlyList<string>> SynonymsAsync(string word, CancellationToken cancellationToken = default);
```

`SpellingIssue(Start, Length, Word)` использует UTF-16 от начала переданного текста.
Результаты упорядочены по Start. Отмена передаётся как OperationCanceledException;
недоступность ресурса — исключение, которое UI отображает в статусе проверки.
Проверка и предложения работают без сети; отсутствие синонимов не означает ошибку.
Изменять документ сервис не может. UI отбрасывает устаревшие результаты и перед
заменой сверяет исходное слово и принадлежность абзаца текущему документу.
Волны не входят в сериализацию/экспорт. Исправление проходит через существующую
историю редактора и сохраняет оформление неизменённых букв и стиль заменяемых.

`AppPreferences.SpellCheckEnabled` и `AutoCapitalizeSentences` по умолчанию true,
`PersonalDictionary` — пустой список. Поля необязательны в старых настройках;
формат `.scri` не меняется. Автозаглавная применяется только к новому набранному
слову после границы предложения, по следующему пробелу. Ручная строчная буква,
вставка текста и отмена не запускают повторное исправление.

### `IAppSettingsService`

```csharp
AppPreferences Current { get; }
Task<Result> LoadAsync(CancellationToken ct = default);
Task<Result> SaveAsync(CancellationToken ct = default);
Task<Result> UpdateAsync(Action<AppPreferences> change, CancellationToken ct = default);
Task<Result> AddRecentProjectAsync(RecentProjectEntry entry, CancellationToken ct = default);
Task<Result> RemoveRecentProjectAsync(string filePath, CancellationToken ct = default);
```

`UpdateAsync` применяет изменение к `Current` и записывает файл настроек одной операцией:
экран настроек не должен оставлять «применённое, но не сохранённое» состояние.

Статистика использования (ADR 025) добавляет необязательные поля `AppPreferences`:
`ShareUsageStatistics` (`null` — выбор не сделан), `StatisticsInstallationId`,
`StatisticsLastSentDay` и `StatisticsLastSentVersion`. Старые настройки читаются
без миграции. Изменение других настроек не заменяет `null` на согласие/отказ.
Идентификатор появляется только после согласия и успешного сохранения.
Интерфейс `IAppSettingsService` и формат файлов проектов не меняются.

Навигация (ADR 026): `AppPreferences.NavigationMode` имеет тип `AppNavigationMode?`
(`Tabs = 0`, `Menu = 1`, владелец перечисления — Core). `null` сохраняет прежний
режим темы: Starter/GreenStart — вкладки, Cabinet/Jam — меню. Явный выбор сохраняется
независимо от последующих изменений темы. Старые файлы настроек читаются без миграции.

Темы (ADR 026): пользователь выбирает Starter («Стандартная», синяя),
GreenStart («Зелёный старт»), Jam («Джем») или Cabinet («Кабинет»).
`AppTheme.GreenStart = 11` добавлен без перенумерации прежних значений;
Starter остаётся темой по умолчанию. Starter и GreenStart используют стандартную
компоновку, Cabinet и Jam — компактную. Все четыре значения сохраняются и
восстанавливаются и при запуске, и при создании оболочки.

### `ICultureService`

```csharp
string Current { get; }
void Apply(string language);
```

Применяет выбранный язык к форматам даты и чисел. Неизвестный код языка не меняет
текущую культуру и не считается ошибкой: настройка остаётся сохранённой (ADR 007).
Интерфейс предлагает `ru-RU`, `en-US` и `zh-CN` (упрощённый китайский, ADR 026).
Смена языка переводит служебные подписи; пользовательские тексты проекта не меняются.

## Навигация и диалоги

Справка (ADR 029): `IHelpService.Show(string topicId = "start")` в `Contracts/Dialogs`
открывает или активирует одно немодальное окно, не меняя текущий экран и проект.
Неизвестная тема переводит к начальной. Статьи и полные лицензии встроены в сборку;
F1 и меню используют один сервис. Справка русская, оригиналы лицензий сохранены.

Обучение (ADR 034): `HelpViewModel.OpenTrainingCommand` запрашивает отдельное окно
презентации. Одновременно открыто не более одного окна обучения на окно справки.
`TrainingViewModel` читает встроенные уроки, ограничивает навигацию их диапазоном
и не обращается к сервисам проекта. Слайды и текст доступны офлайн, без открытой
книги. Закрытие справки закрывает обучение и освобождает изображения и подписки.
Схема книги остаётся 6. Редактируемый PPTX входит в каталог Documentation поставки.

```csharp
// INavigationService (Contracts/Navigation)
AppScreen Current { get; }
event EventHandler<AppScreen>? Navigated;
bool CanNavigateTo(AppScreen screen);
Result NavigateTo(AppScreen screen, object? parameter = null);
Result GoBack();

// IFileDialogService (Contracts/Dialogs)
Task<string?> PickProjectToOpenAsync(string? suggestedDirectory, CancellationToken ct = default);
Task<string?> PickProjectToCreateAsync(string suggestedFileName, string? suggestedDirectory, CancellationToken ct = default);
Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, CancellationToken ct = default);
Task ShowErrorAsync(string title, string message, CancellationToken ct = default);

// IThemeService (Presentation/Theme)
AppTheme Current { get; }
void Apply(AppTheme theme);
```

Экран доступен только если зарегистрирован модулем (`RegisterModules`); иначе
`NavigateTo` возвращает ошибку и функция не должна предлагаться пользователю (ТЗ п. 34).

## Абстракции ядра

```csharp
// Core/Events/IEventBus — подписка возвращает токен, отписка обязательна (ТЗ п. 43)
IDisposable Subscribe<TEvent>(Action<TEvent> handler);
void Publish<TEvent>(TEvent message);

// Core/ISystemClock — проверяемое время
DateTimeOffset UtcNow { get; }
```

## События (ТЗ п. 25)

`ProjectOpened`, `ProjectClosed`, `ProjectChanged`, `BookChanged`, `ChapterChanged`,
`SceneChanged`, `EditorDocumentChanged`, `SaveStarted`, `SaveCompleted`, `SaveFailed`,
`RecoveryDetected`, `ProjectImported`, `ThemeChanged` (`Contracts/Events/AppEvents.cs`).
`ProjectImported` публикуется при открытии проекта с преобразованием данных —
чужой формат или миграция схемы — и несёт исходный формат и список предупреждений
о приблизительности. Открытие файла текущего формата без миграции событие не шлёт.

Событие содержит идентификаторы и короткие значения, сериализуемо и не передаёт
ссылку на View или UI.
