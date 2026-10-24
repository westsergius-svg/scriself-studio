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
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Localization;
using System.Windows.Input;

/// <summary>Книга в списке левого дерева: вариант для выпадающего списка.</summary>
public sealed record BookOption(Guid Id, string Title)
{
    /// <summary>ComboBox показывает элементы через <c>ToString</c>.</summary>
    public override string ToString() => Title;
}

internal static class BookTreeLabels
{
    public static string NumberedTitle(string kind, int order, string title)
    {
        var prefix = $"{kind} {order + 1}";
        var displayPrefix = $"{Loc.T(kind)} {order + 1}";
        return title.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)
            && (title.Length == prefix.Length || title[prefix.Length] is '.' or ':' or ' ')
            ? displayPrefix + title[prefix.Length..]
            : $"{displayPrefix}. {title}";
    }
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

    public string DisplayTitle => BookTreeLabels.NumberedTitle("Сцена", Order, Title);

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

    public string DisplayTitle => BookTreeLabels.NumberedTitle("Глава", Order, Title);

    public IReadOnlyList<SceneNode> Scenes { get; }

    /// <summary>Команда добавления сцены в эту главу, привязанная деревом.</summary>
    public ICommand? AddSceneCommand { get; set; }

    /// <summary>Номер главы: «1», «2», …</summary>
    public string Number => Order.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    [NotifyPropertyChangedFor(nameof(IsCollapsed))]
    private bool _isExpanded;

    public string Chevron => IsExpanded ? "\uE70D" : "\uE76C";

    /// <summary>Обратное состояние: шеврон показывает свёрнутую главу другим значком.</summary>
    public bool IsCollapsed => !IsExpanded;

    /// <summary>Раскрыть или свернуть главу (клик по шеврону).</summary>
    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}

/// <summary>Книга в иерархическом дереве редактора.</summary>
public sealed partial class BookNode : ObservableObject
{
    public BookNode(Guid id, int order, string title, IReadOnlyList<ChapterNode> chapters)
    {
        Id = id;
        Order = order;
        Title = title;
        Chapters = chapters;
    }

    public Guid Id { get; }

    public string Title { get; }

    public int Order { get; }

    public string DisplayTitle => BookTreeLabels.NumberedTitle("Книга", Order, Title);

    public IReadOnlyList<ChapterNode> Chapters { get; }

    public ICommand? ToggleCommand { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    private bool _isExpanded;

    public string Chevron => IsExpanded ? "\uE70D" : "\uE76C";
}

/// <summary>Серия проекта — корневой раскрываемый узел дерева.</summary>
public sealed partial class SeriesNode : ObservableObject
{
    public SeriesNode(Guid id, string title, IReadOnlyList<BookNode> books)
    {
        Id = id;
        Title = title;
        Books = books;
    }

    public Guid Id { get; }

    public string Title { get; }

    public IReadOnlyList<BookNode> Books { get; }

    public string DisplayTitle => Title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    private bool _isExpanded;

    public string Chevron => IsExpanded ? "\uE70D" : "\uE76C";

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}

/// <summary>
/// Левое рабочее пространство (ТЗ п. 24, 27): дерево серий, книг, глав и сцен
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

    /// <summary>Мягкая заливка выбранной сцены в дереве.</summary>
    private static readonly IBrush RowActiveBrush = new SolidColorBrush(Color.Parse("#F3E3DB"));

    private readonly IProjectService _projects;
    private readonly IBookArchitectureService _architecture;
    private readonly IEditorEngine _editorEngine;
    private readonly EditorViewModel _editor;
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

    /// <summary>Полное дерево серий, книг, глав и сцен для панели редактора.</summary>
    public ObservableCollection<SeriesNode> Series { get; } = [];

    /// <summary>Главы выбранной книги со сценами.</summary>
    public ObservableCollection<ChapterNode> Chapters { get; } = [];

    public bool HasBooks => Books.Count > 0;

    public bool HasChapters => Chapters.Count > 0;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public BookTreeViewModel(
        IProjectService projects,
        IBookArchitectureService architecture,
        IEditorEngine editorEngine,
        EditorViewModel editor,
        INavigationService navigation,
        IFileDialogService dialogs,
        IEventBus events)
    {
        _projects = projects;
        _architecture = architecture;
        _editorEngine = editorEngine;
        _editor = editor;
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
        if (current is null)
        {
            return;
        }

        foreach (var chapter in Chapters)
        {
            chapter.IsExpanded = chapter.Id == current.Value;
        }

        foreach (var series in Series)
        {
            foreach (var book in series.Books)
            {
                var containsCurrent = book.Chapters.Any(chapter => chapter.Id == current);
                book.IsExpanded = containsCurrent;
                foreach (var chapter in book.Chapters)
                {
                    chapter.IsExpanded = chapter.Id == current.Value;
                }
            }

            series.IsExpanded = series.Books.Any(book => book.IsExpanded);
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

        var title = await _dialogs.PromptAsync(Loc.T("Новая глава"), Loc.T("Название главы"), Loc.T("Глава"), cancellationToken);
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
        var title = await _dialogs.PromptAsync(Loc.T("Новая сцена"), Loc.T("Название сцены"), Loc.T("Сцена"), cancellationToken);
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
        if (await _editor.OpenSceneFromBookAsync(scene.Id, cancellationToken))
        {
            return;
        }

        ErrorMessage = _editor.ErrorMessage ?? Loc.T("Не удалось открыть сцену.");
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
        var hadTree = Series.Count > 0;
        var previousBook = SelectedBook?.Id;
        var previousScene = _editorEngine.SceneId;
        var expanded = Chapters
            .Where(chapter => chapter.IsExpanded)
            .Select(chapter => chapter.Id)
            .ToHashSet();
        var expandedSeries = Series
            .Where(series => series.IsExpanded)
            .Select(series => series.Id)
            .ToHashSet();
        var expandedBooks = Series
            .SelectMany(series => series.Books)
            .Where(book => book.IsExpanded)
            .Select(book => book.Id)
            .ToHashSet();
        var expandedTreeChapters = Series
            .SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .Where(chapter => chapter.IsExpanded)
            .Select(chapter => chapter.Id)
            .ToHashSet();

        Books.Clear();
        Series.Clear();
        var project = _projects.Current;
        var seriesList = project?.Series ?? [];
        var firstBookId = seriesList.SelectMany(series => series.Books).FirstOrDefault()?.Id;
        var currentChapter = FindChapterForScene(previousScene);
        foreach (var book in seriesList.SelectMany(series => series.Books))
        {
            Books.Add(new BookOption(book.Id, book.Title));
        }

        SelectedBook = Books.FirstOrDefault(candidate => candidate.Id == previousBook)
            ?? Books.FirstOrDefault();

        foreach (var series in seriesList)
        {
            var bookNodes = new List<BookNode>();
            foreach (var book in series.Books)
            {
                var chapters = CreateChapterNodes(
                    book.Chapters,
                    hadTree ? expandedTreeChapters : null,
                    currentChapter,
                    book.Id == firstBookId);
                var bookNode = new BookNode(book.Id, book.Order, book.Title, chapters)
                {
                    IsExpanded = hadTree
                        ? expandedBooks.Contains(book.Id)
                        : book.Id == firstBookId || book.Chapters.Any(chapter => chapter.Id == currentChapter),
                };
                bookNode.ToggleCommand = new RelayCommand(() =>
                {
                    SelectedBook = Books.FirstOrDefault(option => option.Id == bookNode.Id);
                    bookNode.IsExpanded = !bookNode.IsExpanded;
                });
                bookNodes.Add(bookNode);
            }

            var seriesTitle = seriesList.Count == 1 ? project?.Name ?? series.Title : series.Title;
            Series.Add(new SeriesNode(series.Id, seriesTitle, bookNodes)
            {
                IsExpanded = !hadTree || expandedSeries.Contains(series.Id),
            });
        }

        RefreshChapters(hadTree ? expanded : null, previousScene);
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
        foreach (var chapterNode in CreateChapterNodes(book?.Chapters ?? [], expanded, currentChapter, true))
        {
            Chapters.Add(chapterNode);
        }

        AddChapterCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<ChapterNode> CreateChapterNodes(
        IReadOnlyList<ChapterDto> chapters,
        IReadOnlySet<Guid>? expanded,
        Guid? currentChapter,
        bool expandFirstWhenNoSelection)
    {
        var nodes = new List<ChapterNode>();
        foreach (var chapter in chapters)
        {
            var scenes = chapter.Scenes.Select(CreateSceneNode).ToList();
            foreach (var scene in scenes)
            {
                scene.OpenCommand = new AsyncRelayCommand(
                    () => OpenSceneAsync(scene, CancellationToken.None));
                scene.IsCurrent = scene.Id == _editorEngine.SceneId;
                scene.RowBackground = scene.IsCurrent ? RowActiveBrush : null;
            }

            var chapterNode = new ChapterNode(chapter.Id, chapter.Order, chapter.Title, scenes)
            {
                IsExpanded = expanded is not null
                    ? expanded.Contains(chapter.Id)
                    : chapter.Id == currentChapter
                        || (currentChapter is null && expandFirstWhenNoSelection && chapter.Order == 1),
            };
            chapterNode.AddSceneCommand = new AsyncRelayCommand(
                () => AddSceneAsync(chapterNode.Id, CancellationToken.None));
            nodes.Add(chapterNode);
        }

        return nodes;
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
                ? Loc.T("Операция не выполнена.")
                : result.Error.UserMessage;
            return;
        }

        ErrorMessage = null;
        Refresh();
    }
}
