namespace ScriSelfStudio.ArchitectureUI;

using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.ArchitectureUI.Map;
using ScriSelfStudio.BookArchitecture.Map;
using ScriSelfStudio.Presentation;

/// <summary>
/// Экран структуры книги: дерево серий, книг, глав и сцен, команды создания,
/// переименования, удаления, перемещения и открытия сцены (ТЗ п. 24, 27, 35).
/// </summary>
public sealed partial class ArchitectureViewModel : ViewModelBase, IDisposable
{
    private readonly IBookArchitectureService _architecture;
    private readonly IProjectService _projects;
    private readonly IEditorEngine _editor;
    private readonly INavigationService _navigation;
    private readonly IFileDialogService _dialogs;

    [ObservableProperty]
    private ArchitectureNodeViewModel? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    /// <summary>Карта книги — основной вид экрана; список остаётся для быстрых операций.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListMode))]
    private bool _isMapMode = true;

    [ObservableProperty]
    private ArchitectureNodeViewModel? _selectedBook;

    [ObservableProperty]
    private double _mapViewportWidth;

    [ObservableProperty]
    private bool _mapFits = true;

    /// <summary>Масштаб карты: 1.0 — 100%. Меняет ширину столбцов, а не трансформацию.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomLabel))]
    private double _zoom = 1d;

    /// <summary>«Полный холст» карты: правая панель сцены скрыта, доска занимает всё место.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSceneDetails))]
    private bool _isCompact;

    /// <summary>Полоса «Быстрый обзор» развёрнута; сворачивание — как в макете.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverviewChevron))]
    [NotifyPropertyChangedFor(nameof(ShowLegend))]
    private bool _isOverviewExpanded = true;

    /// <summary>Выбранная в шапке линия фильтра; <c>null</c> в «Все линии».</summary>
    [ObservableProperty]
    private PlotLineFilterOption? _selectedLineFilter;

    /// <summary>Правая панель «Сцена»: сведения о выбранной сцене (ТЗ п. 27).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSceneDetails))]
    [NotifyPropertyChangedFor(nameof(ShowSceneDetails))]
    private SceneDetailsViewModel? _sceneDetails;

    public ArchitectureViewModel(
        IBookArchitectureService architecture,
        IProjectService projects,
        IEditorEngine editor,
        INavigationService navigation,
        IFileDialogService dialogs)
    {
        _architecture = architecture;
        _projects = projects;
        _editor = editor;
        _navigation = navigation;
        _dialogs = dialogs;
        _navigation.Navigated += OnNavigated;
        Refresh();
    }

    public ObservableCollection<ArchitectureNodeViewModel> Roots { get; } = [];

    /// <summary>Книги проекта для переключения карты между ними.</summary>
    public ObservableCollection<ArchitectureNodeViewModel> Books { get; } = [];

    /// <summary>Столбцы карты: одна глава — один столбец (ТЗ п. 27).</summary>
    public ObservableCollection<BookMapColumnViewModel> MapColumns { get; } = [];

    /// <summary>Легенда линий: те же цвета, что и отметки на сценах (ТЗ п. 27).</summary>
    public ObservableCollection<PlotLineMarker> PlotLineLegend { get; } = [];

    /// <summary>Главы книги для полосы «Быстрый обзор» (ТЗ п. 27).</summary>
    public ObservableCollection<ChapterOverviewViewModel> ChapterOverview { get; } = [];

    /// <summary>Фильтр линий в шапке карты: «Все линии» и по одной линии проекта.</summary>
    public ObservableCollection<PlotLineFilterOption> LineFilters { get; } = [];

    private const double ZoomStep = 0.1d;
    private const double MinZoom = 0.6d;
    private const double MaxZoom = 1.4d;

    private IReadOnlyDictionary<Guid, IReadOnlyList<PlotLineMarker>> _markersByScene =
        new Dictionary<Guid, IReadOnlyList<PlotLineMarker>>();

    private bool _suspendLineFilter;

    public bool IsListMode => !IsMapMode;

    public double MapContentWidth { get; private set; }

    /// <summary>Глава выбрана или в ней выбрана сцена — столбец раскрыт.</summary>
    public bool HasMapColumns => MapColumns.Count > 0;

    /// <summary>Есть книги — над картой показываем переключатель книги.</summary>
    public bool HasBooks => Books.Count > 0;

    /// <summary>Карта пуста: вместо приглушённой подсказки показываем карточку с действием.</summary>
    public bool ShowMapEmpty => !HasMapColumns;

    public bool HasPlotLines => PlotLineLegend.Count > 0;

    /// <summary>Легенда линий видна только при развёрнутой полосе «Быстрый обзор».</summary>
    public bool ShowLegend => HasPlotLines && IsOverviewExpanded;

    /// <summary>Правая панель показывается только при выбранной сцене (ТЗ п. 27).</summary>
    public bool HasSceneDetails => SceneDetails is not null;

    /// <summary>Правая панель скрывается в режиме «Полный холст».</summary>
    public bool ShowSceneDetails => HasSceneDetails && !IsCompact;

    public string ZoomLabel => $"{Math.Round(Zoom * 100d)}%";

    /// <summary>Стрелка сворачивания полосы «Быстрый обзор»: ▼ развёрнута, ▶ свёрнута.</summary>
    public string OverviewChevron => IsOverviewExpanded ? "\uE70D" : "\uE70E";

    public bool CanZoomIn => Zoom < MaxZoom - 0.001d;

    public bool CanZoomOut => Zoom > MinZoom + 0.001d;

    /// <summary>Что делать, если карта пустая: без подсказки экран выглядит сломанным.</summary>
    public string MapHint => SelectedBook is null
        ? "В проекте нет книг. Создайте книгу в списке."
        : "В выбранной книге нет глав. Создайте главу в списке.";

    /// <summary>Подпись кнопки на пустой карте: следующий недостающий элемент структуры.</summary>
    public string MapEmptyActionLabel => SelectedBook is null ? "Создать книгу" : "Создать главу";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool HasProject => _projects.Current is not null;

    public string ProjectTitle => _projects.Current?.Name ?? "Проект не открыт";

    public bool HasStructure => Roots.Count > 0;

    /// <summary>При входе на экран дерево перечитывается из сервиса структуры.</summary>
    public void Refresh()
    {
        // Свёрнутость и выбранный элемент переживают перестроение дерева: пользователь
        // создаёт элементы подряд, и выбор не должен сбрасываться (ТЗ п. 27).
        var known = Roots.SelectMany(Enumerate).ToDictionary(node => node.Id, node => node.IsExpanded);
        var selectedId = Selected?.Id;

        Roots.Clear();
        foreach (var series in _architecture.GetSeries())
        {
            Roots.Add(ArchitectureNodeViewModel.ForSeries(series));
        }

        foreach (var node in Roots.SelectMany(Enumerate))
        {
            if (known.TryGetValue(node.Id, out var wasExpanded))
            {
                node.IsExpanded = wasExpanded;
            }
        }

        Selected = selectedId is { } id
            ? Roots.SelectMany(Enumerate).FirstOrDefault(node => node.Id == id)
            : null;
        RefreshBooks();
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ProjectTitle));
        OnPropertyChanged(nameof(HasStructure));
        RebuildMap();
        NotifyCommands();
    }

    /// <summary>Выбор узла из карты или списка: выделение одно на оба вида.</summary>
    [RelayCommand]
    private void SelectNode(ArchitectureNodeViewModel? node) => Selected = node;

    [RelayCommand]
    private void ShowMap() => IsMapMode = true;

    [RelayCommand]
    private void ShowList() => IsMapMode = false;

    /// <summary>Вторая вкладка шапки — экран сюжетных линий (ТЗ п. 27).</summary>
    [RelayCommand]
    private void ShowTimeline() => _navigation.NavigateTo(AppScreen.Timeline);

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn() => Zoom = Math.Min(MaxZoom, Zoom + ZoomStep);

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut() => Zoom = Math.Max(MinZoom, Zoom - ZoomStep);

    [RelayCommand]
    private void ResetZoom() => Zoom = 1d;

    /// <summary>Свернуть или развернуть полосу «Быстрый обзор» (как в макете).</summary>
    [RelayCommand]
    private void ToggleOverview() => IsOverviewExpanded = !IsOverviewExpanded;

    /// <summary>
    /// Кнопка на пустой карте: создаёт книгу, если книг нет, иначе — главу в книге,
    /// которая показана на карте. Так следующий шаг всегда доступен одним нажатием.
    /// </summary>
    [RelayCommand]
    private Task MapEmptyActionAsync(CancellationToken cancellationToken) =>
        SelectedBook is null
            ? CreateBookAsync(cancellationToken)
            : CreateChapterInBookAsync(cancellationToken);

    partial void OnMapViewportWidthChanged(double value) => ApplyMapLayout();

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomLabel));
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        ApplyMapLayout();
    }

    partial void OnSelectedLineFilterChanged(PlotLineFilterOption? value)
    {
        if (_suspendLineFilter)
        {
            return;
        }

        ApplyLineFilter();
        UpdateMapSelection();
    }

    partial void OnSelectedBookChanged(ArchitectureNodeViewModel? value)
    {
        RebuildMap();
        CreateChapterInBookCommand.NotifyCanExecuteChanged();
    }

    private void RefreshBooks()
    {
        var knownId = SelectedBook?.Id;
        Books.Clear();
        foreach (var book in Roots.SelectMany(Enumerate).Where(node => node.Kind == EntityKind.Book))
        {
            Books.Add(book);
        }

        SelectedBook = (knownId is { } id ? Books.FirstOrDefault(book => book.Id == id) : null)
                       ?? Books.FirstOrDefault();
        OnPropertyChanged(nameof(HasBooks));
        OnPropertyChanged(nameof(MapHint));
        OnPropertyChanged(nameof(ShowMapEmpty));
        OnPropertyChanged(nameof(MapEmptyActionLabel));
    }

    /// <summary>Собрать столбцы карты по выбранной книге.</summary>
    private void RebuildMap()
    {
        MapColumns.Clear();
        ChapterOverview.Clear();
        _markersByScene = BuildPlotLineMarkers();
        var chapters = SelectedBook?.Children ?? [];
        for (var index = 0; index < chapters.Count; index++)
        {
            var chapter = chapters[index];
            MapColumns.Add(new BookMapColumnViewModel(
                chapter,
                index + 1,
                index,
                SelectNodeCommand,
                CreateSceneInColumnCommand,
                _markersByScene));
            ChapterOverview.Add(new ChapterOverviewViewModel(chapter, index + 1, SelectNodeCommand));
        }

        ApplyLineFilter();
        ApplyMapLayout();
        OnPropertyChanged(nameof(HasMapColumns));
        OnPropertyChanged(nameof(MapHint));
        OnPropertyChanged(nameof(ShowMapEmpty));
        OnPropertyChanged(nameof(MapEmptyActionLabel));
        UpdateMapSelection();
    }

    /// <summary>
    /// Сюжетные линии по сценам: линия отмечается на сцене, если она указана у линии
    /// или у любого её этапа. Карта показывает это цветными точками (ТЗ п. 27).
    /// </summary>
    private IReadOnlyDictionary<Guid, IReadOnlyList<PlotLineMarker>> BuildPlotLineMarkers()
    {
        var byScene = new Dictionary<Guid, List<PlotLineMarker>>();
        PlotLineLegend.Clear();
        var previousFilterId = SelectedLineFilter?.Id;
        LineFilters.Clear();
        LineFilters.Add(new PlotLineFilterOption(null, "Все линии", null));

        foreach (var line in _architecture.GetPlotLines())
        {
            var brush = StoryText.BrushFromHex(line.ColorHex, Brushes.Gray);
            var marker = new PlotLineMarker(
                line.Id,
                line.Name,
                brush,
                IsClosed: line.Status == PlotLineStatus.Done);
            PlotLineLegend.Add(marker);
            LineFilters.Add(new PlotLineFilterOption(line.Id, line.Name, brush));

            var sceneIds = line.SceneIds
                .Concat(line.Steps.SelectMany(step => step.SceneIds))
                .Distinct();
            foreach (var sceneId in sceneIds)
            {
                if (!byScene.TryGetValue(sceneId, out var list))
                {
                    list = [];
                    byScene[sceneId] = list;
                }

                list.Add(marker);
            }
        }

        _suspendLineFilter = true;
        try
        {
            SelectedLineFilter = LineFilters.FirstOrDefault(option => option.Id == previousFilterId)
                ?? LineFilters[0];
        }
        finally
        {
            _suspendLineFilter = false;
        }

        OnPropertyChanged(nameof(HasPlotLines));
        OnPropertyChanged(nameof(ShowLegend));
        return byScene.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlotLineMarker>)pair.Value);
    }

    /// <summary>Фильтр «Все линии / конкретная»: приглушает сцены без выбранной линии.</summary>
    private void ApplyLineFilter()
    {
        var filterId = SelectedLineFilter?.Id;
        foreach (var column in MapColumns)
        {
            foreach (var scene in column.Scenes)
            {
                var all = _markersByScene.TryGetValue(scene.Id, out var markers) ? markers : [];
                var visible = filterId is { } id
                    ? all.Where(marker => marker.Id == id).ToList()
                    : all.ToList();
                scene.SetLines(visible);
                scene.IsDimmed = filterId is not null && visible.Count == 0;
            }
        }
    }

    /// <summary>
    /// Геометрия карты: столбцы одинаковой ширины, позиция зависит только от индекса.
    /// Масштаб меняет ширину столбца и зазор, а не трансформацию холста (ТЗ п. 27).
    /// </summary>
    private void ApplyMapLayout()
    {
        var defaults = BookMapLayoutOptions.Default;
        var options = defaults with
        {
            ColumnWidth = defaults.ColumnWidth * Zoom,
            Gap = defaults.Gap * Zoom,
        };
        var layout = BookMapLayoutCalculator.Calculate(MapColumns.Count, MapViewportWidth, options);

        for (var index = 0; index < MapColumns.Count; index++)
        {
            MapColumns[index].ApplyGeometry(layout.Columns[index]);
        }

        MapFits = layout.Fits;
        MapContentWidth = layout.ContentWidth;
        OnPropertyChanged(nameof(MapContentWidth));
        OnPropertyChanged(nameof(HasMapColumns));
    }

    /// <summary>Подсветка выбора: выбранная глава, выбранная сцена и правая панель.</summary>
    private void UpdateMapSelection()
    {
        foreach (var column in MapColumns)
        {
            var isActive = Selected is { } node &&
                (node.Id == column.Node.Id || node.FindAncestor(EntityKind.Chapter)?.Id == column.Node.Id);
            column.IsActive = isActive;
            foreach (var scene in column.Scenes)
            {
                scene.IsSelected = Selected is { } selected && scene.Id == selected.Id;
            }
        }

        foreach (var overview in ChapterOverview)
        {
            overview.IsSelected = Selected is { } selected &&
                (overview.Node.Id == selected.Id
                 || selected.FindAncestor(EntityKind.Chapter)?.Id == overview.Node.Id);
        }

        BuildSceneDetails();
    }

    /// <summary>
    /// Правая панель «Сцена»: номер, хлебные крошки, содержание, линии со статусами,
    /// персонажи с этапами и заметки (ТЗ п. 27).
    /// </summary>
    private void BuildSceneDetails()
    {
        if (_projects.Current is not { } snapshot || Selected is not { Kind: EntityKind.Scene } node)
        {
            SceneDetails = null;
            return;
        }

        var scene = snapshot.Series
            .SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Scenes)
            .FirstOrDefault(candidate => candidate.Id == node.Id);
        if (scene is null)
        {
            SceneDetails = null;
            return;
        }

        var column = MapColumns.FirstOrDefault(candidate =>
            candidate.Node.Id == node.FindAncestor(EntityKind.Chapter)?.Id);
        var numberLabel = column?.Scenes.FirstOrDefault(cell => cell.Id == node.Id)?.Number ?? node.Title;
        var sceneIndex = column is null
            ? 1
            : column.Scenes.ToList().FindIndex(cell => cell.Id == node.Id) + 1;
        var bookTitle = node.FindAncestor(EntityKind.Book)?.Title ?? "Книга";
        var breadcrumb = $"{bookTitle} / Глава {column?.Number ?? 0} / Сцена {sceneIndex}";

        var touchingLines = snapshot.PlotLines.Where(line => TouchesScene(line, node.Id)).ToList();
        var lines = touchingLines
            .Select(line => new SceneLineEntry(
                line.Id,
                [.. line.Steps.Where(step => step.SceneIds.Contains(node.Id)).Select(step => step.Id)],
                line.Name,
                StoryText.BrushFromHex(line.ColorHex, Brushes.Gray),
                StoryText.LineStatusLabel(line.Status),
                StoryText.StatusDot(line.Status)))
            .ToList();

        var lineChoices = snapshot.PlotLines
            .Where(line => line.Steps.Count > 0)
            .Select(line => new SceneLineOption(
                line.Id,
                line.Name,
                [.. line.Steps
                    .OrderBy(step => step.Order)
                    .Select((step, index) => new SceneStepOption(step.Id, step.Title, index + 1))]))
            .ToList();

        SceneDetails = new SceneDetailsViewModel(
            scene.Id,
            numberLabel,
            scene.Title,
            breadcrumb,
            scene.Description,
            StoryText.SceneStatusLabel(scene.Status),
            WordsLabel(scene.Words),
            scene.IsKey,
            scene.IsLocked,
            scene.Notes,
            lines,
            BuildSceneCharacters(snapshot, touchingLines, node.Id),
            lineChoices,
            OpenSceneAsync,
            ToggleKeyAsync,
            ToggleLockAsync,
            (stepId, cancellationToken) => AddSceneLineAsync(scene.Id, stepId, cancellationToken),
            (lineId, cancellationToken) => RemoveSceneLineAsync(scene.Id, lineId, cancellationToken));
    }

    /// <summary>Персонажи сцены: участники этапов линий, привязанных к этой сцене.</summary>
    private static IReadOnlyList<SceneCharacterEntry> BuildSceneCharacters(
        ProjectDto snapshot,
        IReadOnlyList<PlotLineDto> touchingLines,
        Guid sceneId)
    {
        var characters = new List<SceneCharacterEntry>();
        var seen = new HashSet<Guid>();
        foreach (var line in touchingLines)
        {
            var brush = StoryText.BrushFromHex(line.ColorHex, Brushes.Gray);
            foreach (var step in line.Steps.Where(step => step.SceneIds.Contains(sceneId)))
            {
                foreach (var participant in step.Participants)
                {
                    if (!seen.Add(participant.CharacterId))
                    {
                        continue;
                    }

                    var character = snapshot.Characters
                        .FirstOrDefault(candidate => candidate.Id == participant.CharacterId);
                    if (character is null)
                    {
                        continue;
                    }

                    characters.Add(new SceneCharacterEntry(
                        character.Name.Length > 0 ? character.Name[0].ToString() : "?",
                        character.Name,
                        StoryText.RoleLabel(character.Role),
                        $"Шаг: {step.Title}",
                        brush,
                        HasStep: true));
                }
            }
        }

        return characters;
    }

    /// <summary>
    /// Карта тоже задаёт связь «сцена ↔ линия» со стороны сцены (ADR 012): шаг линии
    /// добавляется к сцене и линия сразу появляется на карте.
    /// </summary>
    private async Task AddSceneLineAsync(Guid sceneId, Guid stepId, CancellationToken cancellationToken)
    {
        var stepIds = CurrentSceneStepIds(sceneId).Append(stepId).Distinct().ToList();
        await ApplySceneLinesAsync(sceneId, stepIds, cancellationToken);
    }

    /// <summary>Снять линию со сцены: убираются все этапы этой линии, привязанные к сцене.</summary>
    private async Task RemoveSceneLineAsync(Guid sceneId, Guid lineId, CancellationToken cancellationToken)
    {
        var lineSteps = _projects.Current?.PlotLines
            .Where(line => line.Id == lineId)
            .SelectMany(line => line.Steps)
            .Select(step => step.Id)
            .ToHashSet() ?? [];
        var stepIds = CurrentSceneStepIds(sceneId).Where(id => !lineSteps.Contains(id)).ToList();
        await ApplySceneLinesAsync(sceneId, stepIds, cancellationToken);
    }

    private IReadOnlyList<Guid> CurrentSceneStepIds(Guid sceneId) =>
        _projects.Current?.PlotLines
            .SelectMany(line => line.Steps)
            .Where(step => step.SceneIds.Contains(sceneId))
            .Select(step => step.Id)
            .ToList() ?? [];

    private async Task ApplySceneLinesAsync(
        Guid sceneId,
        IReadOnlyList<Guid> stepIds,
        CancellationToken cancellationToken)
    {
        ClearMessages();
        var result = await _projects.UpdateSceneAsync(new UpdateSceneRequest
        {
            SceneId = sceneId,
            PlotLineStepIds = stepIds,
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            await ReportAsync(result.Error!);
            return;
        }

        Refresh();
        StatusMessage = "Сюжетные линии сцены обновлены.";
    }

    private static bool TouchesScene(PlotLineDto line, Guid sceneId) =>
        line.SceneIds.Contains(sceneId) || line.Steps.Any(step => step.SceneIds.Contains(sceneId));

    private static string WordsLabel(int words) => words > 0
        ? $"{words} {StoryText.Plural(words, "слово", "слова", "слов")}"
        : "текста нет";

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task CreateSeriesAsync(CancellationToken cancellationToken)
    {
        var title = await _dialogs.PromptAsync("Новая серия", "Название серии", "Серия", cancellationToken);
        if (title is null)
        {
            return;
        }

        await ApplyAsync(_architecture.CreateSeriesAsync(new CreateSeriesRequest { Title = title }, cancellationToken));
    }

    [RelayCommand(CanExecute = nameof(CanCreateBook))]
    private async Task CreateBookAsync(CancellationToken cancellationToken)
    {
        var series = ResolveSeriesTarget();
        if (series is null)
        {
            return;
        }

        var title = await _dialogs.PromptAsync("Новая книга", "Название книги", "Книга", cancellationToken);
        if (title is null)
        {
            return;
        }

        await ApplyAsync(
            _architecture.CreateBookAsync(new CreateBookRequest { SeriesId = series.Id, Title = title }, cancellationToken),
            selectCreated: true);
    }

    [RelayCommand(CanExecute = nameof(CanCreateChapter))]
    private async Task CreateChapterAsync(CancellationToken cancellationToken)
    {
        var book = ResolveBookTarget();
        if (book is null)
        {
            return;
        }

        var title = await _dialogs.PromptAsync("Новая глава", "Название главы", "Глава", cancellationToken);
        if (title is null)
        {
            return;
        }

        await ApplyAsync(
            _architecture.CreateChapterAsync(new CreateChapterRequest { BookId = book.Id, Title = title }, cancellationToken),
            selectCreated: true);
    }

    [RelayCommand(CanExecute = nameof(CanCreateScene))]
    private async Task CreateSceneAsync(CancellationToken cancellationToken)
    {
        var chapter = ResolveChapterTarget();
        if (chapter is null)
        {
            return;
        }

        var title = await _dialogs.PromptAsync("Новая сцена", "Название сцены", "Сцена", cancellationToken);
        if (title is null)
        {
            return;
        }

        await ApplyAsync(
            _architecture.CreateSceneAsync(new CreateSceneRequest { ChapterId = chapter.Id, Title = title }, cancellationToken),
            selectCreated: true);
    }

    /// <summary>Кнопка «+» в заголовке столбца: новая сцена в этой главе.</summary>
    [RelayCommand]
    private async Task CreateSceneInColumnAsync(BookMapColumnViewModel? column)
    {
        if (column is null || !HasProject)
        {
            return;
        }

        var title = await _dialogs.PromptAsync("Новая сцена", "Название сцены", "Сцена", CancellationToken.None);
        if (title is null)
        {
            return;
        }

        await ApplyAsync(
            _architecture.CreateSceneAsync(
                new CreateSceneRequest { ChapterId = column.Node.Id, Title = title },
                CancellationToken.None),
            selectCreated: true);
    }

    /// <summary>Кнопка «+» в «Быстром обзоре»: новая глава в выбранной книге.</summary>
    [RelayCommand(CanExecute = nameof(CanCreateChapterInBook))]
    private async Task CreateChapterInBookAsync(CancellationToken cancellationToken)
    {
        if (SelectedBook is null)
        {
            return;
        }

        var title = await _dialogs.PromptAsync("Новая глава", "Название главы", "Глава", cancellationToken);
        if (title is null)
        {
            return;
        }

        await ApplyAsync(
            _architecture.CreateChapterAsync(
                new CreateChapterRequest { BookId = SelectedBook.Id, Title = title },
                cancellationToken),
            selectCreated: true);
    }

    private bool CanCreateChapterInBook => HasProject && SelectedBook is not null;

    /// <summary>Звезда в правой панели: пометить сцену ключевой или снять отметку (ТЗ п. 27).</summary>
    [RelayCommand(CanExecute = nameof(IsSceneSelected))]
    private async Task ToggleKeyAsync(CancellationToken cancellationToken)
    {
        if (Selected is not { Kind: EntityKind.Scene } scene)
        {
            return;
        }

        await ApplyAsync(_projects.UpdateSceneAsync(
            new UpdateSceneRequest { SceneId = scene.Id, IsKey = !scene.IsKey },
            cancellationToken));
    }

    /// <summary>Замок в правой панели: заблокировать сцену от правок или снять блокировку.</summary>
    [RelayCommand(CanExecute = nameof(IsSceneSelected))]
    private async Task ToggleLockAsync(CancellationToken cancellationToken)
    {
        if (Selected is not { Kind: EntityKind.Scene } scene)
        {
            return;
        }

        await ApplyAsync(_projects.UpdateSceneAsync(
            new UpdateSceneRequest { SceneId = scene.Id, IsLocked = !scene.IsLocked },
            cancellationToken));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameAsync(CancellationToken cancellationToken)
    {
        if (Selected is not { } node)
        {
            return;
        }

        var title = await _dialogs.PromptAsync("Переименовать", node.KindLabel, node.Title, cancellationToken);
        if (title is null)
        {
            return;
        }

        await ApplyAsync(_architecture.RenameEntityAsync(new RenameEntityRequest
        {
            Kind = node.Kind,
            Id = node.Id,
            Title = title,
        }, cancellationToken));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (Selected is not { } node)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Удаление",
            $"Удалить «{node.Title}» ({node.KindLabel})? Содержимое будет скрыто.",
            "Удалить",
            "Отмена",
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        await ApplyAsync(_architecture.DeleteEntityAsync(new DeleteEntityRequest
        {
            Kind = node.Kind,
            Id = node.Id,
        }, cancellationToken));
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private Task MoveUpAsync(CancellationToken cancellationToken) =>
        MoveAsync(-1, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private Task MoveDownAsync(CancellationToken cancellationToken) =>
        MoveAsync(+1, cancellationToken);

    [RelayCommand(CanExecute = nameof(IsSceneSelected))]
    private async Task OpenSceneAsync(CancellationToken cancellationToken)
    {
        if (Selected is not { Kind: EntityKind.Scene } scene)
        {
            return;
        }

        ClearMessages();
        var opened = await _editor.OpenSceneAsync(scene.Id, cancellationToken);
        if (!opened.IsSuccess)
        {
            await ReportAsync(opened.Error!);
            return;
        }

        _navigation.NavigateTo(AppScreen.Editor);
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    public void Dispose() => _navigation.Navigated -= OnNavigated;

    private void OnNavigated(object? sender, AppScreen screen)
    {
        if (screen == AppScreen.Architecture)
        {
            Refresh();
        }
    }

    public bool HasSelection => Selected is not null;

    /// <summary>Книга создаётся в выбранной серии, иначе — в первой серии проекта.</summary>
    public bool CanCreateBook => HasProject && ResolveSeriesTarget() is not null;

    /// <summary>Глава создаётся в выбранной книге, иначе — в книге, показанной на карте.</summary>
    public bool CanCreateChapter => HasProject && ResolveBookTarget() is not null;

    /// <summary>Сцена создаётся в выбранной главе, иначе — в первой главе выбранной книги.</summary>
    public bool CanCreateScene => HasProject && ResolveChapterTarget() is not null;

    private ArchitectureNodeViewModel? ResolveSeriesTarget() =>
        Selected?.FindAncestor(EntityKind.Series) ?? Roots.FirstOrDefault();

    private ArchitectureNodeViewModel? ResolveBookTarget() =>
        Selected?.FindAncestor(EntityKind.Book) ?? SelectedBook ?? Books.FirstOrDefault();

    private ArchitectureNodeViewModel? ResolveChapterTarget() =>
        Selected?.FindAncestor(EntityKind.Chapter) ?? SelectedBook?.Children.FirstOrDefault();

    public bool IsSceneSelected => Selected is { Kind: EntityKind.Scene };

    private bool CanMoveUp => HasSelection && Selected!.Order > 0;

    private bool CanMoveDown => HasSelection;

    partial void OnSelectedChanged(ArchitectureNodeViewModel? value)
    {
        UpdateMapSelection();
        NotifyCommands();
    }

    private async Task MoveAsync(int delta, CancellationToken cancellationToken)
    {
        if (Selected is not { } node)
        {
            return;
        }

        await ApplyAsync(_architecture.MoveEntityAsync(new MoveEntityRequest
        {
            Kind = node.Kind,
            Id = node.Id,
            NewOrder = node.Order + delta,
        }, cancellationToken));
    }

    private async Task ApplyAsync(Task<Result<ProjectDto>> operation, bool selectCreated = false)
    {
        ClearMessages();
        // Для команд создания запоминаем узлы до правки: после перестроения дерева
        // новый узел выделяется, поэтому карта и «Быстрый обзор» сразу показывают его.
        var knownIds = selectCreated
            ? Roots.SelectMany(Enumerate).Select(node => node.Id).ToHashSet()
            : null;

        var result = await operation;
        if (!result.IsSuccess)
        {
            await ReportAsync(result.Error!);
            return;
        }

        Refresh();

        if (knownIds is not null)
        {
            var created = Roots.SelectMany(Enumerate).FirstOrDefault(node => !knownIds.Contains(node.Id));
            if (created is not null)
            {
                if (created.Kind == EntityKind.Book)
                {
                    SelectedBook = created;
                }

                Selected = created;
            }
        }

        StatusMessage = "Структура обновлена.";
    }

    private async Task ReportAsync(AppError error)
    {
        ErrorMessage = error.SuggestedAction is null
            ? error.UserMessage
            : $"{error.UserMessage} {error.SuggestedAction}";
        await _dialogs.ShowErrorAsync("Ошибка структуры", ErrorMessage);
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        StatusMessage = null;
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanCreateBook));
        OnPropertyChanged(nameof(CanCreateChapter));
        OnPropertyChanged(nameof(CanCreateScene));
        OnPropertyChanged(nameof(IsSceneSelected));
        CreateSeriesCommand.NotifyCanExecuteChanged();
        CreateBookCommand.NotifyCanExecuteChanged();
        CreateChapterCommand.NotifyCanExecuteChanged();
        CreateSceneCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        OpenSceneCommand.NotifyCanExecuteChanged();
        ToggleKeyCommand.NotifyCanExecuteChanged();
        ToggleLockCommand.NotifyCanExecuteChanged();
        CreateChapterInBookCommand.NotifyCanExecuteChanged();
    }

    private static IEnumerable<ArchitectureNodeViewModel> Enumerate(ArchitectureNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Enumerate))
        {
            yield return child;
        }
    }
}
