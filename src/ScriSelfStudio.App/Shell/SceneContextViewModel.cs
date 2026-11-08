namespace ScriSelfStudio.App.Shell;

using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Events;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Characters;
using ScriSelfStudio.Presentation.Localization;
using ScriSelfStudio.StoryUI;

/// <summary>
/// Персонаж сцены в правой панели «Сцена» (ТЗ п. 27): имя, роль, цель и шаг линии,
/// через который персонаж попал в сцену (макет: роль в скобках, «Цель: …», чип этапа).
/// </summary>
public sealed partial class CharacterEntry : ObservableObject
{
    public Guid CharacterId { get; init; }
    public IReadOnlyList<CharacterProfileField> Profile { get; init; } = [];
    public bool HasProfile => Profile.Count > 0;
    public IReadOnlyList<CharacterArcStageDto> ArcStages { get; init; } = [];

    [ObservableProperty]
    private bool _isProfileExpanded;

    [RelayCommand]
    private void ToggleProfile() => IsProfileExpanded = !IsProfileExpanded;

    private readonly Action<CharacterEntry> _toggleDescription;

    public CharacterEntry(
        string initial,
        string name,
        string roleLabel,
        string? goal,
        bool hasGoal,
        string? stepLabel,
        string? stepDescription,
        bool hasStep,
        IBrush stepBrush,
        Action<CharacterEntry> toggleDescription)
    {
        Initial = initial;
        Name = name;
        RoleLabel = roleLabel;
        Goal = goal;
        HasGoal = hasGoal;
        StepLabel = stepLabel;
        StepDescription = stepDescription;
        HasStep = hasStep;
        StepBrush = stepBrush;
        _toggleDescription = toggleDescription;
    }

    public string Initial { get; }

    public string Name { get; }

    public string RoleLabel { get; }

    public string? Goal { get; }

    public bool HasGoal { get; }

    public string? StepLabel { get; }

    public string? StepDescription { get; }

    public bool HasStep { get; }

    /// <summary>Описание этапа (шага линии) или явная отметка, что оно не задано.</summary>
    public string StepDescriptionText => string.IsNullOrWhiteSpace(StepDescription)
        ? Loc.T("Описание этапа не задано.")
        : StepDescription!;

    public IBrush StepBrush { get; }

    [ObservableProperty]
    private bool _isDescriptionExpanded;

    [RelayCommand]
    private void ToggleDescription() => _toggleDescription(this);
}

/// <summary>Сюжетная линия сцены в правой панели (название и цвет линии).</summary>
public sealed record PlotLineEntry(string Name, IBrush Brush);

/// <summary>Сюжетная линия, к этапам которой можно отнести сцену (ADR 012).</summary>
public sealed record PlotLineOption(Guid Id, string Name)
{
    /// <summary>ComboBox показывает элемент через <c>ToString</c>.</summary>
    public override string ToString() => Name;
}

/// <summary>Шаг линии, к которому можно отнести сцену (ADR 012).</summary>
public sealed record StepOption(Guid Id, string Title, int Number)
{
    public string Label => $"{Number:00} · {Title}";

    public override string ToString() => Label;
}

/// <summary>Персонаж проекта, которого можно добавить в сцену через шаг линии (ADR 017).</summary>
public sealed record CharacterOption(Guid Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Назначенный сцене шаг линии: чип в панели контекста сцены. Команда снятия
/// живёт в самом чипе, чтобы разметке не приходилось искать модель представления
/// через дерево элементов.
/// </summary>
public sealed partial class SceneStepEntry : ObservableObject
{
    [ObservableProperty]
    private bool _canMoveUp;
    [ObservableProperty]
    private bool _canMoveDown;
    private readonly Func<SceneStepEntry, CancellationToken, Task> _remove;
    private readonly Action<SceneStepEntry> _toggleDescription;

    public SceneStepEntry(
        Guid stepId,
        string lineName,
        string stepTitle,
        string? lineDescription,
        string? stepDescription,
        IBrush brush,
        Func<SceneStepEntry, CancellationToken, Task> remove,
        Action<SceneStepEntry> toggleDescription)
    {
        StepId = stepId;
        LineName = lineName;
        StepTitle = stepTitle;
        LineDescription = lineDescription;
        StepDescription = stepDescription;
        Brush = brush;
        _remove = remove;
        _toggleDescription = toggleDescription;
    }

    public Guid StepId { get; }

    public string LineName { get; }

    public string StepTitle { get; }

    public string? LineDescription { get; }

    public string? StepDescription { get; }

    public bool HasLineDescription => !string.IsNullOrWhiteSpace(LineDescription);

    public bool HasStepDescription => !string.IsNullOrWhiteSpace(StepDescription);

    public IBrush Brush { get; }

    public string Label => $"{LineName} · {StepTitle}";

    /// <summary>Заголовок раскрытого описания: сразу видно, к какому этапу оно относится.</summary>
    public string StepHeading => Loc.F("Этап «{0}»", StepTitle);

    /// <summary>Заголовок блока линии — чтобы описание линии не принимали за описание этапа.</summary>
    public string LineHeading => Loc.F("Линия «{0}»", LineName);

    /// <summary>Описание этапа; если его нет — прямо об этом сообщаем.</summary>
    public string StepDescriptionText => HasStepDescription ? StepDescription! : Loc.T("Описание этапа не задано.");

    [ObservableProperty]
    private bool _isDescriptionExpanded;

    [RelayCommand]
    private void ToggleDescription() => _toggleDescription(this);

    [RelayCommand]
    private Task RemoveAsync(CancellationToken cancellationToken) => _remove(this, cancellationToken);
}

/// <summary>
/// Правая панель «Контекст сцены» (ТЗ п. 27): краткое содержание, персонажи,
/// сюжетные линии сцены, заметки и статус. Источник истины — проект; правки
/// отправляются одной операцией <c>UpdateSceneAsync</c>.
/// Связь «сцена ↔ шаг линии» задаётся здесь, со стороны сцены (ADR 012).
/// </summary>
public sealed partial class SceneContextViewModel : ViewModelBase, IDisposable
{
    private static readonly IBrush PlotLineFallback = new SolidColorBrush(Color.Parse("#B3565D"));

    private readonly IProjectService _projects;
    private readonly IEditorEngine _engine;
    private readonly INavigationService _navigation;
    private readonly IEventBus _events;
    private readonly IBookArchitectureService _architecture;
    private readonly List<IDisposable> _subscriptions = [];
    private Guid? _loadedSceneId;
    private bool _isEditorScreen;
    private bool _isLoadingMeta;
    private bool _metaDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public ObservableCollection<CharacterEntry> Characters { get; } = [];

    public ObservableCollection<PlotLineEntry> PlotLines { get; } = [];

    /// <summary>Шаги линий, назначенные сцене: снимаются кнопкой на чипе.</summary>
    public ObservableCollection<SceneStepEntry> AssignedSteps { get; } = [];

    /// <summary>Линии проекта для выбора этапа.</summary>
    public ObservableCollection<PlotLineOption> LineOptions { get; } = [];

    /// <summary>Шаги выбранной линии.</summary>
    public ObservableCollection<StepOption> StepOptions { get; } = [];

    /// <summary>Персонажи проекта для добавления в сцену через шаг линии (ADR 017).</summary>
    public ObservableCollection<CharacterOption> CharacterOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddStep))]
    private PlotLineOption? _selectedLineOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddStep))]
    private StepOption? _selectedStepOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddCharacter))]
    private CharacterOption? _selectedCharacterOption;

    [ObservableProperty]
    private bool _hasScene;

    [ObservableProperty]
    private string _sceneTitle = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusIdea))]
    [NotifyPropertyChangedFor(nameof(IsStatusDraft))]
    [NotifyPropertyChangedFor(nameof(IsStatusRevised))]
    [NotifyPropertyChangedFor(nameof(IsStatusFinal))]
    private SceneStatus _status = SceneStatus.Idea;

    /// <summary>Заголовок панели «Сцена»: название открытой сцены (макет).</summary>
    [ObservableProperty]
    private string _sceneHeading = string.Empty;

    /// <summary>Хлебные крошки панели: «Глава 1 / Сцена 1» (макет).</summary>
    [ObservableProperty]
    private string _breadcrumb = string.Empty;

    /// <summary>Заметки сцены (макет: карточка «Заметки»); <see cref="HasNotes"/> — есть текст.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private string? _notes;

    /// <summary>Список этапов развёрнут кнопкой «Показать все» (макет).</summary>
    [ObservableProperty]
    private bool _isLinesExpanded;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public bool HasCharacters => Characters.Count > 0;

    public bool IsStatusIdea => Status == SceneStatus.Idea;

    public bool IsStatusDraft => Status == SceneStatus.Draft;

    public bool IsStatusRevised => Status == SceneStatus.Revised;

    public bool IsStatusFinal => Status == SceneStatus.Final;

    public bool HasAssignedSteps => AssignedSteps.Count > 0;

    public bool HasLineOptions => LineOptions.Count > 0;

    public bool HasStepOptions => StepOptions.Count > 0 && SelectedLineOption is not null;

    public bool CanAddStep => HasScene && SelectedLineOption is not null && SelectedStepOption is not null;

    public bool CanAddCharacter => HasScene && AssignedSteps.Count > 0 && SelectedCharacterOption is not null;

    /// <summary>Есть несохранённые описание, статус или заметки сцены.</summary>
    public bool HasPendingChanges => HasScene && _metaDirty;

    /// <summary>Фиксирует контекст сцены перед закрытием приложения.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_metaDirty)
        {
            await PersistAsync(cancellationToken);
        }
    }

    partial void OnSummaryChanged(string value)
    {
        if (!_isLoadingMeta)
        {
            _metaDirty = true;
        }
    }

    partial void OnNotesChanged(string? value)
    {
        if (!_isLoadingMeta)
        {
            _metaDirty = true;
        }
    }

    partial void OnStatusChanged(SceneStatus value)
    {
        if (!_isLoadingMeta)
        {
            _metaDirty = true;
        }
    }

    /// <summary>Подписи статусов сцены для четырёх кнопок панели (ТЗ п. 10).</summary>
    public IReadOnlyList<Choice<SceneStatus>> StatusOptions { get; } =
    [
        new(SceneStatus.Idea, Loc.T("Идея")),
        new(SceneStatus.Draft, Loc.T("Черновик")),
        new(SceneStatus.Revised, Loc.T("В работе")),
        new(SceneStatus.Final, Loc.T("Готово")),
    ];

    public SceneContextViewModel(
        IProjectService projects,
        IEditorEngine engine,
        INavigationService navigation,
        IEventBus events,
        IBookArchitectureService architecture)
    {
        _projects = projects;
        _engine = engine;
        _navigation = navigation;
        _events = events;
        _architecture = architecture;

        _subscriptions.Add(events.Subscribe<SceneChanged>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<EditorDocumentChanged>(_ => OnEditorDocumentChanged()));
        _subscriptions.Add(events.Subscribe<PlotLineChanged>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<CharacterChanged>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<ProjectOpened>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<ProjectClosed>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<SaveCompleted>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<ProjectChanged>(_ => Refresh()));
        _navigation.Navigated += OnNavigated;
        _loadedSceneId = _engine.SceneId;
        _isEditorScreen = _navigation.Current == AppScreen.Editor;
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasScene))]
    private async Task SetStatusAsync(SceneStatus status, CancellationToken cancellationToken)
    {
        Status = status;
        await PersistAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasScene))]
    private async Task UpdateMetaAsync(CancellationToken cancellationToken)
    {
        if (_metaDirty)
        {
            await PersistAsync(cancellationToken);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddStep))]
    private async Task AddStepAsync(CancellationToken cancellationToken)
    {
        if (SelectedStepOption is not { } step)
        {
            return;
        }

        if (AssignedSteps.Any(entry => entry.StepId == step.Id))
        {
            return;
        }

        await PersistStepAssignmentAsync(
            [.. AssignedSteps.Select(entry => entry.StepId), step.Id],
            cancellationToken);
    }

    /// <summary>Снятие чипа этапа: та же операция, что и команда панели.</summary>
    private Task RemoveAssignedStepAsync(SceneStepEntry entry, CancellationToken cancellationToken) =>
        RemoveStepAsync(entry, cancellationToken);

    [RelayCommand]
    private Task MoveStepUpAsync(SceneStepEntry? entry, CancellationToken cancellationToken) =>
        MoveAssignedStepAsync(entry, -1, cancellationToken);

    [RelayCommand]
    private Task MoveStepDownAsync(SceneStepEntry? entry, CancellationToken cancellationToken) =>
        MoveAssignedStepAsync(entry, 1, cancellationToken);

    private Task MoveAssignedStepAsync(SceneStepEntry? entry, int delta, CancellationToken cancellationToken)
    {
        if (entry is null) return Task.CompletedTask;
        var ids = AssignedSteps.Select(step => step.StepId).ToList();
        var index = ids.IndexOf(entry.StepId);
        if (index < 0 || index + delta < 0 || index + delta >= ids.Count) return Task.CompletedTask;
        (ids[index], ids[index + delta]) = (ids[index + delta], ids[index]);
        return PersistStepAssignmentAsync(ids, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasScene))]
    private async Task RemoveStepAsync(SceneStepEntry? entry, CancellationToken cancellationToken)
    {
        if (entry is null)
        {
            return;
        }

        await PersistStepAssignmentAsync(
            [.. AssignedSteps.Where(item => item.StepId != entry.StepId).Select(item => item.StepId)],
            cancellationToken);
    }

    /// <summary>
    /// Добавляет персонажа в сцену: он становится участником шага, привязанного
    /// к сцене. Прямой связи «сцена ↔ персонаж» в модели нет (ADR 017).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddCharacter))]
    private async Task AddCharacterAsync(CancellationToken cancellationToken)
    {
        if (SelectedCharacterOption is not { } character || AssignedSteps.Count == 0)
        {
            return;
        }

        var snapshot = _projects.Current;
        if (snapshot is null)
        {
            return;
        }

        // Персонаж крепится к выбранному шагу, если он привязан к сцене, иначе — к первому.
        var targetStepId = SelectedStepOption is { } selected
            && AssignedSteps.Any(entry => entry.StepId == selected.Id)
            ? selected.Id
            : AssignedSteps[0].StepId;

        var line = snapshot.PlotLines.FirstOrDefault(candidate =>
            candidate.Steps.Any(step => step.Id == targetStepId));
        var step = line?.Steps.FirstOrDefault(candidate => candidate.Id == targetStepId);
        if (line is null || step is null)
        {
            return;
        }

        if (step.Participants.Any(participant => participant.CharacterId == character.Id))
        {
            return;
        }

        var updatedStep = step with
        {
            Participants = [.. step.Participants, new StepParticipantDto { CharacterId = character.Id }],
        };
        var updatedLine = line with
        {
            Steps = [.. line.Steps.Select(candidate => candidate.Id == step.Id ? updatedStep : candidate)],
        };

        var result = await _architecture.SavePlotLineAsync(updatedLine, cancellationToken);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error is null
                ? Loc.T("Не удалось добавить персонажа в сцену.")
                : result.Error.UserMessage;
            return;
        }

        ErrorMessage = null;
        Refresh();
    }

    [RelayCommand]
    private void OpenCharacters() => _navigation.NavigateTo(AppScreen.Characters);

    [RelayCommand]
    private void OpenLines() => _navigation.NavigateTo(AppScreen.Timeline);

    /// <summary>«Показать все» в блоке линий (макет): раскрывает этапы и выбор линии.</summary>
    [RelayCommand]
    private void ToggleLines() => IsLinesExpanded = !IsLinesExpanded;

    /// <summary>«Открыть в планировщике» (макет): экран сюжетных линий.</summary>
    [RelayCommand]
    private void OpenPlanner() => _navigation.NavigateTo(AppScreen.Timeline);

    /// <summary>Сохраняет правки описания и статуса в проект (ТЗ п. 27).</summary>
    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        if (_engine.SceneId is not { } sceneId)
        {
            return;
        }

        await PersistAsync(sceneId, Summary, Status, Notes, cancellationToken);
    }

    private async Task PersistAsync(
        Guid sceneId,
        string summary,
        SceneStatus status,
        string? notes,
        CancellationToken cancellationToken)
    {
        var result = await _projects.UpdateSceneAsync(new UpdateSceneRequest
        {
            SceneId = sceneId,
            Description = summary,
            Status = status,
            Notes = notes,
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error is null
                ? Loc.T("Не удалось сохранить контекст сцены.")
                : result.Error.UserMessage;
            return;
        }

        ErrorMessage = null;
        if (sceneId == _engine.SceneId)
        {
            _metaDirty = false;
        }

        Refresh();
    }

    /// <summary>Записывает полный список этапов, к которым относится сцена (ADR 012).</summary>
    private async Task PersistStepAssignmentAsync(
        IReadOnlyList<Guid> stepIds,
        CancellationToken cancellationToken)
    {
        if (_engine.SceneId is not { } sceneId)
        {
            return;
        }

        var result = await _projects.UpdateSceneAsync(new UpdateSceneRequest
        {
            SceneId = sceneId,
            PlotLineStepIds = stepIds,
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error is null
                ? Loc.T("Не удалось сохранить привязку сцены к сюжетной линии.")
                : result.Error.UserMessage;
            return;
        }

        ErrorMessage = null;
        Refresh();
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

    /// <summary>
    /// При переключении сцены сохраняет описание и заметки предыдущей сцены:
    /// событие приходит после открытия новой сцены, но поля панели ещё содержат
    /// старые значения, поэтому их можно записать до обновления.
    /// </summary>
    private void OnEditorDocumentChanged()
    {
        var currentSceneId = _engine.SceneId;
        if (_loadedSceneId == currentSceneId)
        {
            // Правка текста не меняет описание и заметки сцены. Перечитывать
            // панель здесь нельзя: это затирало бы ещё не сохранённый текст.
            return;
        }

        if (_loadedSceneId is { } previousSceneId && HasScene && _metaDirty)
        {
            _metaDirty = false;
            _ = PersistAsync(previousSceneId, Summary, Status, Notes, CancellationToken.None);
        }

        _loadedSceneId = currentSceneId;
        Refresh();
    }

    private void OnNavigated(object? sender, AppScreen screen)
    {
        if (_isEditorScreen && screen != AppScreen.Editor && HasScene && _metaDirty)
        {
            _ = PersistAsync(CancellationToken.None);
        }

        _isEditorScreen = screen == AppScreen.Editor;
    }

    /// <summary>Перечитывает контекст открытой сцены из снимка проекта.</summary>
    public void Refresh()
    {
        var sceneId = _engine.SceneId;
        var preserveMeta = _metaDirty && _loadedSceneId == sceneId && HasScene;
        var snapshot = _projects.Current;
        var scene = snapshot is null || sceneId is null
            ? null
            : snapshot.Series
                .SelectMany(series => series.Books)
                .SelectMany(book => book.Chapters)
                .SelectMany(chapter => chapter.Scenes)
                .FirstOrDefault(candidate => candidate.Id == sceneId);

        if (scene is null || snapshot is null)
        {
            _metaDirty = false;
            _loadedSceneId = sceneId;
            HasScene = false;
            SceneTitle = string.Empty;
            SceneHeading = string.Empty;
            Breadcrumb = string.Empty;
            _isLoadingMeta = true;
            Summary = string.Empty;
            Notes = null;
            Status = SceneStatus.Idea;
            _isLoadingMeta = false;
            Characters.Clear();
            PlotLines.Clear();
            AssignedSteps.Clear();
            LineOptions.Clear();
            StepOptions.Clear();
            CharacterOptions.Clear();
            NotifyCommands();
            return;
        }

        HasScene = true;
        _loadedSceneId = sceneId;
        SceneTitle = scene.Title;
        if (!preserveMeta)
        {
            _isLoadingMeta = true;
            Summary = scene.Description ?? string.Empty;
            Status = scene.Status;
            Notes = scene.Notes;
            _isLoadingMeta = false;
            _metaDirty = false;
        }

        var chapter = snapshot.Series
            .SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .FirstOrDefault(candidate => candidate.Scenes.Any(item => item.Id == scene.Id));
        SceneHeading = scene.Title;
        Breadcrumb = chapter is null
            ? scene.Title
            : $"{chapter.Title} / {scene.Title}";

        var characters = snapshot.Characters;
        var plotLines = snapshot.PlotLines;
        var touchingLines = plotLines.Where(candidate => TouchesScene(candidate, scene.Id)).ToList();

        var expandedProfiles = Characters.Where(entry => entry.IsProfileExpanded).Select(entry => entry.CharacterId).ToHashSet();
        var expandedCharacters = Characters.Where(entry => entry.IsDescriptionExpanded).Select(entry => entry.CharacterId).ToHashSet();
        Characters.Clear();
        var seenCharacters = new HashSet<Guid>();
        foreach (var line in touchingLines)
        {
            var brush = StoryLabels.BrushFromHex(line.ColorHex, PlotLineFallback);
            foreach (var step in line.Steps.Where(step =>
                line.SceneIds.Contains(scene.Id) || step.SceneIds.Contains(scene.Id)))
            {
                foreach (var participant in step.Participants)
                {
                    if (!seenCharacters.Add(participant.CharacterId))
                    {
                        continue;
                    }

                    var character = characters.FirstOrDefault(candidate => candidate.Id == participant.CharacterId);
                    if (character is null)
                    {
                        continue;
                    }

                    var arcStages = CharacterSceneProfile.ArcStages(character, touchingLines, scene.Id);
                    var arcStage = arcStages.FirstOrDefault();
                    var goal = character.ExternalGoal;
                    Characters.Add(new CharacterEntry(
                        character.Name.Length > 0 ? character.Name[0].ToString() : "?",
                        character.Name,
                        StoryLabels.RoleLabel(character.Role),
                        goal,
                        !string.IsNullOrWhiteSpace(goal),
                        arcStage is null ? Loc.T("Этап арки не выбран") : Loc.F("Этап арки: {0}", arcStage.Title),
                        arcStage?.Description,
                        arcStage is not null,
                        brush,
                        ToggleCharacterDescription)
                    {
                        CharacterId = character.Id,
                        Profile = CharacterSceneProfile.Fields(character),
                        ArcStages = arcStages,
                        IsProfileExpanded = expandedProfiles.Contains(character.Id),
                        IsDescriptionExpanded = expandedCharacters.Contains(character.Id),
                    });
                }
            }
        }

        var previousCharacterId = SelectedCharacterOption?.Id;
        CharacterOptions.Clear();
        foreach (var character in characters.OrderBy(candidate => candidate.Order))
        {
            CharacterOptions.Add(new CharacterOption(character.Id, character.Name));
        }

        SelectedCharacterOption = CharacterOptions.FirstOrDefault(option => option.Id == previousCharacterId);

        PlotLines.Clear();
        foreach (var line in touchingLines)
        {
            PlotLines.Add(new PlotLineEntry(
                line.Name,
                StoryLabels.BrushFromHex(line.ColorHex, PlotLineFallback)));
        }

        RebuildStepAssignment(scene, plotLines);
        NotifyCommands();
    }

    /// <summary>Чипы назначенных этапов и списки выбора линии и этапа (ADR 012).</summary>
    private void RebuildStepAssignment(SceneDto scene, IReadOnlyList<PlotLineDto> plotLines)
    {
        var sceneId = scene.Id;
        var expandedSteps = AssignedSteps.Where(entry => entry.IsDescriptionExpanded).Select(entry => entry.StepId).ToHashSet();
        var previousLineId = SelectedLineOption?.Id;
        var previousStepId = SelectedStepOption?.Id;

        AssignedSteps.Clear();
        LineOptions.Clear();
        foreach (var line in plotLines)
        {
            var assigned = line.Steps
                .Where(step => step.SceneIds.Contains(sceneId))
                .OrderBy(step => step.Order)
                .ToList();

            foreach (var step in assigned)
            {
                AssignedSteps.Add(new SceneStepEntry(
                    step.Id,
                    line.Name,
                    step.Title,
                    line.Description,
                    step.Description,
                    StoryLabels.BrushFromHex(line.ColorHex, PlotLineFallback),
                    RemoveAssignedStepAsync,
                    ToggleLineDescription) { IsDescriptionExpanded = expandedSteps.Contains(step.Id) });
            }

            if (line.Steps.Count > 0)
            {
                LineOptions.Add(new PlotLineOption(line.Id, line.Name));
            }
        }

        var orderedIds = scene.OrderedStepIds(plotLines);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            var entry = AssignedSteps.First(item => item.StepId == orderedIds[index]);
            AssignedSteps.Move(AssignedSteps.IndexOf(entry), index);
            entry.CanMoveUp = index > 0;
            entry.CanMoveDown = index < orderedIds.Count - 1;
        }

        SelectedLineOption = LineOptions.FirstOrDefault(option => option.Id == previousLineId)
            ?? LineOptions.FirstOrDefault();
        RebuildStepOptions(previousStepId);
    }

    partial void OnSelectedLineOptionChanged(PlotLineOption? value) => RebuildStepOptions(null);

    private void RebuildStepOptions(Guid? preferredStepId)
    {
        StepOptions.Clear();
        var snapshot = _projects.Current;
        if (snapshot is null || SelectedLineOption is not { } selected)
        {
            SelectedStepOption = null;
            OnPropertyChanged(nameof(HasStepOptions));
            return;
        }

        var line = snapshot.PlotLines.FirstOrDefault(candidate => candidate.Id == selected.Id);
        if (line is not null)
        {
            var number = 0;
            foreach (var step in line.Steps.OrderBy(step => step.Order))
            {
                number++;
                StepOptions.Add(new StepOption(step.Id, step.Title, number));
            }
        }

        SelectedStepOption = StepOptions.FirstOrDefault(option => option.Id == preferredStepId)
            ?? StepOptions.FirstOrDefault(option => AssignedSteps.All(entry => entry.StepId != option.Id))
            ?? StepOptions.FirstOrDefault();
        OnPropertyChanged(nameof(HasStepOptions));
    }

    private void ToggleLineDescription(SceneStepEntry entry)
    {
        entry.IsDescriptionExpanded = !entry.IsDescriptionExpanded;
    }

    private void ToggleCharacterDescription(CharacterEntry entry)
    {
        entry.IsDescriptionExpanded = !entry.IsDescriptionExpanded;
    }

    public void CollapseDescriptions()
    {
        foreach (var entry in AssignedSteps)
        {
            entry.IsDescriptionExpanded = false;
        }

        foreach (var entry in Characters)
        {
            entry.IsDescriptionExpanded = false;
        }
    }

    private static bool TouchesScene(PlotLineDto line, Guid sceneId)
        => line.SceneIds.Contains(sceneId)
            || line.Steps.Any(step => step.SceneIds.Contains(sceneId));

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(HasScene));
        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(HasAssignedSteps));
        OnPropertyChanged(nameof(HasLineOptions));
        OnPropertyChanged(nameof(HasStepOptions));
        OnPropertyChanged(nameof(CanAddStep));
        OnPropertyChanged(nameof(CanAddCharacter));
        AddCharacterCommand.NotifyCanExecuteChanged();
        SetStatusCommand.NotifyCanExecuteChanged();
        UpdateMetaCommand.NotifyCanExecuteChanged();
        AddStepCommand.NotifyCanExecuteChanged();
        RemoveStepCommand.NotifyCanExecuteChanged();
    }
}
