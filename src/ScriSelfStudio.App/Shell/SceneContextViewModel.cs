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
using ScriSelfStudio.StoryUI;

/// <summary>
/// Персонаж сцены в правой панели «Сцена» (ТЗ п. 27): имя, роль, цель и шаг линии,
/// через который персонаж попал в сцену (макет: роль в скобках, «Цель: …», чип этапа).
/// </summary>
public sealed record CharacterEntry(
    string Initial,
    string Name,
    string RoleLabel,
    string? Goal,
    bool HasGoal,
    string? StepLabel,
    bool HasStep,
    IBrush StepBrush);

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
    private readonly Func<SceneStepEntry, CancellationToken, Task> _remove;

    public SceneStepEntry(
        Guid stepId,
        string lineName,
        string stepTitle,
        IBrush brush,
        Func<SceneStepEntry, CancellationToken, Task> remove)
    {
        StepId = stepId;
        LineName = lineName;
        StepTitle = stepTitle;
        Brush = brush;
        _remove = remove;
    }

    public Guid StepId { get; }

    public string LineName { get; }

    public string StepTitle { get; }

    public IBrush Brush { get; }

    public string Label => $"{LineName} · {StepTitle}";

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

    /// <summary>Подписи статусов сцены для четырёх кнопок панели (ТЗ п. 10).</summary>
    public IReadOnlyList<Choice<SceneStatus>> StatusOptions { get; } =
    [
        new(SceneStatus.Idea, "Идея"),
        new(SceneStatus.Draft, "Черновик"),
        new(SceneStatus.Revised, "В работе"),
        new(SceneStatus.Final, "Готово"),
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
        _subscriptions.Add(events.Subscribe<EditorDocumentChanged>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<PlotLineChanged>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<CharacterChanged>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<ProjectOpened>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<ProjectClosed>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<SaveCompleted>(_ => Refresh()));
        _subscriptions.Add(events.Subscribe<ProjectChanged>(_ => Refresh()));
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasScene))]
    private async Task SetStatusAsync(SceneStatus status, CancellationToken cancellationToken)
    {
        Status = status;
        await PersistAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasScene))]
    private async Task UpdateMetaAsync(CancellationToken cancellationToken) => await PersistAsync(cancellationToken);

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
                ? "Не удалось добавить персонажа в сцену."
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

        var result = await _projects.UpdateSceneAsync(new UpdateSceneRequest
        {
            SceneId = sceneId,
            Description = Summary,
            Status = Status,
            Notes = Notes,
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error is null
                ? "Не удалось сохранить контекст сцены."
                : result.Error.UserMessage;
            return;
        }

        ErrorMessage = null;
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
                ? "Не удалось сохранить привязку сцены к сюжетной линии."
                : result.Error.UserMessage;
            return;
        }

        ErrorMessage = null;
        Refresh();
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    /// <summary>Перечитывает контекст открытой сцены из снимка проекта.</summary>
    public void Refresh()
    {
        var sceneId = _engine.SceneId;
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
            HasScene = false;
            SceneTitle = string.Empty;
            SceneHeading = string.Empty;
            Breadcrumb = string.Empty;
            Summary = string.Empty;
            Notes = null;
            Status = SceneStatus.Idea;
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
        SceneTitle = scene.Title;
        Summary = scene.Description ?? string.Empty;
        Status = scene.Status;
        Notes = scene.Notes;

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

                    var goal = character.ExternalGoal;
                    Characters.Add(new CharacterEntry(
                        character.Name.Length > 0 ? character.Name[0].ToString() : "?",
                        character.Name,
                        StoryLabels.RoleLabel(character.Role),
                        goal,
                        HasGoal: !string.IsNullOrWhiteSpace(goal),
                        $"Шаг: {step.Title}",
                        HasStep: true,
                        brush));
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

        RebuildStepAssignment(scene.Id, plotLines);
        NotifyCommands();
    }

    /// <summary>Чипы назначенных этапов и списки выбора линии и этапа (ADR 012).</summary>
    private void RebuildStepAssignment(Guid sceneId, IReadOnlyList<PlotLineDto> plotLines)
    {
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
                    StoryLabels.BrushFromHex(line.ColorHex, PlotLineFallback),
                    RemoveAssignedStepAsync));
            }

            if (line.Steps.Count > 0)
            {
                LineOptions.Add(new PlotLineOption(line.Id, line.Name));
            }
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
