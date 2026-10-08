namespace ScriSelfStudio.App.Shell;

using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Events;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation;
using System.Windows.Input;

/// <summary>Книга в списке левого дерева: вариант для выпадающего списка.</summary>
public sealed record BookOption(Guid Id, string Title)
{
    /// <summary>ComboBox показывает элементы через <c>ToString</c>.</summary>
    public override string ToString() => Title;
}

/// <summary>Сцена в дереве книги со статус-меткой и командой открытия.</summary>
public sealed partial class SceneNode : ObservableObject
{
    public SceneNode(
        Guid id,
        int order,
        string title,
        int words,
        SceneStatus status,
        IBrush statusBrush)
    {
        Id = id;
        Order = order;
        Title = title;
        Words = words;
        Status = status;
        StatusBrush = statusBrush;
    }

    public Guid Id { get; }

    public int Order { get; }

    public string Title { get; }

    public int Words { get; }

    public SceneStatus Status { get; }

    public IBrush StatusBrush { get; }

    /// <summary>Команда открытия сцены, привязанная к этой ножке деревом.</summary>
    public ICommand? OpenCommand { get; set; }

    /// <summary>Номер сцены в главе: «01», «02», …</summary>
    public string Number => Order.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>
    /// Кисть выделения строки сцены в дереве: при открытой сцене — акцентная,
    /// иначе null (базовый стиль кнопки остаётся прозрачным). Триггеры в XAML
    /// этой версии Avalonia отсутствуют, поэтому состояние кладётся в данные.
    /// </summary>
    [ObservableProperty]
    private IBrush? _rowBackground;
}

/// <summary>Глава в дереве книги со сценами и командой добавления сцены.</summary>
public sealed partial class ChapterNode : ObservableObject
{
    public ChapterNode(
        Guid id,
        int order,
        string title,
        IReadOnlyList<SceneNode> scenes)
    {
        Id = id;
        Order = order;
        Title = title;
        Scenes = scenes;
    }

    public Guid Id { get; }

    public int Order { get; }

    public string Title { get; }

    public IReadOnlyList<SceneNode> Scenes { get; }

    /// <summary>Команда добавления сцены в эту главу, привязанная деревом.</summary>
    public ICommand? AddSceneCommand { get; set; }

    /// <summary>Номер главы: «1», «2», …</summary>
    public string Number => Order.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollapsed))]
    private bool _isExpanded;

    /// <summary>Обратное состояние: шеврон показывает свёрнутую главу другим значком.</summary>
    public bool IsCollapsed => !IsExpanded;

    /// <summary>Раскрыть или свернуть главу (клик по шеврону).</summary>
    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}

/// <summary>
/// Левое рабочее пространство (ТЗ п. 24, 27): список книг, дерево глав и сцен
/// со статус-метками. View содержит только разметку; логика — здесь.
/// Открывает сцену в редакторе и управляет созданием глав и сцен.
/// </summary>
public sealed partial class BookTreeViewModel : ViewModelBase, IDisposable
{
    private static readonly IReadOnlyDictionary<SceneStatus, IBrush> StatusBrushes =
        new Dictionary<SceneStatus, IBrush>
        {
            [SceneStatus.Idea] = new SolidColorBrush(Color.Parse("#9AA0A6")),
            [SceneStatus.Draft] = new SolidColorBrush(Color.Parse("#E8963E")),
            [SceneStatus.Revised] = new SolidColorBrush(Color.Parse("#4A7DFF")),
            [SceneStatus.Final] = new SolidColorBrush(Color.Parse("#3FBF7F")),
        };

    /// <summary>Выделение строки открытой сцены в дереве (прозрачный акцент).</summary>
    private static readonly IBrush RowActiveBrush = new SolidColorBrush(Color.Parse("#33D4AF37"));

    private readonly IProjectService _projects;
    private readonly IBookArchitectureService _architecture;
    private readonly IEditorEngine _editorEngine;
    private readonly INavigationService _navigation;
    private readonly IFileDialogService _dialogs;
    private readonly List<IDisposable> _subscriptions = [];

    [ObservableProperty]
    private BookOption? _selectedBook;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>Книги проекта для выпадающего списка.</summary>
    public ObservableCollection<BookOption> Books { get; } = [];

    /// <summary>Главы выбранной книги со сценами.</summary>
    public ObservableCollection<ChapterNode> Chapters { get; } = [];

    public bool HasBooks => Books.Count > 0;

    public bool HasChapters => Chapters.Count > 0;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public BookTreeViewModel(
        IProjectService projects,
        IBookArchitectureService architecture,
        IEditorEngine editorEngine,
        INavigationService navigation,
        IFileDialogService dialogs,
        IEventBus events)
    {
        _projects = projects;
        _architecture = architecture;
        _editorEngine = editorEngine;
        _navigation = navigation;
        _dialogs = dialogs;

        _subscriptions.Add(events.Subscribe<BookChanged>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<ChapterChanged>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<SceneChanged>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<EditorDocumentChanged>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<ProjectOpened>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<ProjectClosed>(_ => Refresh()));

        // При входе в редактор дерево показывает главу с текущей сценой и
        // сворачивает остальные — автор сразу видит контекст правки.
        _navigation.Navigated += OnNavigated;

        Refresh();
    }

    private void OnNavigated(object? sender, AppScreen screen)
    {
        if (screen != AppScreen.Editor)
        {
            return;
        }

        Refresh();
        ExpandChapterForCurrentScene();
    }

    /// <summary>Раскрывает только главу с текущей сценой, остальные сворачивает.</summary>
    private void ExpandChapterForCurrentScene()
    {
        var current = FindChapterForScene(_editorEngine.SceneId);
        foreach (var chapter in Chapters)
        {
            chapter.IsExpanded = current is not null && chapter.Id == current.Value;
        }
    }

    /// <summary>Глава, которой принадлежит сцена; null, если сцена не открыта.</summary>
    private Guid? FindChapterForScene(Guid? sceneId)
    {
        if (sceneId is not { } id)
        {
            return null;
        }

        return _projects.Current?.Series
            .SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .FirstOrDefault(chapter => chapter.Scenes.Any(scene => scene.Id == id))
            ?.Id;
    }

    [RelayCommand]
    private async Task AddChapterAsync(CancellationToken cancellationToken)
    {
        if (SelectedBook is not { } book)
        {
            return;
        }

        var title = await _dialogs.PromptAsync("Новая глава", "Название главы", "Глава", cancellationToken);
        if (title is null)
        {
            return;
        }

        await ApplyAsync(_architecture.CreateChapterAsync(new CreateChapterRequest
        {
            BookId = book.Id,
            Title = title,
        }), cancellationToken);
    }

    [RelayCommand]
    private async Task AddSceneAsync(Guid chapterId, CancellationToken cancellationToken)
    {
        var title = await _dialogs.PromptAsync("Новая сцена", "Название сцены", "Сцена", cancellationToken);
        if (title is null)
        {
            return;
        }

        await ApplyAsync(_architecture.CreateSceneAsync(new CreateSceneRequest
        {
            ChapterId = chapterId,
            Title = title,
        }), cancellationToken);
    }

    /// <summary>Открывает сцену в редакторе (ТЗ п. 10).</summary>
    private async Task OpenSceneAsync(SceneNode scene, CancellationToken cancellationToken)
    {
        var opened = await _editorEngine.OpenSceneAsync(scene.Id, cancellationToken);
        if (!opened.IsSuccess)
        {
            ErrorMessage = opened.Error is null
                ? "Не удалось открыть сцену."
                : opened.Error.UserMessage;
            return;
        }

        _navigation.NavigateTo(AppScreen.Editor);
    }

    public void Dispose()
    {
        _navigation.Navigated -= OnNavigated;

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    /// <summary>Перечитывает структуру проекта, сохраняя выбор книги и раскрытые главы.</summary>
    public void Refresh()
    {
        var previousBook = SelectedBook?.Id;
        var previousScene = _editorEngine.SceneId;
        var expanded = Chapters
            .Where(chapter => chapter.IsExpanded)
            .Select(chapter => chapter.Id)
            .ToHashSet();

        Books.Clear();
        foreach (var book in _projects.Current?.Series
            .SelectMany(series => series.Books) ?? [])
        {
            Books.Add(new BookOption(book.Id, book.Title));
        }

        SelectedBook = Books.FirstOrDefault(candidate => candidate.Id == previousBook)
            ?? Books.FirstOrDefault();

        RefreshChapters(expanded, previousScene);
        ErrorMessage = null;
    }

    partial void OnSelectedBookChanged(BookOption? value)
        => RefreshChapters(expanded: null, previousScene: _editorEngine.SceneId);

    private void RefreshChapters(HashSet<Guid>? expanded, Guid? previousScene)
    {
        Chapters.Clear();
        if (SelectedBook is not { } selectedBook)
        {
            AddChapterCommand.NotifyCanExecuteChanged();
            return;
        }

        var book = _projects.Current?.Series
            .SelectMany(series => series.Books)
            .FirstOrDefault(candidate => candidate.Id == selectedBook.Id);
        var currentChapter = FindChapterForScene(previousScene);
        foreach (var chapter in book?.Chapters ?? [])
        {
            var scenes = chapter.Scenes
                .Select(CreateSceneNode)
                .ToList();
            foreach (var scene in scenes)
            {
                scene.OpenCommand = new AsyncRelayCommand(
                    () => OpenSceneAsync(scene, CancellationToken.None));
                scene.IsCurrent = scene.Id == previousScene;
            }

            var chapterNode = new ChapterNode(chapter.Id, chapter.Order, chapter.Title, scenes)
            {
                // Явно заданное состояние (ручное раскрытие) важнее: иначе при
                // перечитывании структуры дерево схлопывалось бы. По умолчанию
                // раскрыта только глава с открытой сценой.
                IsExpanded = expanded?.Contains(chapter.Id) ?? chapter.Id == currentChapter,
            };
            foreach (var scene in scenes)
            {
                scene.RowBackground = scene.IsCurrent ? RowActiveBrush : null;
            }

            chapterNode.AddSceneCommand = new AsyncRelayCommand(
                () => AddSceneAsync(chapterNode.Id, CancellationToken.None));
            Chapters.Add(chapterNode);
        }

        AddChapterCommand.NotifyCanExecuteChanged();
    }

    private SceneNode CreateSceneNode(SceneDto scene)
        => new(
            scene.Id,
            scene.Order,
            scene.Title,
            scene.Words,
            scene.Status,
            StatusBrushes[scene.Status]);

    private async Task ApplyAsync(Task<Result<ProjectDto>> operation, CancellationToken cancellationToken)
    {
        var result = await operation.ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error is null
                ? "Операция не выполнена."
                : result.Error.UserMessage;
            return;
        }

        ErrorMessage = null;
        Refresh();
    }
}

