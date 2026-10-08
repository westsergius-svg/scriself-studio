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
using ScriSelfStudio.Presentation;

/// <summary>
/// Экран «Редактор» (ТЗ п. 11, 24, 27): сплошная лента главы, разделённая на сцены.
/// Текст правится обычным текстовым полем — собственного движка ввода у приложения
/// нет, поэтому работают штатные сочетания клавиш Windows (Ctrl+Z, Ctrl+A, …).
/// Модель документа и запись в проект остаются за <see cref="IEditorEngine"/>.
/// </summary>
public sealed partial class EditorViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutosaveTick = TimeSpan.FromSeconds(5);

    private readonly IEditorEngine _engine;
    private readonly IProjectService _projects;
    private readonly INavigationService _navigation;
    private readonly IFileDialogService _dialogs;
    private readonly ISystemClock _clock;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly DispatcherTimer _autosaveTimer;

    private bool _suspendTextSync;
    private Guid? _loadedSceneId;
    private DateTimeOffset _lastActivity;
    private string _text = string.Empty;
    private string? _appliedFontFamily;
    private double _appliedFontSize;
    private SceneBlockViewModel? _activeBlock;

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
    private bool _isFullCanvas;

    /// <summary>
    /// Лента главы: по блоку на сцену, в порядке следования. Текст пишется внутри
    /// блока, активная сцена подсвечивается.
    /// </summary>
    public ObservableCollection<SceneBlockViewModel> Blocks { get; } = [];

    public EditorViewModel(
        IEditorEngine engine,
        IProjectService projects,
        INavigationService navigation,
        IFileDialogService dialogs,
        IEventBus events,
        ISystemClock? clock = null)
    {
        _engine = engine;
        _projects = projects;
        _navigation = navigation;
        _dialogs = dialogs;
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
        _subscriptions.Add(events.Subscribe<SceneChanged>(_ => RebuildRibbon()));
        _subscriptions.Add(events.Subscribe<ProjectOpened>(_ => RebuildRibbon()));

        if (_engine.SceneId is { } existingScene)
        {
            _loadedSceneId = existingScene;
            LoadSceneMetadata(existingScene);
            SyncTextFromEngine();
            RebuildRibbon();
            StartAutosave();
        }
    }

    /// <summary>Движок сцены: владелец модели документа открытой сцены (ТЗ п. 22).</summary>
    public IEditorEngine Engine => _engine;

    public bool IsSceneOpen => _engine.SceneId is not null;

    public bool IsDirty => _engine.IsDirty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Отступ холста от краёв рабочей области.</summary>
    public Thickness CanvasOuterMargin => IsFullCanvas
        ? new Thickness(20, 12, 20, 12)
        : new Thickness(24);

    /// <summary>Отступ «бумаги» внутри холста: в полном режиме лента занимает всю ширину.</summary>
    public Thickness CanvasPaperMargin => IsFullCanvas
        ? new Thickness(0, 10, 0, 10)
        : new Thickness(24, 8, 24, 8);

    /// <summary>Хлебные крошки открытой сцены: «Глава … › Сцена …» (макет).</summary>
    public string BreadcrumbText { get; private set; } = string.Empty;

    /// <summary>Номер открытой сцены в главе (с единицы).</summary>
    public int SceneNumber { get; private set; }

    public string SceneMarkerText => IsSceneOpen ? $"СЦЕНА {SceneNumber}" : string.Empty;

    public string PreviousSceneLabel { get; private set; } = string.Empty;

    public string NextSceneLabel { get; private set; } = string.Empty;

    public bool CanPreviousScene { get; private set; }

    public bool CanNextScene { get; private set; }

    /// <summary>Слов в открытой сцене для статус-бара: «Слов: 482» (макет).</summary>
    public string WordsLabel => $"Слов: {FormatNumber(_engine.Statistics.Words)}";

    public string CharactersLabel => $"Символов: {FormatNumber(_engine.Statistics.Characters)}";

    public string ReadingTimeLabel
    {
        get
        {
            var words = _engine.Statistics.Words;
            if (words <= 0)
            {
                return "~0 мин. чтения";
            }

            var minutes = (int)Math.Ceiling(words / 200.0);
            return $"~{minutes} мин. чтения";
        }
    }

    /// <summary>
    /// Текст активной сцены. Свойство оставлено публичным: сценарии и тесты
    /// наполняют документ напрямую, минуя текстовое поле.
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

    [RelayCommand]
    private void ToggleFullCanvas() => IsFullCanvas = !IsFullCanvas;

    /// <summary>Открывает сцену в редакторе: используется лентой при переходе между блоками.</summary>
    [RelayCommand]
    private Task ActivateBlockAsync(SceneBlockViewModel? block) =>
        block is null ? Task.CompletedTask : ActivateAsync(block);

    [RelayCommand(CanExecute = nameof(IsSceneOpen))]
    private Task SaveAsync(CancellationToken cancellationToken) => SaveCoreAsync(cancellationToken, silent: false);

    [RelayCommand(CanExecute = nameof(IsSceneOpen))]
    private async Task CloseSceneAsync(CancellationToken cancellationToken)
    {
        if (_engine.IsDirty)
        {
            var save = await _dialogs.ConfirmAsync(
                "Несохранённые изменения",
                "В главе есть несохранённые изменения. Сохранить перед закрытием?",
                "Сохранить",
                "Закрыть без сохранения",
                cancellationToken);

            if (save)
            {
                await SaveCoreAsync(cancellationToken, silent: false);
                if (_engine.IsDirty)
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

    public void Dispose()
    {
        _autosaveTimer.Stop();
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private async Task OpenNeighborAsync(int delta, CancellationToken cancellationToken)
    {
        var target = NeighboringScene(delta);
        if (target is null)
        {
            return;
        }

        if (_engine.IsDirty)
        {
            var committed = await _engine.CommitAsync(cancellationToken);
            if (!committed.IsSuccess)
            {
                ReportInPlace(committed.Error!);
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

        if (_engine.IsDirty)
        {
            var committed = await _engine.CommitAsync(CancellationToken.None);
            if (!committed.IsSuccess)
            {
                ReportInPlace(committed.Error!);
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
        RefreshState();
    }

    private void SyncTextFromEngine()
    {
        _suspendTextSync = true;
        Text = _engine.PlainText;
        _suspendTextSync = false;
        _activeBlock?.SetTextSilently(Text);
        RefreshState();
    }

    /// <summary>Пересобирает ленту главы: блоки сцен, снимки текста и активная сцена.</summary>
    private void RebuildRibbon()
    {
        var activeId = _engine.SceneId;
        var located = LocateScene(activeId);

        Blocks.Clear();
        _activeBlock = null;
        HasChapter = located is not null;
        ChapterTitle = located?.Chapter.Title ?? string.Empty;

        if (located is not { } place)
        {
            return;
        }

        foreach (var scene in place.Chapter.Scenes.OrderBy(scene => scene.Order))
        {
            var document = _projects.GetSceneDocument(scene.Id);
            var text = document.IsSuccess ? document.Require().Document.ToPlainText() : string.Empty;
            var block = new SceneBlockViewModel(scene.Id, scene.Order, scene.Title, scene.Status, text, OnBlockTextChanged)
            {
                IsActive = scene.Id == activeId,
            };
            if (block.IsActive)
            {
                _activeBlock = block;
            }

            Blocks.Add(block);
        }
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
        _activeBlock = null;
        HasChapter = false;
        ChapterTitle = string.Empty;
        RefreshState();
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
        if (settings is not { AutoSaveEnabled: true } || !_engine.IsDirty)
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

    private async Task SaveCoreAsync(CancellationToken cancellationToken, bool silent)
    {
        if (_engine.SceneId is null)
        {
            return;
        }

        ClearMessages();
        if (_engine.IsDirty)
        {
            var committed = await _engine.CommitAsync(cancellationToken);
            if (!committed.IsSuccess)
            {
                await ReportAsync(committed.Error!, silent);
                return;
            }
        }

        var saved = await _projects.SaveAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            await ReportAsync(saved.Error!, silent);
            return;
        }

        _lastActivity = _clock.UtcNow;
        if (!silent)
        {
            StatusMessage = "Глава сохранена.";
        }

        RefreshState();
    }

    private void RefreshState()
    {
        RefreshEditorFont();
        OnPropertyChanged(nameof(IsSceneOpen));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(WordsLabel));
        OnPropertyChanged(nameof(CharactersLabel));
        OnPropertyChanged(nameof(ReadingTimeLabel));
        RefreshSceneNavigation();
        SaveCommand.NotifyCanExecuteChanged();
        CloseSceneCommand.NotifyCanExecuteChanged();
        PreviousSceneCommand.NotifyCanExecuteChanged();
        NextSceneCommand.NotifyCanExecuteChanged();
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
        PreviousSceneLabel = NeighboringScene(-1) is { } previous ? $"Сцена {previous.Order + 1}" : string.Empty;
        NextSceneLabel = NeighboringScene(1) is { } next ? $"Сцена {next.Order + 1}" : string.Empty;
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
        => number.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));

    private async Task ReportAsync(AppError error, bool silent)
    {
        ErrorMessage = error.SuggestedAction is null
            ? error.UserMessage
            : $"{error.UserMessage} {error.SuggestedAction}";

        if (!silent)
        {
            await _dialogs.ShowErrorAsync("Ошибка редактора", ErrorMessage);
        }
    }

    private void ReportInPlace(AppError error) =>
        ErrorMessage = error.SuggestedAction is null
            ? error.UserMessage
            : $"{error.UserMessage} {error.SuggestedAction}";

    private void ClearMessages()
    {
        ErrorMessage = null;
        StatusMessage = null;
    }
}