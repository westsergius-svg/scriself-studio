namespace ScriSelfStudio.Launcher;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Events;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Theme;

/// <summary>
/// Стартовый экран: создание, открытие, последние проекты и состояние текущего
/// проекта (ТЗ п. 27, 28, 31).
/// </summary>
public sealed partial class LauncherViewModel : ViewModelBase, IDisposable
{
    private readonly IProjectService _projects;
    private readonly IAppSettingsService _settings;
    private readonly IFileDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IEditorEngine _editor;
    private readonly List<IDisposable> _subscriptions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectOpen))]
    [NotifyPropertyChangedFor(nameof(CurrentProjectTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentProjectPath))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private ProjectDto? _currentProject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _isDirty;

    public LauncherViewModel(
        IProjectService projects,
        IAppSettingsService settings,
        IFileDialogService dialogs,
        IThemeService theme,
        INavigationService navigation,
        IEventBus events,
        IEditorEngine editor)
    {
        _projects = projects;
        _settings = settings;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;
        _editor = editor;

        // Экран сам следит за состоянием проекта: карточка открытого проекта и
        // панель «Контекст проекта» должны обновляться, даже если лаунчер
        // инициализирован без оболочки (например, в тестах или при открытии
        // проекта из командной строки).
        _subscriptions.Add(events.Subscribe<ProjectOpened>(_ => SyncProjectState()));
        _subscriptions.Add(events.Subscribe<ProjectClosed>(_ => SyncProjectState()));
        _subscriptions.Add(events.Subscribe<ProjectChanged>(_ => SyncProjectState()));
    }

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    /// <summary>Отфильтрованный список центральной панели: подстрока поиска.</summary>
    public ObservableCollection<RecentProjectItem> VisibleProjects { get; } = [];

    /// <summary>Выбранная карточка: на неё смотрит правая панель «Контекст проекта».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProject))]
    private RecentProjectItem? _selectedProject;

    /// <summary>Строка поиска по названию и пути проекта.</summary>
    [ObservableProperty]
    private string? _projectSearchQuery;

    /// <summary>Сколько проектов в списке (значок раздела «Мои проекты»).</summary>
    public int ProjectCount => RecentProjects.Count;

    public bool HasProjects => RecentProjects.Count > 0;

    public bool HasSelectedProject => SelectedProject is not null;

    public bool HasVisibleProjects => VisibleProjects.Count > 0;

    partial void OnProjectSearchQueryChanged(string? value) => RefreshVisibleProjects();

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool IsProjectOpen => CurrentProject is not null;

    public bool HasUnsavedChanges => IsProjectOpen && IsDirty;

    public string CurrentProjectTitle => CurrentProject?.Name ?? "Проект не открыт";

    public string CurrentProjectPath => CurrentProject?.FilePath ?? string.Empty;

    /// <summary>
    /// Пояснение для проекта в старом формате: файл остаётся в нём, а предыдущая
    /// версия сохраняется в резервную копию (ТЗ п. 5, 13).
    /// </summary>
    public string? FormatNotice => _projects.CurrentSourceFormat is null
        ? null
        : "Проект открыт в старом формате (ZIP: manifest.json + content.json). "
          + "Сохранение запишет файл в этом же формате, предыдущая версия останется в .bak.";

    public bool HasFormatNotice => FormatNotice is not null;

    public bool HasRecentProjects => RecentProjects.Count > 0;

    /// <summary>Структура открывается только при открытом проекте: кнопка и команда совпадают.</summary>
    public bool CanOpenArchitectureScreen => IsProjectOpen && _navigation.CanNavigateTo(AppScreen.Architecture);

    public bool CanOpenEditorScreen => _navigation.CanNavigateTo(AppScreen.Editor);

    public bool CanOpenSettingsScreen => _navigation.CanNavigateTo(AppScreen.Settings);

    /// <summary>Персонажи и линии относятся к проекту: без него экран пуст (ТЗ п. 27).</summary>
    public bool CanOpenCharactersScreen => IsProjectOpen && _navigation.CanNavigateTo(AppScreen.Characters);

    public bool CanOpenTimelineScreen => IsProjectOpen && _navigation.CanNavigateTo(AppScreen.Timeline);

    [RelayCommand(CanExecute = nameof(CanOpenArchitectureScreen))]
    private void OpenArchitecture() => _navigation.NavigateTo(AppScreen.Architecture);

    /// <summary>Переход на экран персонажей (ТЗ п. 27).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenCharactersScreen))]
    private void OpenCharacters() => _navigation.NavigateTo(AppScreen.Characters);

    /// <summary>Переход на экран сюжетных линий (ТЗ п. 27).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenTimelineScreen))]
    private void OpenTimeline() => _navigation.NavigateTo(AppScreen.Timeline);

    /// <summary>Переход на экран настроек (ТЗ п. 27).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenSettingsScreen))]
    private void OpenSettings() => _navigation.NavigateTo(AppScreen.Settings);

    /// <summary>Раздел «Мои проекты»: обновляет список из настроек.</summary>
    [RelayCommand]
    private void ShowProjects() => RefreshRecentProjects();

    /// <summary>Выбор карточки проекта (щелчок по карточке в списке).</summary>
    [RelayCommand]
    private void SelectProject(RecentProjectItem? item) => SelectedProject = item;

    /// <summary>«Продолжить работу»: открыть выбранный проект.</summary>
    [RelayCommand]
    private async Task OpenSelectedProjectAsync()
    {
        if (SelectedProject is { } item)
        {
            await OpenRecentAsync(item);
        }
    }

    /// <summary>Показать файл выбранного проекта в проводнике.</summary>
    [RelayCommand]
    private void OpenSelectedFolder()
    {
        if (SelectedProject is { } item)
        {
            RevealInExplorer(item);
        }
    }

    /// <summary>Показать файл проекта в проводнике (меню карточки).</summary>
    [RelayCommand]
    private void OpenFolder(RecentProjectItem? item)
    {
        if (item is not null)
        {
            RevealInExplorer(item);
        }
    }

    private static void RevealInExplorer(RecentProjectItem item)
    {
        var directory = Path.GetDirectoryName(item.FilePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{item.FilePath}\"",
            UseShellExecute = true,
        });
    }

    /// <summary>Убрать выбранный проект из списка последних (файл на диске не трогаем).</summary>
    [RelayCommand]
    private async Task RemoveSelectedProjectAsync()
    {
        if (SelectedProject is { } item)
        {
            await RemoveRecentAsync(item);
        }
    }

    /// <summary>
    /// Разделы «Шаблоны», «Облако» и «Блог» из макета пока не реализованы:
    /// кнопка честно сообщает об этом, а не молчит.
    /// </summary>
    [RelayCommand]
    private void ShowComingSoon(string? title) =>
        StatusMessage = $"Раздел «{title}» появится в следующих версиях.";

    /// <summary>Загрузка настроек и списка последних проектов при старте приложения.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _settings.LoadAsync(cancellationToken);
        if (!loaded.IsSuccess)
        {
            ErrorMessage = loaded.Error!.UserMessage;
        }

        // Тема из настроек применяется здесь же: любой вход в приложение
        // (в том числе тестовый) получает оформление, выбранное пользователем.
        _theme.Apply(_settings.Current.Theme);

        RefreshRecentProjects();
        CurrentProject = _projects.Current;
        IsDirty = _projects.IsDirty;
        if (CurrentProject is { } open)
        {
            SelectedProject = RecentProjects.FirstOrDefault(
                item => string.Equals(item.FilePath, open.FilePath, StringComparison.OrdinalIgnoreCase))
                ?? SelectedProject;
        }

        OpenArchitectureCommand.NotifyCanExecuteChanged();
        OpenCharactersCommand.NotifyCanExecuteChanged();
        OpenTimelineCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CreateProjectAsync(CancellationToken cancellationToken)
    {
        ClearMessages();
        var path = await _dialogs.PickProjectToCreateAsync(
            "Новый проект.scri",
            SuggestedDirectory(),
            cancellationToken);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // Создание нового проекта при уже открытом не должно упираться в
        // «Сначала закройте текущий проект»: текущий сначала сохраняется.
        if (!await PrepareProjectSwitchAsync(cancellationToken))
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        var created = await _projects.CreateAsync(
            new CreateProjectRequest { FilePath = path, Name = name },
            cancellationToken);

        await HandleLoadedProjectAsync(created, path, cancellationToken);
    }

    [RelayCommand]
    private async Task OpenProjectAsync(CancellationToken cancellationToken)
    {
        ClearMessages();
        var path = await _dialogs.PickProjectToOpenAsync(SuggestedDirectory(), cancellationToken);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await OpenPathAsync(path, cancellationToken);
    }

    [RelayCommand]
    private async Task OpenRecentAsync(RecentProjectItem? item)
    {
        if (item is null)
        {
            return;
        }

        ClearMessages();
        await OpenPathAsync(item.FilePath, CancellationToken.None);
    }

    [RelayCommand]
    private async Task RemoveRecentAsync(RecentProjectItem? item)
    {
        if (item is null)
        {
            return;
        }

        var removed = await _settings.RemoveRecentProjectAsync(item.FilePath);
        if (!removed.IsSuccess)
        {
            await ReportAsync(removed.Error!);
            return;
        }

        RefreshRecentProjects();
        StatusMessage = $"Проект «{item.Name}» убран из списка последних.";
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        ClearMessages();
        var saved = await _projects.SaveAsync();
        if (!saved.IsSuccess)
        {
            await ReportAsync(saved.Error!);
            return;
        }

        CurrentProject = _projects.Current;
        IsDirty = _projects.IsDirty;
        StatusMessage = "Проект сохранён.";
        if (CurrentProject is { } project)
        {
            await RememberProjectAsync(project, project.FilePath, CancellationToken.None);
        }
    }

    [RelayCommand]
    private async Task CloseProjectAsync()
    {
        if (!IsProjectOpen)
        {
            return;
        }

        ClearMessages();

        // Кнопка «Закрыть проект» сохраняет правки, а не выбрасывает их:
        // тот же порядок, что при смене проекта.
        if (!await PrepareProjectSwitchAsync(CancellationToken.None))
        {
            return;
        }

        StatusMessage = "Проект закрыт.";
    }

    /// <summary>
    /// Открыть проект, путь к которому передан аргументом командной строки (ТЗ п. 27, 31).
    /// </summary>
    public async Task OpenProjectFromCommandLineAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        ClearMessages();
        await OpenPathAsync(path, cancellationToken);
    }

    public void RefreshRecentProjects()
    {
        // Выделение переживает обновление списка: ищем ту же карточку по пути.
        var selectedPath = SelectedProject?.FilePath;
        var current = _projects.Current;
        RecentProjects.Clear();
        foreach (var entry in _settings.Current.RecentProjects)
        {
            RecentProjects.Add(new RecentProjectItem(
                MergeWithOpenProject(entry, current),
                OpenRecentCommand,
                RemoveRecentCommand,
                SelectProjectCommand,
                OpenFolderCommand));
        }

        SelectedProject = selectedPath is null
            ? RecentProjects.FirstOrDefault()
            : RecentProjects.FirstOrDefault(
                  item => string.Equals(item.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
              ?? RecentProjects.FirstOrDefault();

        OnPropertyChanged(nameof(HasRecentProjects));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(ProjectCount));
        RefreshVisibleProjects();
    }

    /// <summary>
    /// Для открытого проекта карточка показывает живую сводку, а не кэш: автор
    /// добавляет главы и сцены, и карточка должна меняться сразу, не дожидаясь
    /// повторного открытия проекта.
    /// </summary>
    private static RecentProjectEntry MergeWithOpenProject(RecentProjectEntry entry, ProjectDto? current)
    {
        if (current is null
            || !string.Equals(current.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return entry;
        }

        return entry with
        {
            Name = current.Name,
            Description = current.Description,
            BookCount = current.Series.Sum(series => series.Books.Count),
            ChapterCount = current.Series
                .SelectMany(series => series.Books)
                .Sum(book => book.Chapters.Count),
            SceneCount = current.Series
                .SelectMany(series => series.Books)
                .SelectMany(book => book.Chapters)
                .Sum(chapter => chapter.Scenes.Count),
            CharacterCount = current.Characters.Count,
            PlotLineCount = current.PlotLines.Count,
            UpdatedAt = current.UpdatedAt,
        };
    }

    /// <summary>Применяет строку поиска к списку карточек.</summary>
    private void RefreshVisibleProjects()
    {
        var query = ProjectSearchQuery?.Trim();
        VisibleProjects.Clear();
        foreach (var item in RecentProjects)
        {
            if (string.IsNullOrEmpty(query)
                || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                VisibleProjects.Add(item);
            }
        }

        OnPropertyChanged(nameof(HasVisibleProjects));
    }

    /// <summary>Состояние проекта изменилось вне этого экрана (например, редактором).</summary>
    public void SyncProjectState()
    {
        CurrentProject = _projects.Current;
        IsDirty = _projects.IsDirty;
        OpenArchitectureCommand.NotifyCanExecuteChanged();
        OpenSettingsCommand.NotifyCanExecuteChanged();
        OpenCharactersCommand.NotifyCanExecuteChanged();
        OpenTimelineCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(FormatNotice));
        OnPropertyChanged(nameof(HasFormatNotice));

        // Структура проекта могла измениться: карточка открытого проекта должна
        // показывать свежие счётчики.
        RefreshRecentProjects();
    }

    /// <summary>
    /// Готовит смену проекта: правки открытой сцены коммитятся в проект, проект
    /// сохраняется на диск, и только после этого он закрывается. Так «Открыть
    /// проект» и «Новый проект» не упираются в «Сначала закройте текущий
    /// проект», а работа автора не теряется.
    /// </summary>
    /// <returns><c>false</c>, если сохранить не удалось и смену отменили.</returns>
    private async Task<bool> PrepareProjectSwitchAsync(CancellationToken cancellationToken)
    {
        if (!IsProjectOpen)
        {
            return true;
        }

        // Текст сцены живёт в движке, а не в проекте: без коммита сохранение
        /// проекта записало бы на диск старую ревизию документа.
        if (_editor.SceneId is not null && _editor.IsDirty)
        {
            var committed = await _editor.CommitAsync(cancellationToken);
            if (!committed.IsSuccess)
            {
                await ReportAsync(committed.Error!);
                return false;
            }
        }

        if (_projects.IsDirty)
        {
            var saved = await _projects.SaveAsync(cancellationToken);
            if (!saved.IsSuccess)
            {
                await ReportAsync(saved.Error!);
                return false;
            }
        }

        var closed = await _projects.CloseAsync(discardChanges: false, cancellationToken);
        if (!closed.IsSuccess)
        {
            await ReportAsync(closed.Error!);
            return false;
        }

        CurrentProject = null;
        IsDirty = false;
        return true;
    }

    private async Task OpenPathAsync(string path, CancellationToken cancellationToken)
    {
        ClearMessages();

        // Открытие другого проекта при уже открытом: текущий проект и правки
        // редактора сохраняются, и только потом освобождается состояние.
        if (!await PrepareProjectSwitchAsync(cancellationToken))
        {
            return;
        }

        var recovery = await _projects.DetectRecoveryAsync(path, cancellationToken);
        if (!recovery.IsSuccess)
        {
            await ReportAsync(recovery.Error!);
            return;
        }

        if (recovery.Value is { } info)
        {
            var restore = await _dialogs.ConfirmAsync(
                "Найдено восстановление",
                $"Найден файл восстановления от {info.WrittenAt.ToLocalTime():g}. Открыть восстановленные данные?",
                "Открыть восстановление",
                "Открыть файл проекта");

            if (restore)
            {
                var recovered = await _projects.RecoverAsync(info, cancellationToken);
                await HandleLoadedProjectAsync(recovered, path, cancellationToken);
                return;
            }

            await _projects.DiscardRecoveryAsync(info, cancellationToken);
        }

        var opened = await _projects.OpenAsync(path, cancellationToken);
        await HandleLoadedProjectAsync(opened, path, cancellationToken);
    }

    private async Task HandleLoadedProjectAsync(
        Result<ProjectDto> result,
        string path,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            await ReportAsync(result.Error!);
            return;
        }

        var project = result.Require();
        CurrentProject = project;
        IsDirty = _projects.IsDirty;
        StatusMessage = $"Проект «{project.Name}» открыт. Книг: {project.Series.Count}.";

        await RememberProjectAsync(project, path, cancellationToken);

        // Только что открытый проект сразу показываем в «Контексте проекта».
        SelectedProject = RecentProjects.FirstOrDefault(
            item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase))
            ?? SelectedProject;

        // Кнопка «Открыть» сразу ведёт в рабочее пространство проекта: автор
        // продолжает работу, а не остаётся на лаунчере.
        await OpenProjectWorkspaceAsync(project, cancellationToken);
    }

    /// <summary>
    /// Переводит оболочку в рабочее пространство только что открытого проекта.
    /// Если в проекте есть сцена, открываем её в редакторе; у только что созданного
    /// проекта сцен нет, поэтому показываем карту книги — иначе проект «не
    /// открывается» с точки зрения автора: экран оставался бы лаунчером.
    /// </summary>
    private async Task OpenProjectWorkspaceAsync(ProjectDto project, CancellationToken cancellationToken)
    {
        var scene = project.Series
            .SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Scenes)
            .FirstOrDefault();

        if (scene is null)
        {
            // Пустой проект: открывать в редакторе нечего, ведём на карту книги.
            // Там автор создаёт книгу, главу и первую сцену.
            _navigation.NavigateTo(AppScreen.Architecture);
            return;
        }

        var opened = await _editor.OpenSceneAsync(scene.Id, cancellationToken);
        if (!opened.IsSuccess)
        {
            await ReportAsync(opened.Error!);
            return;
        }

        _navigation.NavigateTo(AppScreen.Editor);
    }

    /// <summary>
    /// Кэширует сводку проекта в списке последних, чтобы лаунчер показывал карточку
    /// без чтения файла проекта (макет «Мои проекты»).
    /// </summary>
    private async Task RememberProjectAsync(ProjectDto project, string path, CancellationToken cancellationToken)
    {
        var books = project.Series.Sum(series => series.Books.Count);
        var chapters = project.Series
            .SelectMany(series => series.Books)
            .Sum(book => book.Chapters.Count);
        var scenes = project.Series
            .SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .Sum(chapter => chapter.Scenes.Count);

        var saved = await _settings.AddRecentProjectAsync(new RecentProjectEntry
        {
            FilePath = path,
            Name = project.Name,
            LastOpenedAt = DateTimeOffset.UtcNow,
            Description = project.Description,
            BookCount = books,
            ChapterCount = chapters,
            SceneCount = scenes,
            CharacterCount = project.Characters.Count,
            PlotLineCount = project.PlotLines.Count,
            UpdatedAt = project.UpdatedAt,
        }, cancellationToken);

        if (!saved.IsSuccess)
        {
            await ReportAsync(saved.Error!);
        }

        RefreshRecentProjects();
    }

    private async Task ReportAsync(AppError error)
    {
        ErrorMessage = error.SuggestedAction is null
            ? error.UserMessage
            : $"{error.UserMessage} {error.SuggestedAction}";

        await _dialogs.ShowErrorAsync("Ошибка", ErrorMessage);
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        StatusMessage = null;
    }

    /// <summary>Отписка от шины событий (ТЗ п. 43).</summary>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private string? SuggestedDirectory()
    {
        var recent = _settings.Current.RecentProjects.FirstOrDefault();
        if (recent is not null)
        {
            var directory = Path.GetDirectoryName(recent.FilePath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
