namespace ScriSelfStudio.App.Shell;

using System.Collections.ObjectModel;
using System.Diagnostics;
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
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation;
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
    private readonly List<IDisposable> _subscriptions = [];
    private readonly DispatcherTimer _autosaveTimer;

    private DateTimeOffset _lastProjectActivity;
    private bool _isProjectSaving;

    /// <summary>Темы оформления из макета: Studio, Paper, Graphite, Ocean, Forest.</summary>
    public IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new(AppTheme.Studio, "Studio", "тёмная", ThemeOption.CreateSwatch("#142127")),
        new(AppTheme.Paper, "Paper", "основная", ThemeOption.CreateSwatch("#F4EEE5")),
        new(AppTheme.Graphite, "Graphite", "тёмная", ThemeOption.CreateSwatch("#202324")),
        new(AppTheme.Ocean, "Ocean", "холодная", ThemeOption.CreateSwatch("#10262E")),
        new(AppTheme.Forest, "Forest", "глубокая", ThemeOption.CreateSwatch("#172520")),
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
        ISystemClock clock)
    {
        _projects = projects;
        _launcher = launcher;
        _editor = editor;
        _settings = settings;
        _export = export;
        _navigation = navigation;
        _appSettings = appSettings;
        _theme = theme;
        _clock = clock;
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
            OnPropertyChanged(nameof(StatusStatsText));
            SaveCommand.NotifyCanExecuteChanged();
        }));
        _subscriptions.Add(events.Subscribe<SaveCompleted>(_ =>
        {
            LastSavedAt = _clock.UtcNow;
            StatusMessage = "Проект сохранён.";
            SaveCommand.NotifyCanExecuteChanged();
        }));
        _subscriptions.Add(events.Subscribe<SaveFailed>(eventArgs =>
        {
            StatusMessage = $"Ошибка сохранения: {eventArgs.Error.UserMessage}";
        }));

        SyncProjectState();
        LoadSavedTheme();
    }

        [ObservableProperty]
    private double _bookTreeWidth = 280;

    [ObservableProperty]
    private double _sceneContextWidth = 340;

    public void SetBookTreeWidth(double width) => BookTreeWidth = Math.Clamp(width, 180, 400);

    public void SetSceneContextWidth(double width) => SceneContextWidth = Math.Clamp(width, 180, 400);

    public BookTreeViewModel BookTree { get; }

    public SceneContextViewModel SceneContext { get; }

    public bool IsProjectOpen => _projects.Current is not null;

    public string ProjectName => _projects.Current?.Name ?? "Проект не открыт";

        public bool IsFullCanvas
    {
        get => _editor.IsFullCanvas;
        set => _editor.IsFullCanvas = value;
    }

    public bool ShowWorkspace => IsProjectOpen && !IsFullCanvas;

        public const double MinPaneWidth = 180;
    public const double MaxPaneWidth = 400;

    public bool ShowScenePanel => ShowWorkspace && CurrentScreen is not (AppScreen.Timeline or AppScreen.Architecture or AppScreen.Characters) && !IsFullCanvas;

    public bool ShowBookTree => ShowWorkspace && CurrentScreen is not (AppScreen.Timeline or AppScreen.Architecture or AppScreen.Characters) && !IsFullCanvas;

    public bool ShowStatusBar => ShowWorkspace && CurrentScreen != AppScreen.Timeline;

    public bool IsFullCanvasAvailable => ShowWorkspace && CurrentScreen != AppScreen.Timeline;

    public string CurrentThemeName => SelectedTheme?.Title ?? "Тема";

    public string? StatusMessage { get; private set; }

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.IsFullCanvas))
        {
            OnPropertyChanged(nameof(IsFullCanvas));
            OnPropertyChanged(nameof(ShowWorkspace));
            OnPropertyChanged(nameof(ShowBookTree));
            OnPropertyChanged(nameof(ShowScenePanel));
            OnPropertyChanged(nameof(ShowStatusBar));
            OnPropertyChanged(nameof(IsFullCanvasAvailable));
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
        OnPropertyChanged(nameof(IsProjectOpen));
        OnPropertyChanged(nameof(ShowWorkspace));
        OnPropertyChanged(nameof(ShowBookTree));
        OnPropertyChanged(nameof(ShowStatusBar));
        OnPropertyChanged(nameof(IsFullCanvasAvailable));
        OnPropertyChanged(nameof(BreadcrumbsText));
        OnPropertyChanged(nameof(StatusStatsText));
        OnPropertyChanged(nameof(IsArchitectureActive));
        OnPropertyChanged(nameof(IsLauncherActive));
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
    public string AutosaveText => _projects.Current?.Settings.AutoSaveEnabled == true
        ? "Автосохранение: включено"
        : "Автосохранение: выключено";

    /// <summary>Хлебные крошки: книга › глава › сцена либо имя проекта.</summary>
    public string BreadcrumbsText
    {
        get
        {
            var sceneId = _editor.Engine.SceneId;
            var project = _projects.Current;
            if (sceneId is null || project is null)
            {
                return project?.Name ?? "Проект не открыт";
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
                return "Проект пуст";
            }

            return $"{FormatNumber(words)} {Plural(words, "слово", "слова", "слов")} · " +
                   $"{chapters} {Plural(chapters, "глава", "главы", "глав")} · " +
                   $"{scenes} {Plural(scenes, "сцена", "сцены", "сцен")}";
        }
    }

    public bool IsArchitectureActive => CurrentScreen is AppScreen.Architecture or AppScreen.Characters or AppScreen.Timeline;

    public bool IsLauncherActive => CurrentScreen == AppScreen.Launcher;

    public bool IsEditorActive => CurrentScreen == AppScreen.Editor;

    public bool IsSettingsActive => CurrentScreen == AppScreen.Settings;

    public bool IsExportActive => CurrentScreen == AppScreen.Export;

    /// <summary>Подпись открытого проекта в шапке (макет: строка справа со стрелкой).</summary>
    public string ProjectCaption => _projects.Current?.Name ?? "Проект не открыт";

    public bool CanOpenProjectFolder => !string.IsNullOrEmpty(_projects.CurrentFilePath);

    /// <summary>Показывает файл проекта в проводнике (макет: кнопка с папкой).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenProjectFolder))]
    private void OpenProjectFolder()
    {
        if (_projects.CurrentFilePath is not { Length: > 0 } path)
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private void OpenLauncher() => _navigation.NavigateTo(AppScreen.Launcher);

    [RelayCommand]
    private void OpenEditor() => _navigation.NavigateTo(AppScreen.Editor);

    [RelayCommand]
    private void OpenMap() => _navigation.NavigateTo(AppScreen.Architecture);

    [RelayCommand]
    private void OpenExport() => _navigation.NavigateTo(AppScreen.Export);

    [RelayCommand]
    private void OpenSettings() => _navigation.NavigateTo(AppScreen.Settings);

    [RelayCommand]
    private void ToggleThemeMenu() => IsThemeMenuOpen = !IsThemeMenuOpen;

    [RelayCommand]
    private async Task SelectThemeAsync(ThemeOption theme, CancellationToken cancellationToken)
    {
        IsThemeMenuOpen = false;
        SelectedTheme = theme;
        _theme.Apply(theme.Value);
        await _appSettings.UpdateAsync(
            preferences => preferences.Theme = theme.Value,
            cancellationToken);
    }

    private void LoadSavedTheme()
    {
        var savedTheme = _appSettings.Current.Theme;
        SelectedTheme = Themes.FirstOrDefault(t => t.Value == savedTheme) ?? Themes[0];
        _theme.Apply(SelectedTheme.Value);
    }

    /// <summary>Сохранение из верхней строки или по Ctrl+S: сцена сохраняется вместе с проектом.</summary>
    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_editor.IsSceneOpen)
        {
            // Команда сохранения редактора сама доносит отмену изменения в проект.
            await _editor.SaveCommand.ExecuteAsync(null);
            return;
        }

        var result = await _projects.SaveAsync(cancellationToken);
        // Ошибка отражается событием SaveFailed, строку состояния обновляет оболочка.
        StatusMessage = result.IsSuccess ? "Проект сохранён." : result.Error?.UserMessage;
        RaiseShellState();
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
        => number.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));

    private static string Plural(int number, string one, string few, string many)
    {
        var mod10 = number % 10;
        var mod100 = number % 100;
        if (mod100 is >= 11 and <= 14)
        {
            return many;
        }

        return mod10 switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
    }

    public void Dispose()
    {
        _autosaveTimer.Stop();
        _navigation.Navigated -= OnNavigated;
        _editor.PropertyChanged -= OnEditorPropertyChanged;
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }
}
