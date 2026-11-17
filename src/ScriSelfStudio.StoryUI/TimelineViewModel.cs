namespace ScriSelfStudio.StoryUI;

using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Localization;

/// <summary>
/// Экран сюжетных линий по макету: список линий, лента шагов и свойства линии.
/// Связь «сцена ↔ шаг линии» задаётся со стороны сцены (ADR 012), поэтому здесь
/// сцены только показываются.
/// </summary>
public sealed partial class TimelineViewModel : ViewModelBase, IDisposable
{
    private readonly IBookArchitectureService _architecture;
    private readonly IProjectService _projects;
    private readonly INavigationService _navigation;
    private readonly IFileDialogService _dialogs;

    private IReadOnlyDictionary<Guid, IReadOnlyList<ArcStageChoice>> _arcStagesByCharacter =
        new Dictionary<Guid, IReadOnlyList<ArcStageChoice>>();

    /// <summary>
    /// Сцены линии на уровне линии — наследие старого формата. Экран их не показывает
    /// и не меняет, но возвращает при сохранении, чтобы не потерять данные автора.
    /// </summary>
    private IReadOnlyList<Guid> _lineSceneIds = [];
    private bool _isTimelineScreen;
    private bool _suppressLineSelection;
    private string? _savedSignature;

    /// <summary>
    /// Номер раскрытого этапа. Идентификатор нового этапа до сохранения пуст, поэтому
    /// после перечитывания раскрытие восстанавливается по позиции.
    /// </summary>
    private int _expandedStepIndex = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(LineBrush))]
    private PlotLineListItem? _selectedLine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    [NotifyPropertyChangedFor(nameof(LineTitle))]
    [NotifyPropertyChangedFor(nameof(LineHeading))]
    [NotifyPropertyChangedFor(nameof(GoalText))]
    [NotifyPropertyChangedFor(nameof(LineBrush))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(StatusDot))]
    [NotifyPropertyChangedFor(nameof(CanPersist))]
    private PlotLineDraft _draft = new(Guid.Empty);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedStep))]
    [NotifyPropertyChangedFor(nameof(SelectedStepHeading))]
    [NotifyPropertyChangedFor(nameof(SelectedStepDescriptionText))]
    [NotifyPropertyChangedFor(nameof(SelectedStepParticipantsText))]
    [NotifyPropertyChangedFor(nameof(SelectedStepScenesText))]
    [NotifyPropertyChangedFor(nameof(SelectedStepFinalText))]
    [NotifyPropertyChangedFor(nameof(SelectedStepNumberText))]
    [NotifyPropertyChangedFor(nameof(SelectedStepHint))]
    private StepDraft? _selectedStep;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    /// <summary>Заголовок линии правится прямо на месте («Изменить» в шапке макета).</summary>
    [ObservableProperty]
    private bool _isHeaderEditing;

    /// <summary>Правая колонка свойств: сворачивается стрелкой в шапке (макет).</summary>
    [ObservableProperty]
    private bool _isPropertiesExpanded = true;

    /// <summary>Статус линии в выпадающем списке: выбранный элемент списка подписей.</summary>
    [ObservableProperty]
    private Choice<PlotLineStatus>? _statusChoice;

    public TimelineViewModel(
        IBookArchitectureService architecture,
        IProjectService projects,
        INavigationService navigation,
        IFileDialogService dialogs)
    {
        _architecture = architecture;
        _projects = projects;
        _navigation = navigation;
        _dialogs = dialogs;
        _navigation.Navigated += OnNavigated;
        _isTimelineScreen = _navigation.Current == AppScreen.Timeline;
        Refresh();
    }

    public ObservableCollection<PlotLineListItem> PlotLines { get; } = [];

    /// <summary>Все сцены проекта: нужны, чтобы показать, где происходит этап.</summary>
    public ObservableCollection<SceneChoice> SceneCatalog { get; } = [];

    public IReadOnlyList<CharacterChoice> CharacterChoices { get; private set; } = [];

    /// <summary>Палитра выбора цвета линии из макета: выбранный образец обведён кольцом.</summary>
    public ObservableCollection<LineSwatch> Swatches { get; } = [];

    /// <summary>Статусы линии для выпадающего списка (макет: «Статус»).</summary>
    public static IReadOnlyList<Choice<PlotLineStatus>> StatusChoices => StoryLabels.LineStatuses;

    public bool HasProject => _projects.Current is not null;

    public string ProjectTitle => _projects.Current?.Name ?? Loc.T("Проект не открыт");

    public bool HasSelection => SelectedLine is not null;

    public bool IsNew => Draft.IsNew;

    public string LineTitle => Draft.IsNew ? Loc.T("Новая линия") : Draft.Name;

    /// <summary>Крупный заголовок в центре: рабочее название линии (макет).</summary>
    public string LineHeading => string.IsNullOrWhiteSpace(Draft.Description) ? LineTitle : Draft.Description!;

    public string GoalText => string.IsNullOrWhiteSpace(Draft.Goal)
        ? Loc.T("Цель не задана")
        : Loc.F("Цель: {0}", Draft.Goal);

    public IBrush LineBrush => StoryLabels.BrushFromHex(Draft.ColorHex, StoryLabels.DefaultLineBrush);

    public string StatusLabel => StoryLabels.LineStatusLabel(Draft.Status);

    public IBrush StatusDot => StoryLabels.StatusDot(Draft.Status);


    public bool HasSelectedStep => SelectedStep is not null;

    public string SelectedStepNumberText => SelectedStep is { } step ? Loc.F("Шаг {0}", step.NumberText) : string.Empty;

    public string SelectedStepHeading => SelectedStep?.Header ?? string.Empty;

    public string SelectedStepDescriptionText =>
        string.IsNullOrWhiteSpace(SelectedStep?.Description)
            ? Loc.T("Описание не задано.")
            : SelectedStep!.Description!;

    public string SelectedStepParticipantsText => SelectedStep is not { } step || step.Participants.Count == 0
        ? Loc.T("Персонажи не назначены.")
        : string.Join("\n", step.Participants.Select(participant =>
            participant.Character is null
                ? Loc.T("персонаж не выбран")
                : participant.ArcStage is null
                    ? Loc.F("{0} · этап не выбран", participant.Character.Name)
                    : $"{participant.Character.Name} · {participant.ArcStage.Title}"));

    public string SelectedStepScenesText => SelectedStep is not { } step || step.Scenes.Count == 0
        ? Loc.T("Сцены не назначены.")
        : string.Join(", ", step.Scenes.Select(scene => scene.Label));

    public string SelectedStepFinalText => SelectedStep?.IsFinal == true
        ? Loc.T("Финальный шаг линии")
        : Loc.T("Не финальный шаг");

    public string SelectedStepHint =>
        "Сцены шага назначаются на экране сцены: связь задаётся со стороны сцены.";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool HasPlotLines => PlotLines.Count > 0;

    public bool HasScenes => SceneCatalog.Count > 0;

    /// <summary>Сохранять можно, когда у линии есть название.</summary>
    public bool CanPersist => !string.IsNullOrWhiteSpace(Draft.Name);

    public string StepHint => Draft.Steps.Count == 0
        ? Loc.T("У линии нет шагов. Добавьте первый шаг.")
        : Loc.T("Выберите шаг, чтобы раскрыть его и назначить персонажей.");

    /// <summary>Есть ли у линии шаги: подсказка в ленте показывается только при пустой ленте.</summary>
    public bool HasSteps => Draft.Steps.Count > 0;

    /// <summary>Перечитать каталоги и линии из сервиса, сохранив выбор.</summary>
    public void Refresh() => RefreshTo(null);

    /// <summary>
    /// Перечитать данные и выделить линию: только что сохранённую или прежнюю.
    /// Без этого созданная линия не выделялась и редактор показывал пустую форму.
    /// </summary>
    private void RefreshTo(Guid? preferredLineId)
    {
        var selectedId = preferredLineId ?? SelectedLine?.Id;
        var selectedStepId = SelectedStep?.Id;
        RebuildCatalogs();

        PlotLines.Clear();
        foreach (var line in _architecture.GetPlotLines())
        {
            PlotLines.Add(new PlotLineListItem(line.Id, line.Name, line.Description, line.ColorHex, line.Steps.Count));
        }

        if (selectedId is { } id && PlotLines.FirstOrDefault(item => item.Id == id) is { } restored)
        {
            // Перечитывание списка не должно приниматься за выбор пользователя:
            // иначе черновик сохранялся бы повторно при каждом Refresh.
            _suppressLineSelection = true;
            try
            {
                SelectedLine = restored;
            }
            finally
            {
                _suppressLineSelection = false;
            }

            LoadDraft(restored.Id, selectedStepId, preserveStepState: true);
        }
        else
        {
            SelectedLine = null;
            Draft = new PlotLineDraft(Guid.Empty);
            SelectedStep = null;
            _savedSignature = Signature(BuildDto());
        }

        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ProjectTitle));
        OnPropertyChanged(nameof(HasPlotLines));
        OnPropertyChanged(nameof(HasScenes));
        OnPropertyChanged(nameof(StepHint));
        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(CanPersist));
        NotifyCommands();
    }

    partial void OnSelectedLineChanged(PlotLineListItem? value)
    {
        if (value is null || _suppressLineSelection)
        {
            return;
        }

        if (value.Id != Draft.Id)
        {
            // Правки прежней линии фиксируем до подмены черновика.
            _ = FlushDraftAsync();
        }

        ClearMessages();
        LoadDraft(value.Id, null);
    }

    /// <summary>Раскрыть этап по клику; повторный клик сворачивает и снимает выбор.</summary>
    [RelayCommand]
    private void ToggleStep(StepDraft? step)
    {
        if (step is null)
        {
            return;
        }

        if (step.IsExpanded)
        {
            step.IsExpanded = false;
            _expandedStepIndex = -1;
            if (ReferenceEquals(SelectedStep, step))
            {
                SelectedStep = null;
            }

            return;
        }

        foreach (var other in Draft.Steps)
        {
            other.IsExpanded = false;
        }

        step.IsExpanded = true;
        _expandedStepIndex = Draft.Steps.IndexOf(step);
        SelectedStep = step;
    }

    [RelayCommand]
    private async Task NewLineAsync(CancellationToken cancellationToken)
    {
        if (!(await FlushDraftAsync(cancellationToken)).IsSuccess) return;
        ClearMessages();
        IsHeaderEditing = false;
        SelectedLine = null;

        // Цвет новой линии берётся из палитры по кругу: в макете линии книги
        // различаются цветом точки, и автору не приходится выбирать его вручную.
        Draft = new PlotLineDraft(Guid.Empty)
        {
            ColorHex = StoryLabels.LineSwatches[PlotLines.Count % StoryLabels.LineSwatches.Count],
        };
        _lineSceneIds = [];
        _savedSignature = Signature(BuildDto());
        RebuildSwatches();
        SelectedStep = null;
        NotifyCommands();
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteLineAsync(CancellationToken cancellationToken)
    {
        if (SelectedLine is not { } line)
        {
            return;
        }

        await DeleteLineByIdAsync(line.Id, cancellationToken);
    }

    [RelayCommand]
    private Task MoveLineUpAsync(Guid lineId, CancellationToken cancellationToken) =>
        MoveLineAsync(lineId, -1, cancellationToken);

    [RelayCommand]
    private Task MoveLineDownAsync(Guid lineId, CancellationToken cancellationToken) =>
        MoveLineAsync(lineId, 1, cancellationToken);

    private async Task MoveLineAsync(Guid lineId, int delta, CancellationToken cancellationToken)
    {
        var lines = _architecture.GetPlotLines().OrderBy(line => line.Order).ToList();
        var index = lines.FindIndex(line => line.Id == lineId);
        if (index < 0 || index + delta < 0 || index + delta >= lines.Count) return;
        await FlushDraftAsync();
        var result = await _architecture.MoveEntityAsync(new MoveEntityRequest
        {
            Kind = EntityKind.PlotLine, Id = lineId, NewOrder = index + delta,
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            await ReportAsync(result.Error!);
            return;
        }
        RefreshTo(lineId);
    }

    /// <summary>Удаление строки списка через «…»: подтверждение и удаление (макет).</summary>
    [RelayCommand]
    private async Task DeleteLineByIdAsync(Guid lineId, CancellationToken cancellationToken)
    {
        var name = PlotLines.FirstOrDefault(item => item.Id == lineId)?.Name ?? Loc.T("линию");
        if (!await _dialogs.ConfirmAsync(
                "Удалить линию",
                Loc.F("Удалить «{0}»? Действие нельзя отменить.", name),
                "Удалить",
                Loc.T("Отмена")))
        {
            return;
        }

        var result = await _architecture.DeletePlotLineAsync(lineId, cancellationToken);
        if (!result.IsSuccess)
        {
            await ReportAsync(result.Error!);
            return;
        }

        ClearMessages();
        StatusMessage = Loc.F("Линия «{0}» удалена.", name);
        if (SelectedLine?.Id == lineId)
        {
            SelectedLine = null;
            Draft = new PlotLineDraft(Guid.Empty);
            SelectedStep = null;
        }

        Refresh();
    }

    /// <summary>Сохранить линию целиком: имя, цель, статус, заметки, цвет и этапы.</summary>
    [RelayCommand]
    private async Task SaveLineAsync(CancellationToken cancellationToken)
    {
        if (!CanPersist)
        {
            ErrorMessage = Loc.T("У линии должно быть название.");
            return;
        }

        var savedName = Draft.Name.Trim();
        var wasNew = Draft.IsNew;
        var result = await _architecture.SavePlotLineAsync(BuildDto(), cancellationToken);
        if (!result.IsSuccess)
        {
            await ReportAsync(result.Error!);
            return;
        }

        var savedId = result.Value?.PlotLines
            .FirstOrDefault(line => string.Equals(line.Name, savedName, StringComparison.Ordinal))
            ?.Id;

        ErrorMessage = null;
        StatusMessage = wasNew ? Loc.F("Линия «{0}» создана.", savedName) : null;
        IsHeaderEditing = false;
        RefreshTo(savedId);
    }

    /// <summary>Сохранить правки текстовых полей линии (потеря фокуса поля).</summary>
    [RelayCommand]
    private async Task CommitLineAsync(CancellationToken cancellationToken)
    {
        // Новую линию ещё нельзя выделить: она существует только черновиком, поэтому
        // проверяем не выбор, а готовность черновика — иначе создание линии терялось.
        if (!HasUnsavedDraft)
        {
            if (CanPersist)
            {
                IsHeaderEditing = false;
            }

            return;
        }

        await SaveLineAsync(cancellationToken);
    }

    [RelayCommand]
    private void AddStep()
    {
        // Название нового этапа берётся у значка по номеру: в макете лента идёт
        // «Начало → Призыв → Поиск → …», и автору не приходится переименовывать.
        var number = Draft.Steps.Count + 1;
        var step = new StepDraft(Guid.Empty, StepIcons.ByNumber(number).Title, null, isFinal: false, icon: null)
        {
            Number = number,
        };
        Draft.Steps.Add(step);
        RenumberSteps();
        SelectedStep = step;
        step.IsExpanded = true;
        _expandedStepIndex = Draft.Steps.Count - 1;
    }

    [RelayCommand]
    private async Task RemoveStepAsync(StepDraft? step, CancellationToken cancellationToken)
    {
        if (step is null)
        {
            return;
        }

        Draft.Steps.Remove(step);
        _expandedStepIndex = -1;
        if (ReferenceEquals(SelectedStep, step))
        {
            SelectedStep = null;
        }

        RenumberSteps();
        await SaveIfPossibleAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task MoveStepUpAsync(StepDraft? step, CancellationToken cancellationToken) =>
        await MoveStepAsync(step, -1, cancellationToken);

    [RelayCommand]
    private async Task MoveStepDownAsync(StepDraft? step, CancellationToken cancellationToken) =>
        await MoveStepAsync(step, 1, cancellationToken);

    /// <summary>Сделать этап финальным или снять признак (пункт меню «…»).</summary>
    [RelayCommand]
    private async Task ToggleFinalAsync(StepDraft? step, CancellationToken cancellationToken)
    {
        if (step is null)
        {
            return;
        }

        step.IsFinal = !step.IsFinal;
        await SaveIfPossibleAsync(cancellationToken);
    }

    [RelayCommand]
    private void AddParticipant()
    {
        if (SelectedStep is not { } step)
        {
            return;
        }

        var participant = new StepParticipantDraft(CharacterChoices, StepsForCharacter);
        participant.PropertyChanged += OnParticipantChanged;
        step.Participants.Add(participant);
        step.NotifyScenesChanged();
    }

    [RelayCommand]
    private void RemoveParticipant(StepParticipantDraft? participant)
    {
        if (participant is null || SelectedStep is not { } step)
        {
            return;
        }

        participant.PropertyChanged -= OnParticipantChanged;
        step.Participants.Remove(participant);
        step.NotifyScenesChanged();
        // Участник исчез из карточки сцены: фиксируем сразу, как статус и цвет.
        _ = SaveIfPossibleAsync(CancellationToken.None);
    }

    /// <summary>
    /// Выбор персонажа или этапа арки — дискретное действие, поэтому линия сохраняется
    /// сразу: иначе добавленный в линию персонаж не доходил до карты книги.
    /// </summary>
    private void OnParticipantChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not StepParticipantDraft participant || participant.Character is null)
        {
            return;
        }

        if (e.PropertyName is not (nameof(StepParticipantDraft.Character) or nameof(StepParticipantDraft.ArcStage)))
        {
            return;
        }

        _ = SaveIfPossibleAsync(CancellationToken.None);
    }

    /// <summary>Смена статуса линии сохраняется сразу: это дискретное действие (макет).</summary>
    [RelayCommand]
    private async Task SetStatusAsync(PlotLineStatus status, CancellationToken cancellationToken)
    {
        Draft.Status = status;
        StatusChoice = StoryLabels.LineStatuses.FirstOrDefault(choice => choice.Value == status);
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusDot));
        await SaveIfPossibleAsync(cancellationToken);
    }

    /// <summary>Выбор цвета линии образцом из палитры макета.</summary>
    [RelayCommand]
    private async Task SetColorAsync(string? hex, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return;
        }

        Draft.ColorHex = hex;
        OnPropertyChanged(nameof(LineBrush));
        RebuildSwatches();
        await SaveIfPossibleAsync(cancellationToken);
    }

    [RelayCommand]
    private void ToggleHeaderEditing() => IsHeaderEditing = !IsHeaderEditing;

    [RelayCommand]
    private void ToggleProperties() => IsPropertiesExpanded = !IsPropertiesExpanded;

    /// <summary>Выбор статуса из списка: сохраняется сразу, как и цвет.</summary>
    partial void OnStatusChoiceChanged(Choice<PlotLineStatus>? value)
    {
        if (value is null || value.Value == Draft.Status)
        {
            return;
        }

        _ = SetStatusAsync(value.Value, CancellationToken.None);
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    private async Task MoveStepAsync(StepDraft? step, int delta, CancellationToken cancellationToken)
    {
        if (step is null)
        {
            return;
        }

        var index = Draft.Steps.IndexOf(step);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Draft.Steps.Count)
        {
            return;
        }

        Draft.Steps.Move(index, target);
        RenumberSteps();
        await SaveIfPossibleAsync(cancellationToken);
    }

    /// <summary>Номера этапов задаёт экран: они определяют цвет и значок по умолчанию.</summary>
    private void RenumberSteps()
    {
        for (var index = 0; index < Draft.Steps.Count; index++)
        {
            Draft.Steps[index].Number = index + 1;
            Draft.Steps[index].IsLast = index == Draft.Steps.Count - 1;
        }

        OnPropertyChanged(nameof(StepHint));
        OnPropertyChanged(nameof(HasSteps));
    }

    /// <summary>Есть ли правки, которые ещё не записаны в проект.</summary>
    public bool HasUnsavedDraft => HasProject && Signature(BuildDto()) != _savedSignature;

    /// <summary>
    /// Фоновая фиксация линии без перечитывания выбранной строки: так клик по
    /// другой линии не теряет текст и не перетягивает выбор обратно.
    /// </summary>
    public async Task<Result> FlushDraftAsync(CancellationToken cancellationToken = default)
    {
        if (!HasUnsavedDraft) return Result.Success;
        if (!CanPersist)
        {
            return Result.FromError(ErrorCode.ValidationFailed, "Укажите название сюжетной линии, чтобы сохранить черновик.");
        }

        var draft = Draft;
        var dto = BuildDto();
        if (dto.Id != Guid.Empty && Signature(dto) == _savedSignature)
        {
            return Result.Success;
        }

        var result = await _architecture.SavePlotLineAsync(dto, cancellationToken);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.SuggestedAction is null
                ? result.Error.UserMessage
                : $"{result.Error.UserMessage} {result.Error.SuggestedAction}";
            return Result.Failure(result.Error!);
        }

        var saved = result.Value?.PlotLines.FirstOrDefault(line =>
            dto.Id == Guid.Empty
                ? string.Equals(line.Name, dto.Name, StringComparison.Ordinal)
                : line.Id == dto.Id);
        if (saved is null)
        {
            return Result.FromError(ErrorCode.EntityNotFound, "Сохранённая сюжетная линия не найдена. Черновик оставлен открытым.");
        }

        UpdateListItem(saved);
        if (ReferenceEquals(Draft, draft))
        {
            if (Signature(BuildDto()) == Signature(dto))
            {
                // LoadDraft сохраняет подпись UI-снимка вместе с новыми ID этапов.
                LoadDraft(saved.Id, SelectedStep?.Id, preserveStepState: true);
            }
            else
            {
                _savedSignature = Signature(saved);
            }
        }
        ErrorMessage = null;
        return Result.Success;
    }

    /// <summary>
    /// Обновляет строку списка, не трогая выбранную линию: фоновое сохранение
    /// не должно менять выбор пользователя.
    /// </summary>
    private void UpdateListItem(PlotLineDto saved)
    {
        var item = new PlotLineListItem(
            saved.Id,
            saved.Name,
            saved.Description,
            saved.ColorHex,
            saved.Steps.Count);
        for (var index = 0; index < PlotLines.Count; index++)
        {
            if (PlotLines[index].Id != saved.Id)
            {
                continue;
            }

            if (!ReferenceEquals(PlotLines[index], SelectedLine))
            {
                PlotLines[index] = item;
            }

            return;
        }

        if (SelectedLine?.Id != saved.Id)
        {
            PlotLines.Add(item);
            OnPropertyChanged(nameof(HasPlotLines));
        }
    }

    private async Task SaveIfPossibleAsync(CancellationToken cancellationToken)
    {
        if (!HasSelection || !CanPersist)
        {
            return;
        }

        await SaveLineAsync(cancellationToken);
    }

    private void OnNavigated(object? sender, AppScreen screen)
    {
        if (_isTimelineScreen && screen != AppScreen.Timeline)
        {
            _ = CommitLineCommand.ExecuteAsync(null);
        }

        _isTimelineScreen = screen == AppScreen.Timeline;
        if (screen == AppScreen.Timeline)
        {
            Refresh();
        }
    }

    private void RebuildCatalogs()
    {
        CharacterChoices = _architecture.GetCharacters()
            .Select(character => new CharacterChoice(character.Id, character.Name))
            .ToList();

        _arcStagesByCharacter = _architecture.GetCharacters().ToDictionary(
            character => character.Id,
            character => (IReadOnlyList<ArcStageChoice>)
            [
                .. character.ArcStages.Select(step => new ArcStageChoice(step.Id, step.Title, step.Kind)),
            ]);

        SceneCatalog.Clear();
        foreach (var scene in EnumerateScenes())
        {
            SceneCatalog.Add(new SceneChoice(scene.Id, scene.Label));
        }

        OnPropertyChanged(nameof(CharacterChoices));
        OnPropertyChanged(nameof(HasScenes));
    }

    private IEnumerable<(Guid Id, string Label)> EnumerateScenes()
    {
        var snapshot = _projects.Current;
        if (snapshot is null)
        {
            yield break;
        }

        foreach (var series in snapshot.Series)
        {
            foreach (var book in series.Books)
            {
                foreach (var chapter in book.Chapters)
                {
                    foreach (var scene in chapter.Scenes)
                    {
                        yield return (scene.Id, $"{book.Title} · {chapter.Title} · {scene.Title}");
                    }
                }
            }
        }
    }

    private void LoadDraft(Guid lineId, Guid? preferredStepId, bool preserveStepState = false)
    {
        var line = _architecture.GetPlotLines().FirstOrDefault(item => item.Id == lineId);
        if (line is null)
        {
            Draft = new PlotLineDraft(Guid.Empty);
            SelectedStep = null;
            return;
        }

        var previousSteps = preserveStepState ? Draft.Steps.ToArray() : [];
        _lineSceneIds = [.. line.SceneIds];
        var draft = new PlotLineDraft(line.Id)
        {
            Name = line.Name,
            Description = line.Description,
            Goal = line.Goal,
            Status = line.Status,
            Notes = line.Notes,
            ColorHex = line.ColorHex ?? StoryLabels.DefaultLineColor,
        };

        var number = 0;
        foreach (var step in line.Steps.OrderBy(step => step.Order))
        {
            number++;
            var previousStep = previousSteps.FirstOrDefault(item => item.Id == step.Id);
            if (previousStep is null && number <= previousSteps.Length && previousSteps[number - 1].Id == Guid.Empty)
            {
                previousStep = previousSteps[number - 1];
            }

            var stepDraft = new StepDraft(step.Id, step.Title, step.Description, step.IsFinal, step.Icon)
            {
                Number = number,
                SelectedTabIndex = previousStep?.SelectedTabIndex ?? 0,
            };
            stepDraft.InitializeIcon(step.Icon);

            FillStepScenes(stepDraft, step.SceneIds);
            foreach (var participant in step.Participants)
            {
                stepDraft.Participants.Add(CreateParticipant(participant.CharacterId, participant.CharacterArcStageId));
            }

            stepDraft.NotifyScenesChanged();
            draft.Steps.Add(stepDraft);
        }

        if (draft.Steps.Count > 0)
        {
            draft.Steps[^1].IsLast = true;
        }

        Draft = draft;
        _savedSignature = Signature(BuildDto());
        StatusChoice = StoryLabels.LineStatuses.FirstOrDefault(choice => choice.Value == draft.Status);
        RebuildSwatches();

        // Раскрытый этап восстанавливается по идентификатору, а для только что
        // созданного этапа — по позиции: до сохранения идентификатора ещё нет.
        var restored = preferredStepId is { } stepId
            ? draft.Steps.FirstOrDefault(step => step.Id == stepId)
            : null;
        if (restored is null && _expandedStepIndex >= 0 && _expandedStepIndex < draft.Steps.Count)
        {
            restored = draft.Steps[_expandedStepIndex];
        }

        if (restored is not null)
        {
            restored.IsExpanded = true;
        }

        SelectedStep = restored;
        OnPropertyChanged(nameof(StepHint));
        OnPropertyChanged(nameof(HasSteps));
    }

    /// <summary>Образцы цвета: выбранный помечен, чтобы нарисовать кольцо (макет).</summary>
    private void RebuildSwatches()
    {
        Swatches.Clear();
        foreach (var hex in StoryLabels.LineSwatches)
        {
            var isSelected = string.Equals(hex, Draft.ColorHex, StringComparison.OrdinalIgnoreCase);
            Swatches.Add(new LineSwatch(hex, StoryLabels.BrushFromHex(hex, StoryLabels.DefaultLineBrush))
            {
                IsSelected = isSelected,
            });
        }
    }

    private StepParticipantDraft CreateParticipant(Guid characterId, Guid? arcStageId)
    {
        var participant = new StepParticipantDraft(CharacterChoices, StepsForCharacter);
        participant.Initialize(characterId, arcStageId);
        participant.PropertyChanged += OnParticipantChanged;
        return participant;
    }

    private IReadOnlyList<ArcStageChoice> StepsForCharacter(Guid characterId) =>
        _arcStagesByCharacter.TryGetValue(characterId, out var steps) ? steps : [];

    /// <summary>Сцены этапа показываются по идентификаторам из модели (ADR 012).</summary>
    private void FillStepScenes(StepDraft step, IReadOnlyList<Guid> sceneIds)
    {
        step.Scenes.Clear();
        step.SceneIds.Clear();
        step.SceneIds.AddRange(sceneIds);
        var byId = SceneCatalog.ToDictionary(scene => scene.Id, scene => scene.Label);
        foreach (var sceneId in sceneIds)
        {
            if (byId.TryGetValue(sceneId, out var label))
            {
                step.Scenes.Add(new StepSceneLabel(label));
            }
        }
    }

    private PlotLineDto BuildDto() => new()
    {
        Id = Draft.Id,
        Name = Draft.Name.Trim(),
        Description = string.IsNullOrWhiteSpace(Draft.Description) ? null : Draft.Description.Trim(),
        Goal = string.IsNullOrWhiteSpace(Draft.Goal) ? null : Draft.Goal.Trim(),
        Status = Draft.Status,
        Notes = string.IsNullOrWhiteSpace(Draft.Notes) ? null : Draft.Notes.Trim(),
        ColorHex = string.IsNullOrWhiteSpace(Draft.ColorHex) ? StoryLabels.DefaultLineColor : Draft.ColorHex.Trim(),
        SceneIds = [.. _lineSceneIds],
        Steps =
        [
            .. Draft.Steps.Select(step => new PlotLineStepDto
            {
                Id = step.Id,
                Title = string.IsNullOrWhiteSpace(step.Title) ? Loc.T("Шаг") : step.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(step.Description) ? null : step.Description.Trim(),
                IsFinal = step.IsFinal,
                Icon = step.IconKey,
                SceneIds = [.. step.SceneIds],
                Participants =
                [
                    .. step.Participants
                        .Where(participant => participant.Character is not null)
                        .Select(participant => new StepParticipantDto
                        {
                            CharacterId = participant.Character!.Id,
                            CharacterArcStageId = participant.ArcStage?.Id,
                        }),
                ],
            }),
        ],
    };

    private static string Signature(PlotLineDto line) =>
        System.Text.Json.JsonSerializer.Serialize(line);

    private async Task ReportAsync(AppError error)
    {
        ErrorMessage = error.SuggestedAction is null
            ? Loc.T(error.UserMessage)
            : $"{Loc.T(error.UserMessage)} {Loc.T(error.SuggestedAction)}";
        await _dialogs.ShowErrorAsync(Loc.T("Ошибка сюжетной линии"), ErrorMessage);
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        StatusMessage = null;
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(LineTitle));
        OnPropertyChanged(nameof(LineHeading));
        OnPropertyChanged(nameof(GoalText));
        OnPropertyChanged(nameof(LineBrush));
        OnPropertyChanged(nameof(HasSelectedStep));
        OnPropertyChanged(nameof(CanPersist));
        DeleteLineCommand.NotifyCanExecuteChanged();
        AddParticipantCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _navigation.Navigated -= OnNavigated;
    }
}
