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
- `UpdateSceneDocumentAsync` требует `BaseRevision`, равную текущей ревизии сцены;
  иначе возвращается `EditorOperationConflict` (защита от потери чужих правок).
  При успехе ревизия увеличивается на 1 и публикуется `SceneChanged`.
- `UpdateSettingsAsync` проверяет значения (размер шрифта `8…72`, интервал автосохранения
  `5…3600`, непустые шрифт и язык), применяет их и сразу записывает файл проекта
  (ADR 007). Без открытого проекта возвращается `ProjectNotOpen`.
- `UpdateSceneAsync` принимает `IsKey` (отметка «ключевая сцена»); `null` — не менять.
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

## Редактор

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

### `ICultureService`

```csharp
string Current { get; }
void Apply(string language);
```

Применяет выбранный язык к форматам даты и чисел. Неизвестный код языка не меняет
текущую культуру и не считается ошибкой: настройка остаётся сохранённой (ADR 007).

## Навигация и диалоги

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
