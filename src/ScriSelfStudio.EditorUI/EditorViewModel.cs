namespace ScriSelfStudio.EditorUI;

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Events;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Localization;

public sealed record EditorPreviewRun(
    string Text,
    string? FontFamily,
    double? FontSize,
    bool IsBold,
    bool IsItalic,
    bool IsUnderline,
    bool IsStrikethrough,
    string? Foreground);

public sealed record EditorPreviewBlock(
    IReadOnlyList<EditorPreviewRun> Runs,
    ScriSelfStudio.Core.Text.TextAlignment Alignment,
    BlockKind Kind);

/// <summary>
/// Экран «Редактор» (ТЗ п. 11, 24, 27): сплошная лента главы, разделённая на сцены.
/// The rich-text view edits the combined ribbon while <see cref="IEditorEngine"/>
/// and the project service keep each scene document separate for persistence.
/// </summary>
public sealed partial class EditorViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutosaveTick = TimeSpan.FromSeconds(5);

    private readonly IEditorEngine _engine;
    private readonly IProjectService _projects;
    private readonly INavigationService _navigation;
    private readonly IFileDialogService _dialogs;
    private readonly ISystemClock _clock;
    private readonly ISceneVersionsDialogService _versionsDialog;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly DispatcherTimer _autosaveTimer;
    private readonly Dictionary<Guid, RichDocument> _sceneDocuments = [];
    private readonly Dictionary<Guid, RichDocument> _persistedSceneDocuments = [];
    private readonly Dictionary<Guid, long> _sceneRevisions = [];
    private readonly HashSet<Guid> _pendingSceneDocuments = [];

    private bool _suspendTextSync;
    private bool _suppressRibbonRefresh;
    private bool _ribbonRefreshPending;
    private Guid? _loadedSceneId;
    private int _ribbonCaretLine = 1;
    private DateTimeOffset _lastActivity;
    private string _text = string.Empty;
    private string? _appliedFontFamily;
    private double _appliedFontSize;
    private SceneBlockViewModel? _activeBlock;
    private int _selectionStart;
    private int _selectionEnd;

    [ObservableProperty]
    private string _sceneTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    /// <summary>Заголовок главы над лентой (макет: крупный заголовок главы).</summary>
    [ObservableProperty]
    private string _chapterTitle = string.Empty;

    /// <summary>Есть ли открытая глава: без неё лента пуста и показывается подсказка.</summary>
    [ObservableProperty]
    private bool _hasChapter;

    /// <summary>Шрифт текста из настроек проекта (ТЗ п. 27).</summary>
    [ObservableProperty]
    private string _editorFontFamily = "Inter";

    /// <summary>Кегль текста из настроек проекта (ТЗ п. 27).</summary>
    [ObservableProperty]
    private double _editorFontSize = 16d;

    /// <summary>
    /// Режим «Полный холст»: панели оболочки прячутся, лента растягивается почти
    /// на всю ширину окна — остаются только отступы по 20px (ТЗ: режим холста).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasOuterMargin))]
    [NotifyPropertyChangedFor(nameof(CanvasPaperMargin))]
    [NotifyPropertyChangedFor(nameof(ShowReadingZoom))]
    private bool _isFullCanvas;

    /// <summary>Упорядоченные сцены главы, отображаемые как единый поток текста.</summary>
    public ObservableCollection<SceneBlockViewModel> Blocks { get; } = [];

    public ObservableCollection<EditorPreviewBlock> PreviewBlocks { get; } = [];

    public RichDocument RibbonDocument => RichDocumentAdapter.ToRibbonDocument(
        Blocks.Select(block => _sceneDocuments.TryGetValue(block.SceneId, out var document)
            ? document
            : RichDocument.CreateEmpty()));

    public string CurrentCaretSceneTitle
    {
        get
        {
            var scene = GetSceneAtCaretLine(_ribbonCaretLine) ?? LocateScene(_engine.SceneId)?.Scene;
            if (scene is null)
            {
                return string.Empty;
            }

            return scene.Title.StartsWith("Сцена ", StringComparison.CurrentCultureIgnoreCase)
                ? scene.Title
                : Loc.F("Сцена {0}", scene.Title);
        }
    }

    public IReadOnlyList<string> FontFamilies { get; } =
        ["Georgia", "Inter", "Arial", "Times New Roman", "Consolas"];

    public IReadOnlyList<string> FontSizes { get; } =
        ["10", "11", "12", "14", "16", "18", "20", "24", "28", "32", "36"];

    [ObservableProperty]
    private string _selectedFormattingFontFamily = "Georgia";

    [ObservableProperty]
    private string _selectedFormattingFontSize = "14";

    [ObservableProperty]
    private bool _isBold;

    [ObservableProperty]
    private bool _isItalic;

    [ObservableProperty]
    private bool _isUnderline;

    [ObservableProperty]
    private bool _isStrikethrough;

    public bool HasTextSelection => _selectionStart != _selectionEnd;

    public TextToolsViewModel TextTools { get; }
    public GrammarViewModel Grammar { get; }

    public ReviewPanelViewModel ReviewPanel { get; } = new();

    public void SetRibbonCaretLine(int line)
    {
        var nextLine = Math.Max(1, line);
        if (_ribbonCaretLine == nextLine)
        {
            return;
        }

        _ribbonCaretLine = nextLine;
        OnPropertyChanged(nameof(CurrentCaretSceneTitle));
    }

    public EditorViewModel(
        IEditorEngine engine,
        IProjectService projects,
        INavigationService navigation,
        IFileDialogService dialogs,
        IEventBus events,
        TextToolsViewModel textTools,
        GrammarViewModel grammar,
        ISceneVersionsDialogService versionsDialog,
        ISystemClock? clock = null)
    {
        _engine = engine;
        _projects = projects;
        _navigation = navigation;
        _dialogs = dialogs;
        TextTools = textTools;
        Grammar = grammar;
        _versionsDialog = versionsDialog;
        _clock = clock ?? new SystemClock();
        _lastActivity = _clock.UtcNow;

        _autosaveTimer = new DispatcherTimer { Interval = AutosaveTick };
        _autosaveTimer.Tick += (_, _) => OnAutosaveTick();

        var settings = _projects.Current?.Settings;
        _appliedFontFamily = settings?.FontFamily ?? "Inter";
        _appliedFontSize = settings?.FontSize ?? 16d;
        EditorFontFamily = _appliedFontFamily;
        EditorFontSize = _appliedFontSize;

        _subscriptions.Add(events.Subscribe<EditorDocumentChanged>(OnDocumentChanged));
        _subscriptions.Add(events.Subscribe<ProjectClosed>(_ => OnProjectClosed()));
        _subscriptions.Add(events.Subscribe<SaveCompleted>(_ => RefreshEditorFont()));
        _subscriptions.Add(events.Subscribe<SceneChanged>(_ => OnSceneChanged()));
        _subscriptions.Add(events.Subscribe<ProjectOpened>(_ => RebuildRibbon()));
        _subscriptions.Add(events.Subscribe<EditorDocumentChanged>(_ => RefreshFormattedPreview()));
        _navigation.Navigated += OnNavigated;
        LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;

        if (_engine.SceneId is { } existingScene)
        {
            _loadedSceneId = existingScene;
            LoadSceneMetadata(existingScene);
            SyncTextFromEngine();
            RebuildRibbon();
            StartAutosave();
            RefreshFormattedPreview();
        }
    }

    /// <summary>Движок сцены: владелец модели документа открытой сцены (ТЗ п. 22).</summary>
    public IEditorEngine Engine => _engine;

    public bool IsSceneOpen => _engine.SceneId is not null;

    public bool IsDirty => _engine.IsDirty || _pendingSceneDocuments.Count > 0;

    public bool CanUndo => _engine.CanUndo;

    public bool CanRedo => _engine.CanRedo;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Отступ холста от краёв рабочей области.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasOuterMargin))]
    [NotifyPropertyChangedFor(nameof(ShowReadingZoom))]
    private bool _isCabinetLayout;

    [ObservableProperty]
    private bool _isJamLayout;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasOuterMargin))]
    [NotifyPropertyChangedFor(nameof(ShowReadingZoom))]
    private bool _isStudioLayout;

    [ObservableProperty]
    private bool _isToolbarMoreOpen;

    [RelayCommand]
    private void ToggleToolbarMore() => IsToolbarMoreOpen = !IsToolbarMoreOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadingZoomLabel))]
    private double _readingZoom = 1.15;

    public string ReadingZoomLabel => $"{ReadingZoom * 100:0}%";
    public bool ShowReadingZoom => IsCabinetLayout && !IsStudioLayout && !IsFullCanvas;

    [RelayCommand]
    private void IncreaseReadingZoom() => ReadingZoom = Math.Min(1.75, Math.Round(ReadingZoom + 0.1, 2));

    [RelayCommand]
    private void DecreaseReadingZoom() => ReadingZoom = Math.Max(0.75, Math.Round(ReadingZoom - 0.1, 2));

    public Thickness CanvasOuterMargin => IsFullCanvas
        ? new Thickness(20, 12, 20, 12)
        : IsStudioLayout ? new Thickness(0)
        : IsCabinetLayout ? new Thickness(20, 12, 20, 8) : new Thickness(24);

    /// <summary>Отступ «бумаги» внутри холста: в полном режиме лента занимает всю ширину.</summary>
    public Thickness CanvasPaperMargin => IsFullCanvas
        ? new Thickness(0, 10, 0, 10)
        : new Thickness(24, 8, 24, 8);

    /// <summary>Хлебные крошки открытой сцены: «Глава … › Сцена …» (макет).</summary>
    public string BreadcrumbText { get; private set; } = string.Empty;

    /// <summary>Номер открытой сцены в главе (с единицы).</summary>
    public int SceneNumber { get; private set; }

    public string SceneMarkerText => IsSceneOpen ? Loc.F("СЦЕНА {0}", SceneNumber) : string.Empty;

    public string PreviousSceneLabel { get; private set; } = string.Empty;

    public string NextSceneLabel { get; private set; } = string.Empty;

    public bool CanPreviousScene { get; private set; }

    public bool CanNextScene { get; private set; }

    /// <summary>Слов в открытой сцене для статус-бара: «Слов: 482» (макет).</summary>
    public string WordsLabel => Loc.F("Слов: {0}", FormatNumber(_engine.Statistics.Words));

    public string CharactersLabel => Loc.F("Символов: {0}", FormatNumber(_engine.Statistics.Characters));

    public string ReadingTimeLabel
    {
        get
        {
            var words = _engine.Statistics.Words;
            if (words <= 0)
            {
                return Loc.T("~0 мин. чтения");
            }

            var minutes = (int)Math.Ceiling(words / 200.0);
            return Loc.F("~{0} мин. чтения", minutes);
        }
    }

    /// <summary>
    /// Текст активной сцены. The editor control synchronizes user input through this property.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            if (value == _text || !SetProperty(ref _text, value))
            {
                return;
            }

            if (!_suspendTextSync)
            {
                ApplyTextChange(value);
            }
        }
    }

    public void SynchronizeRichDocument(RichDocument document)
    {
        var plainText = document.ToPlainText().Replace("\r\n", "\n").Replace('\r', '\n');
        _suspendTextSync = true;
        try
        {
            Text = plainText;
        }
        finally
        {
            _suspendTextSync = false;
        }

        var result = _engine.ReplaceDocument(document);
        if (!result.IsSuccess)
        {
            ReportInPlace(result.Error!);
            return;
        }

        _activeBlock?.SetTextSilently(Text);
        UpdateActiveRibbonDocument();
        ErrorMessage = null;
        RefreshFormattedPreview();
        RefreshState();
    }

    public bool SynchronizeRibbonDocument(RichDocument document)
    {
        if (Blocks.Count == 0)
        {
            return false;
        }

        IReadOnlyList<RichDocument> sceneDocuments;
        try
        {
            sceneDocuments = RichDocumentAdapter.SplitRibbonDocument(document, Blocks.Count);
        }
        catch (InvalidDataException error)
        {
            ReportRichEditorError(error.Message);
            return false;
        }

        for (var index = 0; index < Blocks.Count; index++)
        {
            var scene = Blocks[index];
            var sceneDocument = sceneDocuments[index];
            _sceneDocuments[scene.SceneId] = sceneDocument.Clone();
            scene.SetTextSilently(sceneDocument.ToPlainText());

            if (scene.SceneId == _engine.SceneId)
            {
                SynchronizeRichDocument(sceneDocument);
            }
            else if (_persistedSceneDocuments.TryGetValue(scene.SceneId, out var persisted)
                && AreDocumentsEquivalent(persisted, sceneDocument))
            {
                _pendingSceneDocuments.Remove(scene.SceneId);
            }
            else
            {
                _pendingSceneDocuments.Add(scene.SceneId);
            }
        }

        _lastActivity = _clock.UtcNow;
        RefreshState();
        return true;
    }

    public void ReportRichEditorError(string message) => ErrorMessage = message;

    [RelayCommand]
    private void ToggleFullCanvas() => IsFullCanvas = !IsFullCanvas;

    /// <summary>
    /// Выход из режима «Холст» по Esc. Команда доступна только внутри режима, поэтому
    /// вне него Esc не перехватывается и работает как обычно в текстовом поле.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsFullCanvas))]
    private void ExitFullCanvas() => IsFullCanvas = false;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        var result = _engine.Undo();
        if (!result.IsSuccess)
        {
            ReportInPlace(result.Error!);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        var result = _engine.Redo();
        if (!result.IsSuccess)
        {
            ReportInPlace(result.Error!);
        }
    }

    public void SetEditorSelection(int start, int end)
    {
        _selectionStart = Math.Clamp(start, 0, Text.Length);
        _selectionEnd = Math.Clamp(end, 0, Text.Length);
        OnPropertyChanged(nameof(HasTextSelection));
        RefreshFormattingState();
    }

    [RelayCommand]
    private void ToggleBold() => ToggleFormatting(TextFormat.Bold);

    [RelayCommand]
    private void ToggleItalic() => ToggleFormatting(TextFormat.Italic);

    [RelayCommand]
    private void ToggleUnderline() => ToggleFormatting(TextFormat.Underline);

    [RelayCommand]
    private void ToggleStrikethrough() => ToggleFormatting(TextFormat.Strikethrough);

    [RelayCommand]
    private void ApplyForeground(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return;
        }

        ApplyStyle(foreground: TextColor.Parse(colorHex));
    }

    [RelayCommand]
    private void AlignLeft() => ApplyBlockStyle(alignment: TextAlignment.Left);

    [RelayCommand]
    private void AlignCenter() => ApplyBlockStyle(alignment: TextAlignment.Center);

    [RelayCommand]
    private void AlignRight() => ApplyBlockStyle(alignment: TextAlignment.Right);

    [RelayCommand]
    private void ApplyBulletList() => ApplyBlockStyle(kind: BlockKind.BulletListItem);

    [RelayCommand]
    private void ApplyNumberedList() => ApplyBlockStyle(kind: BlockKind.NumberedListItem);

    public void ApplySelectedFontFamily() => ApplyStyle(
        fontFamily: SelectedFormattingFontFamily,
        changeFontFamily: true);

    public void ApplySelectedFontSize()
    {
        if (double.TryParse(
                SelectedFormattingFontSize,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var fontSize))
        {
            ApplyStyle(fontSize: fontSize, changeFontSize: true);
        }
    }

    /// <summary>Открывает сцену в редакторе: используется лентой при переходе между блоками.</summary>
    [RelayCommand]
    private Task ActivateBlockAsync(SceneBlockViewModel? block) =>
        block is null ? Task.CompletedTask : ActivateAsync(block);

    [RelayCommand(CanExecute = nameof(IsSceneOpen))]
    private Task SaveAsync(CancellationToken cancellationToken) => SaveCoreAsync(cancellationToken, silent: false);

    [RelayCommand(CanExecute = nameof(IsSceneOpen))]
    private async Task CloseSceneAsync(CancellationToken cancellationToken)
    {
        if (IsDirty)
        {
            var save = await _dialogs.ConfirmAsync(
                Loc.T("Несохранённые изменения"),
                Loc.T("В главе есть несохранённые изменения. Сохранить перед закрытием?"),
                Loc.T("Сохранить"),
                Loc.T("Закрыть без сохранения"),
                cancellationToken);

            if (save)
            {
                await SaveCoreAsync(cancellationToken, silent: false);
                if (IsDirty || HasError)
                {
                    return;
                }
            }
        }

        await _engine.CloseAsync(cancellationToken);
        CloseSession();
        _navigation.NavigateTo(AppScreen.Architecture);
    }

    [RelayCommand]
    private void GoBack() => _navigation.NavigateTo(AppScreen.Architecture);

    /// <summary>Переход к предыдущей сцене книги (ТЗ п. 27: навигация по сценам).</summary>
    [RelayCommand(CanExecute = nameof(CanPreviousScene))]
    private Task PreviousSceneAsync(CancellationToken cancellationToken) => OpenNeighborAsync(-1, cancellationToken);

    /// <summary>Переход к следующей сцене книги (ТЗ п. 27: навигация по сценам).</summary>
    [RelayCommand(CanExecute = nameof(CanNextScene))]
    private Task NextSceneAsync(CancellationToken cancellationToken) => OpenNeighborAsync(1, cancellationToken);

    public void StartAutosave()
    {
        if (!_autosaveTimer.IsEnabled)
        {
            _autosaveTimer.Start();
        }
    }

    public void StopAutosave() => _autosaveTimer.Stop();

    public async Task AutosaveAsync()
    {
        await SaveCoreAsync(CancellationToken.None, silent: true);
    }

    /// <summary>
    /// Фиксирует текст сцены и сохраняет проект без сообщений в интерфейсе.
    /// Вызывается при закрытии приложения, когда потеря фокуса уже не срабатывает.
    /// </summary>
    public Task<Result> FlushAsync(CancellationToken cancellationToken = default) =>
        SaveCoreAsync(cancellationToken, silent: true);

    /// <summary>Передаёт все документы ленты проекту; запись файла выполняется после остальных панелей.</summary>
    public async Task<Result> CommitPendingEditsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsDirty) return Result.Success;
        var committed = await CommitRibbonDocumentsAsync(cancellationToken);
        if (committed.IsSuccess && _ribbonRefreshPending)
        {
            _ribbonRefreshPending = false;
            RebuildRibbon();
        }
        return committed;
    }

    public void Dispose()
    {
        _autosaveTimer.Stop();
        _navigation.Navigated -= OnNavigated;
        LocalizationManager.Instance.PropertyChanged -= OnLanguageChanged;
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    /// <summary>
    /// Открывает сцену из дерева книги. Перед переключением сохраняет все
    /// несохранённые документы ленты, иначе текст предыдущей сцены терялся.
    /// </summary>
    public async Task<bool> OpenSceneFromBookAsync(Guid sceneId, CancellationToken cancellationToken = default)
    {
        if (_engine.SceneId == sceneId)
        {
            _navigation.NavigateTo(AppScreen.Editor);
            return true;
        }

        if (IsDirty)
        {
            var committed = await CommitRibbonDocumentsAsync(cancellationToken);
            if (!committed.IsSuccess)
            {
                return false;
            }
        }

        var opened = await _engine.OpenSceneAsync(sceneId, cancellationToken);
        if (!opened.IsSuccess)
        {
            ReportInPlace(opened.Error!);
            return false;
        }

        _navigation.NavigateTo(AppScreen.Editor);
        return true;
    }

    /// <summary>
    /// Уход с экрана редактора фиксирует текст текущей сцены. Автосохранение
    /// проекта пишет снимок на диск по настройкам, а commit сохраняет текст в модели.
    /// </summary>
    private void OnNavigated(object? sender, AppScreen screen)
    {
        if (screen == AppScreen.Editor)
        {
            return;
        }

        if (IsDirty)
        {
            _ = SaveCoreAsync(CancellationToken.None, silent: true);
        }
    }

    private async Task OpenNeighborAsync(int delta, CancellationToken cancellationToken)
    {
        var target = NeighboringScene(delta);
        if (target is null)
        {
            return;
        }

        if (IsDirty)
        {
            var committed = await CommitRibbonDocumentsAsync(cancellationToken);
            if (!committed.IsSuccess)
            {
                return;
            }
        }

        var opened = await _engine.OpenSceneAsync(target.Id, cancellationToken);
        if (!opened.IsSuccess)
        {
            ReportInPlace(opened.Error!);
        }
    }

    /// <summary>Переключает активную сцену ленты: сохраняет прежнюю и открывает новую.</summary>
    private async Task ActivateAsync(SceneBlockViewModel block)
    {
        if (ReferenceEquals(block, _activeBlock) || _engine.SceneId == block.SceneId)
        {
            return;
        }

        if (IsDirty)
        {
            var committed = await CommitRibbonDocumentsAsync(CancellationToken.None);
            if (!committed.IsSuccess)
            {
                return;
            }
        }

        var opened = await _engine.OpenSceneAsync(block.SceneId, CancellationToken.None);
        if (!opened.IsSuccess)
        {
            ReportInPlace(opened.Error!);
        }
    }

    /// <summary>Зовётся блоком ленты: правка активной сцены уходит в документ.</summary>
    private void OnBlockTextChanged(SceneBlockViewModel block)
    {
        if (_suspendTextSync || !ReferenceEquals(block, _activeBlock))
        {
            return;
        }

        Text = block.Text;
    }

    private void ApplyTextChange(string value)
    {
        var result = _engine.SetPlainText(value);
        if (!result.IsSuccess)
        {
            ReportInPlace(result.Error!);
            SyncTextFromEngine();
            return;
        }

        _lastActivity = _clock.UtcNow;
        _activeBlock?.SetTextSilently(value);
        UpdateActiveRibbonDocument();
        RefreshState();
    }

    private void UpdateActiveRibbonDocument()
    {
        if (_engine.SceneId is not { } sceneId)
        {
            return;
        }

        var snapshot = _engine.GetDocumentSnapshot();
        if (!snapshot.IsSuccess)
        {
            ReportInPlace(snapshot.Error!);
            return;
        }

        _sceneDocuments[sceneId] = snapshot.Require().Document.Clone();
        OnPropertyChanged(nameof(RibbonDocument));
    }

    private void ToggleFormatting(TextFormat format)
    {
        var result = _engine.ApplyFormatting(CurrentTextRange, format);
        if (!result.IsSuccess)
        {
            ReportInPlace(result.Error!);
            return;
        }

        ErrorMessage = null;
        RefreshFormattingState();
        RefreshFormattedPreview();
        RefreshState();
    }

    private void ApplyStyle(
        string? fontFamily = null,
        bool changeFontFamily = false,
        double? fontSize = null,
        bool changeFontSize = false,
        TextColor? foreground = null)
    {
        var result = _engine.ApplyStyle(
            CurrentTextRange,
            fontFamily: fontFamily,
            changeFontFamily: changeFontFamily,
            fontSize: fontSize,
            changeFontSize: changeFontSize,
            foreground: foreground);
        if (!result.IsSuccess)
        {
            ReportInPlace(result.Error!);
            return;
        }

        ErrorMessage = null;
        RefreshFormattingState();
        RefreshFormattedPreview();
        RefreshState();
    }

    private void ApplyBlockStyle(BlockKind? kind = null, TextAlignment? alignment = null)
    {
        var result = _engine.ApplyBlockStyle(CurrentTextRange, kind, alignment);
        if (!result.IsSuccess)
        {
            ReportInPlace(result.Error!);
            return;
        }

        ErrorMessage = null;
        RefreshFormattedPreview();
        RefreshState();
    }

    private TextRange CurrentTextRange => new(
        new TextPosition(Math.Min(_selectionStart, _selectionEnd)),
        new TextPosition(Math.Max(_selectionStart, _selectionEnd)));

    private void RefreshFormattingState()
    {
        var result = _engine.GetFormatting(CurrentTextRange);
        var flags = result.IsSuccess ? result.Require() : TextFormat.None;
        IsBold = flags.HasFlag(TextFormat.Bold);
        IsItalic = flags.HasFlag(TextFormat.Italic);
        IsUnderline = flags.HasFlag(TextFormat.Underline);
        IsStrikethrough = flags.HasFlag(TextFormat.Strikethrough);
    }

    private void RefreshFormattedPreview()
    {
        PreviewBlocks.Clear();
        var result = _engine.GetDocumentSnapshot();
        if (!result.IsSuccess)
        {
            return;
        }

        var blocks = result.Require().Document.Blocks;
        foreach (var block in blocks)
        {
            var runs = new List<EditorPreviewRun>();
            foreach (var span in block.Spans)
            {
                if (span.Text.Length == 0)
                {
                    continue;
                }

                var style = span.Style;
                runs.Add(new EditorPreviewRun(
                    span.Text,
                    style.FontFamily,
                    style.FontSize,
                    style.Flags.HasFlag(TextFormat.Bold),
                    style.Flags.HasFlag(TextFormat.Italic),
                    style.Flags.HasFlag(TextFormat.Underline),
                    style.Flags.HasFlag(TextFormat.Strikethrough),
                    style.Foreground.IsSet ? style.Foreground.ToHex() : null));
            }

            PreviewBlocks.Add(new EditorPreviewBlock(runs, block.Alignment, block.Kind));
        }
    }

    private void SyncTextFromEngine()
    {
        _suspendTextSync = true;
        Text = _engine.PlainText;
        _suspendTextSync = false;
        _activeBlock?.SetTextSilently(Text);
        _selectionStart = Math.Clamp(_selectionStart, 0, Text.Length);
        _selectionEnd = Math.Clamp(_selectionEnd, 0, Text.Length);
        OnPropertyChanged(nameof(HasTextSelection));
        RefreshFormattingState();
        RefreshFormattedPreview();
        RefreshState();
    }

    /// <summary>Пересобирает ленту главы: блоки сцен, снимки текста и активная сцена.</summary>
    private void RebuildRibbon()
    {
        var activeId = _engine.SceneId;
        var located = LocateScene(activeId);

        Blocks.Clear();
        _sceneDocuments.Clear();
        _persistedSceneDocuments.Clear();
        _sceneRevisions.Clear();
        _pendingSceneDocuments.Clear();
        _ribbonRefreshPending = false;
        _activeBlock = null;
        _ribbonCaretLine = 1;
        HasChapter = located is not null;
        ChapterTitle = located?.Chapter.Title ?? string.Empty;
        OnPropertyChanged(nameof(CurrentCaretSceneTitle));

        if (located is not { } place)
        {
            OnPropertyChanged(nameof(RibbonDocument));
            return;
        }

        foreach (var scene in place.Chapter.Scenes.OrderBy(scene => scene.Order))
        {
            var document = _projects.GetSceneDocument(scene.Id);
            if (!document.IsSuccess)
            {
                ReportInPlace(document.Error!);
                continue;
            }

            var snapshot = document.Require();
            _sceneRevisions[scene.Id] = snapshot.Revision;
            _persistedSceneDocuments[scene.Id] = snapshot.Document.Clone();
            _sceneDocuments[scene.Id] = snapshot.Document.Clone();
            var block = new SceneBlockViewModel(
                scene.Id,
                scene.Order,
                scene.Title,
                scene.Status,
                snapshot.Document.ToPlainText(),
                OnBlockTextChanged)
            {
                IsActive = scene.Id == activeId,
            };
            if (block.IsActive)
            {
                _activeBlock = block;
            }

            Blocks.Add(block);
        }

        if (activeId is { } currentId && _sceneDocuments.ContainsKey(currentId))
        {
            var currentDocument = _engine.GetDocumentSnapshot();
            if (currentDocument.IsSuccess)
            {
                _sceneDocuments[currentId] = currentDocument.Require().Document.Clone();
                var persisted = _persistedSceneDocuments[currentId];
                if (!AreDocumentsEquivalent(persisted, _sceneDocuments[currentId]))
                {
                    _pendingSceneDocuments.Add(currentId);
                }

                _activeBlock?.SetTextSilently(_sceneDocuments[currentId].ToPlainText());
            }
            else
            {
                ReportInPlace(currentDocument.Error!);
            }
        }

        OnPropertyChanged(nameof(RibbonDocument));
        OnPropertyChanged(nameof(CurrentCaretSceneTitle));
        RefreshState();
    }

    private void LoadSceneMetadata(Guid sceneId)
    {
        var located = LocateScene(sceneId);
        SceneTitle = located?.Scene.Title ?? string.Empty;
        ClearMessages();
    }

    private void CloseSession()
    {
        _loadedSceneId = null;
        _suspendTextSync = true;
        Text = string.Empty;
        _suspendTextSync = false;
        SceneTitle = string.Empty;
        Blocks.Clear();
        _sceneDocuments.Clear();
        _persistedSceneDocuments.Clear();
        _sceneRevisions.Clear();
        _pendingSceneDocuments.Clear();
        _activeBlock = null;
        HasChapter = false;
        ChapterTitle = string.Empty;
        OnPropertyChanged(nameof(RibbonDocument));
        OnPropertyChanged(nameof(CurrentCaretSceneTitle));
        RefreshState();
    }

    private void OnSceneChanged()
    {
        if (_suppressRibbonRefresh)
        {
            return;
        }

        if (IsDirty)
        {
            _ribbonRefreshPending = true;
            return;
        }

        RebuildRibbon();
    }

    private SceneDto? GetSceneAtCaretLine(int caretLine)
    {
        var line = 1;
        for (var index = 0; index < Blocks.Count; index++)
        {
            var block = Blocks[index];
            var scene = LocateScene(block.SceneId)?.Scene;
            if (caretLine <= line)
            {
                return scene;
            }

            // Заголовок сцены занимает первую строку её блока в ленте.
            line++;
            var document = _sceneDocuments.GetValueOrDefault(block.SceneId);
            var textLines = document is null
                ? 1
                : Math.Max(1, document.Blocks.Sum(textBlock =>
                    Math.Max(1, CountHardLines(string.Concat(textBlock.Spans.Select(span => span.Text))))));

            if (caretLine <= line + textLines - 1)
            {
                return scene;
            }

            line += textLines;
            if (index < Blocks.Count - 1)
            {
                // Разделитель относится к следующей сцене: курсор на линии
                // разрыва должен показывать уже новую сцену.
                line++;
            }
        }

        return Blocks.Count > 0 ? LocateScene(Blocks[^1].SceneId)?.Scene : null;
    }

    private static int CountHardLines(string text)
    {
        var lines = 1;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private void OnDocumentChanged(EditorDocumentChanged change)
    {
        if (_loadedSceneId != change.SceneId)
        {
            _loadedSceneId = change.SceneId;
            LoadSceneMetadata(change.SceneId);
            SyncTextFromEngine();
            RebuildRibbon();
            StartAutosave();
        }

        RefreshState();
    }

    private void OnProjectClosed()
    {
        CloseSession();
        _ = _engine.CloseAsync();
    }

    private void OnAutosaveTick()
    {
        if (_engine.SceneId is null)
        {
            return;
        }

        var settings = _projects.Current?.Settings;
        if (settings is not { AutoSaveEnabled: true } || !IsDirty)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, settings.AutoSaveIntervalSeconds));
        if (_clock.UtcNow - _lastActivity < interval)
        {
            return;
        }

        _ = AutosaveAsync();
    }

    private async Task<Result> SaveCoreAsync(CancellationToken cancellationToken, bool silent)
    {
        if (_engine.SceneId is null)
        {
            return Result.Success;
        }

        ClearMessages();
        if (IsDirty)
        {
            var committed = await CommitRibbonDocumentsAsync(cancellationToken);
            if (!committed.IsSuccess)
            {
                return committed;
            }
        }

        var saved = await _projects.SaveAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            await ReportAsync(saved.Error!, silent);
            return Result.Failure(saved.Error!);
        }

        _lastActivity = _clock.UtcNow;
        if (!silent)
        {
            StatusMessage = Loc.T("Глава сохранена.");
        }

        if (_ribbonRefreshPending)
        {
            _ribbonRefreshPending = false;
            RebuildRibbon();
        }

        RefreshState();
        return Result.Success;
    }

    private async Task<Result> CommitRibbonDocumentsAsync(CancellationToken cancellationToken)
    {
        _suppressRibbonRefresh = true;
        try
        {
            foreach (var scene in Blocks)
            {
                if (!_pendingSceneDocuments.Contains(scene.SceneId)
                    || scene.SceneId == _engine.SceneId)
                {
                    continue;
                }

                var request = new UpdateSceneDocumentRequest
                {
                    SceneId = scene.SceneId,
                    BaseRevision = _sceneRevisions[scene.SceneId],
                    Document = _sceneDocuments[scene.SceneId].Clone(),
                };
                var updated = await _projects.UpdateSceneDocumentAsync(request, cancellationToken);
                if (!updated.IsSuccess)
                {
                    ReportInPlace(updated.Error!);
                    return Result.Failure(updated.Error!);
                }

                var result = updated.Require();
                _sceneRevisions[scene.SceneId] = result.Revision;
                _persistedSceneDocuments[scene.SceneId] = result.Document.Clone();
                _pendingSceneDocuments.Remove(scene.SceneId);
            }

            if (_engine.IsDirty)
            {
                var committed = await _engine.CommitAsync(cancellationToken);
                if (!committed.IsSuccess)
                {
                    ReportInPlace(committed.Error!);
                    return Result.Failure(committed.Error!);
                }

                var result = committed.Require();
                _sceneRevisions[result.SceneId] = result.Revision;
                _persistedSceneDocuments[result.SceneId] = result.Document.Clone();
                _sceneDocuments[result.SceneId] = result.Document.Clone();
                _pendingSceneDocuments.Remove(result.SceneId);
            }

            RefreshState();
            return Result.Success;
        }
        finally
        {
            _suppressRibbonRefresh = false;
        }
    }

    internal static bool AreDocumentsEquivalent(RichDocument left, RichDocument right)
    {
        if (!ReviewEditing.SameReview(left.Review, right.Review)) return false;
        if (left.SchemaVersion != right.SchemaVersion || left.Blocks.Count != right.Blocks.Count)
        {
            return false;
        }

        for (var blockIndex = 0; blockIndex < left.Blocks.Count; blockIndex++)
        {
            var leftBlock = left.Blocks[blockIndex];
            var rightBlock = right.Blocks[blockIndex];
            if (leftBlock.Kind != rightBlock.Kind
                || leftBlock.Alignment != rightBlock.Alignment
                || leftBlock.IndentLevel != rightBlock.IndentLevel
                || leftBlock.ListMarker != rightBlock.ListMarker
                || leftBlock.Spans.Count != rightBlock.Spans.Count)
            {
                return false;
            }

            for (var spanIndex = 0; spanIndex < leftBlock.Spans.Count; spanIndex++)
            {
                var leftSpan = leftBlock.Spans[spanIndex];
                var rightSpan = rightBlock.Spans[spanIndex];
                if (leftSpan.Text != rightSpan.Text || leftSpan.Style != rightSpan.Style)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void RefreshState()
    {
        RefreshEditorFont();
        RefreshFormattingState();
        OnPropertyChanged(nameof(IsSceneOpen));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(WordsLabel));
        OnPropertyChanged(nameof(CharactersLabel));
        OnPropertyChanged(nameof(ReadingTimeLabel));
        RefreshSceneNavigation();
        SaveCommand.NotifyCanExecuteChanged();
        CloseSceneCommand.NotifyCanExecuteChanged();
        PreviousSceneCommand.NotifyCanExecuteChanged();
        NextSceneCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LocalizationManager.Language)) return;
        OnPropertyChanged(nameof(WordsLabel));
        OnPropertyChanged(nameof(CharactersLabel));
        OnPropertyChanged(nameof(ReadingTimeLabel));
        RefreshSceneNavigation();
        TextTools.RefreshLocalization();
        Grammar.RefreshLocalization();
        ReviewPanel.RefreshLocalization();
    }

    /// <summary>
    /// Пересчитывает номер сцены, подписи и доступность навигации внизу
    /// документа после смены сцены или структуры проекта (ТЗ п. 27).
    /// </summary>
    private void RefreshSceneNavigation()
    {
        var located = LocateScene(_engine.SceneId);
        SceneNumber = located?.Scene.Order + 1 ?? 0;
        BreadcrumbText = located is { } place ? $"{place.Chapter.Title} › {place.Scene.Title}" : string.Empty;
        PreviousSceneLabel = NeighboringScene(-1) is { } previous ? Loc.F("Сцена {0}", previous.Order + 1) : string.Empty;
        NextSceneLabel = NeighboringScene(1) is { } next ? Loc.F("Сцена {0}", next.Order + 1) : string.Empty;
        CanPreviousScene = NeighboringScene(-1) is not null;
        CanNextScene = NeighboringScene(1) is not null;
        OnPropertyChanged(nameof(SceneNumber));
        OnPropertyChanged(nameof(SceneMarkerText));
        OnPropertyChanged(nameof(BreadcrumbText));
        OnPropertyChanged(nameof(PreviousSceneLabel));
        OnPropertyChanged(nameof(NextSceneLabel));
        OnPropertyChanged(nameof(CanPreviousScene));
        OnPropertyChanged(nameof(CanNextScene));
    }

    /// <summary>
    /// Шрифт берётся из настроек проекта и обновляется после их сохранения (ТЗ п. 27).
    /// </summary>
    private void RefreshEditorFont()
    {
        var settings = _projects.Current?.Settings;
        var family = settings?.FontFamily ?? "Inter";
        var size = settings?.FontSize ?? 16d;
        if (family != _appliedFontFamily)
        {
            _appliedFontFamily = family;
            EditorFontFamily = family;
        }

        if (size != _appliedFontSize)
        {
            _appliedFontSize = size;
            EditorFontSize = size;
        }
    }

    private SceneDto? NeighboringScene(int delta)
    {
        var current = _projects.Current;
        if (current is null || _engine.SceneId is not { } sceneId)
        {
            return null;
        }

        var book = current.Series
            .SelectMany(series => series.Books)
            .FirstOrDefault(candidate =>
                candidate.Chapters.Any(chapter => chapter.Scenes.Any(scene => scene.Id == sceneId)));
        if (book is null)
        {
            return null;
        }

        var scenes = book.Chapters
            .OrderBy(chapter => chapter.Order)
            .SelectMany(chapter => chapter.Scenes.OrderBy(scene => scene.Order))
            .ToList();
        var index = scenes.FindIndex(scene => scene.Id == sceneId);
        if (index < 0)
        {
            return null;
        }

        var targetIndex = index + delta;
        return targetIndex >= 0 && targetIndex < scenes.Count ? scenes[targetIndex] : null;
    }

    /// <summary>Находит главу и сцену по идентификатору в текущем снимке проекта.</summary>
    private (ChapterDto Chapter, SceneDto Scene)? LocateScene(Guid? sceneId)
    {
        if (sceneId is not { } id || _projects.Current is not { } snapshot)
        {
            return null;
        }

        foreach (var series in snapshot.Series)
        {
            foreach (var book in series.Books)
            {
                foreach (var chapter in book.Chapters)
                {
                    var scene = chapter.Scenes.FirstOrDefault(candidate => candidate.Id == id);
                    if (scene is not null)
                    {
                        return (chapter, scene);
                    }
                }
            }
        }

        return null;
    }

    private static string FormatNumber(int number)
        => number.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    private async Task ReportAsync(AppError error, bool silent)
    {
        ErrorMessage = error.SuggestedAction is null
            ? Loc.T(error.UserMessage)
            : $"{Loc.T(error.UserMessage)} {Loc.T(error.SuggestedAction)}";

        if (!silent)
        {
            await _dialogs.ShowErrorAsync(Loc.T("Ошибка редактора"), ErrorMessage);
        }
    }

    private void ReportInPlace(AppError error) =>
        ErrorMessage = error.SuggestedAction is null
            ? Loc.T(error.UserMessage)
            : $"{Loc.T(error.UserMessage)} {Loc.T(error.SuggestedAction)}";

    private void ClearMessages()
    {
        ErrorMessage = null;
        StatusMessage = null;
    }
}
