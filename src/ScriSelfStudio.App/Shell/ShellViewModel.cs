namespace ScriSelfStudio.App.Shell;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Events;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Desktop;
using ScriSelfStudio.Presentation.Localization;
using ScriSelfStudio.Presentation.Export;
using ScriSelfStudio.Presentation.Theme;
using ScriSelfStudio.StoryUI;

/// <summary>Тема оформления в списке переключателя тем (макет редактора).</summary>
public sealed record ThemeOption(AppTheme Value, string Title, string Hint, IBrush Swatch)
{
    /// <summary>
    /// Образец цвета темы для кружка в меню (макет: <c>.swatch</c>). Кисть
    /// замораживается: она общая для всех элементов меню и не меняется.
    /// </summary>
    public static IBrush CreateSwatch(string color) => new ImmutableSolidColorBrush(Color.Parse(color));
}

/// <summary>
/// Оболочка приложения: заголовок окна и текущий экран (ТЗ п. 27).
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase, IDisposable
{
    /// <summary>Частота проверки автосохранения: сам интервал берётся из настроек проекта.</summary>
    private static readonly TimeSpan AutosaveTick = TimeSpan.FromSeconds(5);

    private readonly IProjectService _projects;
    private readonly LauncherViewModel _launcher;
    private readonly ArchitectureHostViewModel _architectureHost;
    private readonly EditorViewModel _editor;
    private readonly SettingsViewModel _settings;
    private readonly ExportViewModel _export;
    private readonly INavigationService _navigation;
    private readonly IAppSettingsService _appSettings;
    private readonly IThemeService _theme;
    private readonly ISystemClock _clock;
    private readonly IHelpService _help;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly DispatcherTimer _autosaveTimer;

    private DateTimeOffset _lastProjectActivity;
    private bool _isProjectSaving;

    /// <summary>Темы рабочего пространства.</summary>
    public IReadOnlyList<ThemeOption> Themes { get; private set; } = CreateThemes();

    private static IReadOnlyList<ThemeOption> CreateThemes() =>
    [
        new(AppTheme.Starter, Loc.T("Стандартная"), Loc.T("синяя"), ThemeOption.CreateSwatch("#3B8AE5")),
        new(AppTheme.GreenStart, Loc.T("Зелёный старт"), Loc.T("зелёная"), ThemeOption.CreateSwatch("#38634F")),
        new(AppTheme.Jam, Loc.T("Джем"), Loc.T("прозрачное стекло"), ThemeOption.CreateSwatch("#9E376D")),
        new(AppTheme.Cabinet, Loc.T("Кабинет"), Loc.T("для рукописи"), ThemeOption.CreateSwatch("#38634F")),
        new(AppTheme.Studio, Loc.T("Мокрый асфальт"), Loc.T("графитовое рабочее пространство"), ThemeOption.CreateSwatch("#3785DB")),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThemeMenuOpen))]
    [NotifyPropertyChangedFor(nameof(CurrentThemeName))]
    private ThemeOption? _selectedTheme;

    [ObservableProperty]
    private bool _isThemeMenuOpen;

    [ObservableProperty]
    private object? _current;

    [ObservableProperty]
    private AppScreen? _currentScreen;

    [ObservableProperty]
    private DateTimeOffset? _lastSavedAt;

    public ShellViewModel(
        IProjectService projects,
        LauncherViewModel launcher,
        EditorViewModel editor,
        SettingsViewModel settings,
        ExportViewModel export,
        INavigationService navigation,
        IEventBus events,
        IAppSettingsService appSettings,
        IThemeService theme,
        BookTreeViewModel bookTree,
        SceneContextViewModel sceneContext,
        ArchitectureHostViewModel architectureHost,
        ISystemClock clock,
        IHelpService help)
    {
        _projects = projects;
        _launcher = launcher;
        _editor = editor;
        _settings = settings;
        _export = export;
        _navigation = navigation;
        _appSettings = appSettings;
        _theme = theme;
        _theme.Changed += OnThemeChanged;
        LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;
        _clock = clock;
        _help = help;
        BookTree = bookTree;
        SceneContext = sceneContext;
        _architectureHost = architectureHost;
        _lastProjectActivity = _clock.UtcNow;

        // Автосохранение проекта целиком: правки структуры, персонажей и линий живут
        // в снимке проекта, поэтому их пишет та же настройка, что и текст сцены (ТЗ п. 11).
        _autosaveTimer = new DispatcherTimer { Interval = AutosaveTick };
        _autosaveTimer.Tick += (_, _) => OnAutosaveTick();

        _currentScreen = navigation.Current;
        // Экран синхронизируется с текущим состоянием навигации: оболочка может быть
        // создана после того, как пользователь уже перешёл на другой экран.
        _current = ScreenFor(navigation.Current);

        _navigation.Navigated += OnNavigated;
        // Кнопка «во весь экран» в редакторе тоже переключает панели оболочки: редактор
        // не знает о панелях, поэтому оболочка слушает его IsFullCanvas.
        _editor.PropertyChanged += OnEditorPropertyChanged;
        _subscriptions.Add(events.Subscribe<ProjectOpened>(_ =>
        {
            _lastProjectActivity = _clock.UtcNow;
            SyncProjectState();
            StartProjectAutosave();
        }));
        _subscriptions.Add(events.Subscribe<ProjectClosed>(_ =>
        {
            SyncProjectState();
            _autosaveTimer.Stop();
        }));
        _subscriptions.Add(events.Subscribe<ProjectChanged>(_ =>
        {
            _lastProjectActivity = _clock.UtcNow;
            OnPropertyChanged(nameof(ProjectCaption));
            OnPropertyChanged(nameof(AutosaveText));
            OnPropertyChanged(nameof(IsAutosaveEnabled));
            OnPropertyChanged(nameof(StatusStatsText));
            SaveCommand.NotifyCanExecuteChanged();
        }));
        _subscriptions.Add(events.Subscribe<EditorDocumentChanged>(_ =>
        {
            OnPropertyChanged(nameof(StatusStatsText));
            OnPropertyChanged(nameof(BreadcrumbsText));
        }));
        _subscriptions.Add(events.Subscribe<SaveCompleted>(_ =>
        {
            LastSavedAt = _clock.UtcNow;
            StatusMessage = Loc.T("Проект сохранён.");
            SaveCommand.NotifyCanExecuteChanged();
        }));
        _subscriptions.Add(events.Subscribe<SaveFailed>(eventArgs =>
        {
            StatusMessage = Loc.F("Ошибка сохранения: {0}", eventArgs.Error.UserMessage);
        }));

        SyncProjectState();
        LoadSavedTheme();
    }

    [ObservableProperty]
    private double _bookTreeWidth = 280;

    [ObservableProperty]
    private double _sceneContextWidth = 340;

    public void SetBookTreeWidth(double width) => BookTreeWidth = Math.Clamp(width, MinPaneWidth, MaxPaneWidth);

    public void SetSceneContextWidth(double width) => SceneContextWidth = Math.Clamp(width, MinPaneWidth, MaxPaneWidth);

    public BookTreeViewModel BookTree { get; }

    public SceneContextViewModel SceneContext { get; }

    public bool IsProjectOpen => _projects.Current is not null;

    public string ProjectName => _projects.Current?.Name ?? Loc.T("Проект не открыт");

    public bool IsFullCanvas
    {
        get => _editor.IsFullCanvas;
        set => _editor.IsFullCanvas = value;
    }

    /// <summary>
    /// Esc в окне выключает режим «Холст». Команда принадлежит редактору — владельцу
    /// состояния; оболочка только открывает её окну, чтобы сочетание работало при любом
    /// фокусе внутри окна, а не только когда фокус в редакторе.
    /// </summary>
    public System.Windows.Input.ICommand ExitFullCanvasCommand => _editor.ExitFullCanvasCommand;

    public bool ShowWorkspace => IsProjectOpen
        && (CurrentScreen != AppScreen.Editor || !IsFullCanvas);

    public const double MinPaneWidth = 180;
    public const double MaxPaneWidth = 400;

    public bool ShowScenePanel => ShowWorkspace
        && CurrentScreen == AppScreen.Editor
        && !IsFullCanvas
        && IsScenePanelRequested;

    public bool IsBookTreeAvailable => ShowWorkspace
        && (CurrentScreen == AppScreen.Editor || (IsCompactLayout && !IsStudioTheme && CurrentScreen is
            AppScreen.Architecture or AppScreen.Characters or AppScreen.Timeline or AppScreen.Export));

    public bool ShowBookTree => IsBookTreeAvailable
        && IsBookTreeRequested && (!ShowScenePanel || _windowWidth >= 1120);

    public bool ShowStatusBar => ShowWorkspace && CurrentScreen == AppScreen.Editor;

    public bool IsCabinetTheme => SelectedTheme?.Value == AppTheme.Cabinet;
    public bool IsJamTheme => SelectedTheme?.Value == AppTheme.Jam;
    public bool IsStudioTheme => SelectedTheme?.Value == AppTheme.Studio;
    public bool IsCompactLayout => IsCabinetTheme || IsJamTheme || IsStudioTheme;
    public bool UseDropdownNavigation => (_appSettings.Current.NavigationMode
        ?? (IsCompactLayout ? AppNavigationMode.Menu : AppNavigationMode.Tabs)) == AppNavigationMode.Menu;
    public bool ShowClassicHeader => ShowWorkspace && !UseDropdownNavigation && !IsStudioTheme;
    public bool ShowCabinetChrome => UseDropdownNavigation && !IsFullCanvas && !IsStudioTheme;
    public bool ShowStudioChrome => IsStudioTheme && !IsFullCanvas;

    private double _windowWidth = 1440;
    private bool _bookTreePreference = true;
    public bool IsBookTreeRequested => _bookTreePreference;

    private bool? _scenePanelPreference;
    public bool IsScenePanelRequested
    {
        get => _scenePanelPreference ?? (!IsCompactLayout || IsStudioTheme);
        set
        {
            if (_scenePanelPreference == value) return;
            _scenePanelPreference = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowBookTree));
            OnPropertyChanged(nameof(ShowScenePanel));
        }
    }

    public void UpdateWindowWidth(double width)
    {
        _windowWidth = width;
        OnPropertyChanged(nameof(ShowBookTree));
    }

    [RelayCommand]
    private void ToggleBookTree()
    {
        _bookTreePreference = !ShowBookTree;
        if (_bookTreePreference && _windowWidth < 1120) IsScenePanelRequested = false;
        OnPropertyChanged(nameof(IsBookTreeRequested));
        OnPropertyChanged(nameof(ShowBookTree));
    }

    [RelayCommand]
    private void ToggleScenePanel() => IsScenePanelRequested = !IsScenePanelRequested;


    public bool IsFullCanvasAvailable => IsProjectOpen && CurrentScreen == AppScreen.Editor;

    public string CurrentThemeName => SelectedTheme?.Title ?? Loc.T("Тема");

    public string? StatusMessage { get; private set; }

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.IsFullCanvas))
        {
            OnPropertyChanged(nameof(IsFullCanvas));
            OnPropertyChanged(nameof(ShowWorkspace));
            OnPropertyChanged(nameof(IsBookTreeAvailable));
            OnPropertyChanged(nameof(ShowBookTree));
            OnPropertyChanged(nameof(ShowScenePanel));
            OnPropertyChanged(nameof(ShowStatusBar));
            OnPropertyChanged(nameof(IsFullCanvasAvailable));
            OnPropertyChanged(nameof(ShowClassicHeader));
            OnPropertyChanged(nameof(ShowCabinetChrome));
            OnPropertyChanged(nameof(ShowStudioChrome));
        }
    }

    private void SyncProjectState()
    {
        // Лаунчер подписан на шину событий сам (ProjectOpened/Closed/Changed),
        // поэтому здесь его состояние не дублируется: иначе каждое событие
        // сцены (в том числе автосохранение) перестраивало бы список проектов.
        RaiseShellState();
    }

    private void OnNavigated(object? sender, AppScreen screen)
    {
        CurrentScreen = screen;
        Current = ScreenFor(screen);
        RaiseShellState();
    }

    /// <summary>
    /// Поднимает свойства оболочки: состояние проекта и редактора меняется
    /// из разных событий, поэтому свойства пересчитываются целиком.
    /// </summary>
    private void RaiseShellState()
    {
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectCaption));
        OnPropertyChanged(nameof(CanOpenProjectFolder));
        OnPropertyChanged(nameof(AutosaveText));
        OnPropertyChanged(nameof(IsAutosaveEnabled));
        OpenProjectFolderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsProjectOpen));
        OnPropertyChanged(nameof(ShowWorkspace));
        OnPropertyChanged(nameof(ShowClassicHeader));
        OnPropertyChanged(nameof(ShowCabinetChrome));
        OnPropertyChanged(nameof(ShowStudioChrome));
        OnPropertyChanged(nameof(IsBookTreeAvailable));
        OnPropertyChanged(nameof(ShowBookTree));
        OnPropertyChanged(nameof(ShowStatusBar));
        OnPropertyChanged(nameof(IsFullCanvasAvailable));
        OnPropertyChanged(nameof(BreadcrumbsText));
        OnPropertyChanged(nameof(StatusStatsText));
        OnPropertyChanged(nameof(IsArchitectureActive));
        OnPropertyChanged(nameof(IsLauncherActive));
        OnPropertyChanged(nameof(IsMapActive));
        OnPropertyChanged(nameof(IsCharactersActive));
        OnPropertyChanged(nameof(IsLinesActive));
        OnPropertyChanged(nameof(IsEditorActive));
        OnPropertyChanged(nameof(IsSettingsActive));
        OnPropertyChanged(nameof(ShowScenePanel));
        OnPropertyChanged(nameof(IsExportActive));
    }

    private object ScreenFor(AppScreen screen) => screen switch
    {
        AppScreen.Architecture => ShowArchitecture(ArchitectureTab.Map),
        AppScreen.Editor => _editor,
        AppScreen.Settings => _settings,
        AppScreen.Characters => ShowArchitecture(ArchitectureTab.Characters),
        AppScreen.Timeline => ShowArchitecture(ArchitectureTab.Lines),
        AppScreen.Export => _export,
        _ => _launcher,
    };

    /// <summary>Показывает Архитектор на нужной вкладке и возвращает хост-экран.</summary>
    private object ShowArchitecture(ArchitectureTab tab)
    {
        _architectureHost.Show(tab);
        return _architectureHost;
    }

    /// <summary>Строка автосохранения для статус-бара (ТЗ п. 11).</summary>
    public bool IsAutosaveEnabled => _projects.Current?.Settings.AutoSaveEnabled == true;

    public string AutosaveText => IsAutosaveEnabled
        ? Loc.T("Автосохранение: включено")
        : Loc.T("Автосохранение: выключено");

    /// <summary>Хлебные крошки: книга › глава › сцена либо имя проекта.</summary>
    public string BreadcrumbsText
    {
        get
        {
            var sceneId = _editor.Engine.SceneId;
            var project = _projects.Current;
            if (sceneId is null || project is null)
            {
                return project?.Name ?? Loc.T("Проект не открыт");
            }

            var found = (from series in project.Series
                         from book in series.Books
                         from chapter in book.Chapters
                         from scene in chapter.Scenes
                         where scene.Id == sceneId
                         select new { Book = book.Title, Chapter = chapter.Title, Scene = scene.Title }).FirstOrDefault();
            return found is null
                ? project.Name
                : $"{found.Book} › {found.Chapter} › {found.Scene}";
        }
    }

    /// <summary>Статистика для статус-бара: текст открытой сцены либо объём проекта.</summary>
    public string StatusStatsText
    {
        get
        {
            if (_editor.IsSceneOpen)
            {
                var statistics = _editor.Engine.Statistics;
                return $"{FormatNumber(statistics.Words)} {Plural(statistics.Words, "слово", "слова", "слов")} · " +
                       $"{FormatNumber(statistics.Characters)} {Plural(statistics.Characters, "символ", "символа", "символов")}";
            }

            var books = _projects.Current?.Series.SelectMany(series => series.Books) ?? [];
            var words = books.Sum(book => book.Words);
            var chapters = books.Sum(book => book.Chapters.Count);
            var scenes = books.Sum(book => book.Chapters.Sum(chapter => chapter.Scenes.Count));
            if (!books.Any())
            {
                return Loc.T("Проект пуст");
            }

            return $"{FormatNumber(words)} {Plural(words, "слово", "слова", "слов")} · " +
                   $"{chapters} {Plural(chapters, "глава", "главы", "глав")} · " +
                   $"{scenes} {Plural(scenes, "сцена", "сцены", "сцен")}";
        }
    }

    public bool IsArchitectureActive => CurrentScreen is AppScreen.Architecture or AppScreen.Characters or AppScreen.Timeline;

    public bool IsLauncherActive => CurrentScreen == AppScreen.Launcher;

    public bool IsMapActive => CurrentScreen == AppScreen.Architecture;

    public bool IsCharactersActive => CurrentScreen == AppScreen.Characters;

    public bool IsLinesActive => CurrentScreen == AppScreen.Timeline;

    public bool IsEditorActive => CurrentScreen == AppScreen.Editor;

    public bool IsSettingsActive => CurrentScreen == AppScreen.Settings;

    public bool IsExportActive => CurrentScreen == AppScreen.Export;

    /// <summary>Подпись открытого проекта в шапке (макет: строка справа со стрелкой).</summary>
    public string ProjectCaption => _projects.Current?.Name ?? Loc.T("Проект не открыт");

    public bool CanOpenProjectFolder => !string.IsNullOrEmpty(_projects.CurrentFilePath);

    /// <summary>Показывает файл проекта в системном файловом менеджере.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenProjectFolder))]
    private void OpenProjectFolder()
    {
        if (_projects.CurrentFilePath is not { Length: > 0 } path)
        {
            return;
        }

        DesktopFileManager.RevealFile(path);
    }

    [RelayCommand]
    private void OpenLauncher() => _navigation.NavigateTo(AppScreen.Launcher);

    [RelayCommand]
    private void OpenEditor() => _navigation.NavigateTo(AppScreen.Editor);

    [RelayCommand]
    private void OpenMap() => _navigation.NavigateTo(AppScreen.Architecture);

    [RelayCommand]
    private void OpenCharacters() => _navigation.NavigateTo(AppScreen.Characters);

    [RelayCommand]
    private void OpenLines() => _navigation.NavigateTo(AppScreen.Timeline);

    [RelayCommand]
    private void OpenExport() => _navigation.NavigateTo(AppScreen.Export);

    [RelayCommand]
    private void OpenSettings() => _navigation.NavigateTo(AppScreen.Settings);

    [RelayCommand]
    private void OpenHelp() => _help.Show();

    [RelayCommand]
    private void OpenAbout() => _help.Show("about");

    [RelayCommand]
    private void OpenLicenses() => _help.Show("licenses");

    [RelayCommand]
    private void ToggleThemeMenu() => IsThemeMenuOpen = !IsThemeMenuOpen;

    [RelayCommand]
    private async Task SelectThemeAsync(ThemeOption theme, CancellationToken cancellationToken)
    {
        IsThemeMenuOpen = false;
        _settings.Load();
        _settings.SelectedTheme = _settings.Themes.Single(option => option.Value == theme.Value);
        if (_settings.PendingApply is { } pending) await pending;
    }

    private void LoadSavedTheme()
    {
        _theme.Apply(_appSettings.Current.Theme is AppTheme.Studio or AppTheme.GreenStart or AppTheme.Cabinet or AppTheme.Jam
            ? _appSettings.Current.Theme : AppTheme.Starter);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        var wasCompact = IsCompactLayout;
        SelectedTheme = Themes.FirstOrDefault(option => option.Value == _theme.Current) ?? Themes[0];
        _editor.IsCabinetLayout = IsCompactLayout;
        _editor.IsJamLayout = IsJamTheme;
        _editor.IsStudioLayout = IsStudioTheme;
        if (wasCompact != IsCompactLayout)
        {
            BookTreeWidth = IsCompactLayout ? 232 : 280;
            SceneContextWidth = IsCompactLayout ? 300 : 340;
        }
        OnPropertyChanged(nameof(IsCabinetTheme));
        OnPropertyChanged(nameof(IsJamTheme));
        OnPropertyChanged(nameof(IsStudioTheme));
        OnPropertyChanged(nameof(IsCompactLayout));
        OnPropertyChanged(nameof(UseDropdownNavigation));
        OnPropertyChanged(nameof(IsScenePanelRequested));
        RaiseShellState();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LocalizationManager.Language)) return;
        var value = SelectedTheme?.Value ?? _theme.Current;
        Themes = CreateThemes();
        OnPropertyChanged(nameof(Themes));
        SelectedTheme = Themes.FirstOrDefault(option => option.Value == value) ?? Themes[0];
        RaiseShellState();
    }

    /// <summary>Есть ли правки, которые не дошли до проекта и могут потеряться при выходе.</summary>
    public bool HasPendingEdits =>
        _editor.IsDirty || SceneContext.HasPendingChanges || _architectureHost.HasPendingChanges;

    /// <summary>
    /// Фиксирует текст сцены, контекст сцены и черновики вкладок архитектора,
    /// затем сохраняет проект. Вызывается при закрытии приложения.
    /// </summary>
    public async Task FlushPendingEditsAsync(CancellationToken cancellationToken = default)
    {
        await _editor.FlushAsync(cancellationToken);
        await SceneContext.FlushAsync(cancellationToken);
        await _architectureHost.CommitPendingAsync(cancellationToken);

        // Контекст сцены и карточки правят снимок проекта уже после сохранения
        // редактора, поэтому финальную запись на диск делаем последней.
        if (_projects.IsDirty)
        {
            var saved = await _projects.SaveAsync(cancellationToken);
            if (!saved.IsSuccess)
            {
                return;
            }
        }

        await _launcher.RefreshRecentProjectSummaryAsync(cancellationToken);
    }

    /// <summary>Сохранение из верхней строки или по Ctrl+S: сцена сохраняется вместе с проектом.</summary>
    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_editor.IsSceneOpen)
        {
            // Команда сохранения редактора сама доносит отмену изменения в проект.
            await _editor.SaveCommand.ExecuteAsync(null);
        }
        else
        {
            var result = await _projects.SaveAsync(cancellationToken);
            // Ошибка отражается событием SaveFailed, строку состояния обновляет оболочка.
            StatusMessage = result.IsSuccess ? Loc.T("Проект сохранён.") : result.Error?.UserMessage;
            RaiseShellState();
            if (!result.IsSuccess)
            {
                return;
            }
        }

        if (!_projects.IsDirty)
        {
            await _launcher.RefreshRecentProjectSummaryAsync(cancellationToken);
        }
    }

    /// <summary>Таймер автосохранения работает, пока проект открыт и настройка включена.</summary>
    private void StartProjectAutosave()
    {
        if (_projects.Current is { Settings.AutoSaveEnabled: true })
        {
            if (!_autosaveTimer.IsEnabled)
            {
                _autosaveTimer.Start();
            }
        }
        else
        {
            _autosaveTimer.Stop();
        }
    }

    /// <summary>
    /// Автосохранение проекта после паузы в правках: интервал берётся из настроек,
    /// снимок пишется той же службой проекта, что и сохранение вручную.
    /// </summary>
    private void OnAutosaveTick()
    {
        if (_projects is not { Current: { } project, IsDirty: true } ||
            project.Settings is not { AutoSaveEnabled: true } ||
            _editor.IsDirty)
        {
            // Пока в редакторе есть несохранённый текст, проект пишет автосохранение
            // редактора: оно фиксирует текст и сохраняет проект целиком.
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, project.Settings.AutoSaveIntervalSeconds));
        if (_clock.UtcNow - _lastProjectActivity < interval)
        {
            return;
        }

        _ = AutosaveProjectAsync();
    }

    private async Task AutosaveProjectAsync()
    {
        if (_isProjectSaving)
        {
            return;
        }

        _isProjectSaving = true;
        try
        {
            await _projects.SaveAsync(CancellationToken.None);
        }
        finally
        {
            _isProjectSaving = false;
        }
    }

    private static string FormatNumber(int number)
        => number.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    private static string Plural(int number, string one, string few, string many)
        => Loc.Plural(number, one, few, many);

    public void Dispose()
    {
        _autosaveTimer.Stop();
        _navigation.Navigated -= OnNavigated;
        _editor.PropertyChanged -= OnEditorPropertyChanged;
        _theme.Changed -= OnThemeChanged;
        LocalizationManager.Instance.PropertyChanged -= OnLanguageChanged;
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }
}
