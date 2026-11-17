namespace ScriSelfStudio.ArchitectureUI.Map;

using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Presentation.Localization;

/// <summary>
/// Сюжетная линия сцены в правой панели карты: цвет, название, статус и этапы,
/// которыми линия связана со сценой. <see cref="StepIds"/> нужны, чтобы снять линию
/// со сцены целиком (ADR 012: связь живёт на стороне сцены).
/// </summary>
public sealed record SceneLineEntry(
    Guid LineId,
    IReadOnlyList<Guid> StepIds,
    string Name,
    IBrush Brush,
    string StatusLabel,
    IBrush StatusBrush)
{
    public string StatusText => $"({StatusLabel})";
    public IAsyncRelayCommand? MoveUpCommand { get; init; }
    public IAsyncRelayCommand? MoveDownCommand { get; init; }

    public string Description { get; init; } = string.Empty;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public IReadOnlyList<SceneStageDescription> Steps { get; init; } = [];
    public bool IsExpanded { get; set; }
}

/// <summary>Описание шага линии или этапа арки, относящегося к выбранной сцене.</summary>
public sealed record SceneStageDescription(string Title, string? Description)
{
    public IAsyncRelayCommand? RemoveCommand { get; init; }
    public bool HasStepActions => RemoveCommand is not null;
    public string DescriptionText => string.IsNullOrWhiteSpace(Description)
        ? Loc.T("Описание этапа не задано.") : Description;
}

/// <summary>Шаг линии, доступный для привязки к сцене с карты (ADR 012).</summary>
public sealed record SceneStepOption(Guid Id, string Title, int Number)
{
    public string Label => $"{Number:00} · {Title}";

    /// <summary>ComboBox показывает элемент через <c>ToString</c>.</summary>
    public override string ToString() => Label;
}

/// <summary>Сюжетная линия проекта, к этапу которой можно отнести сцену (карта, ADR 012).</summary>
public sealed record SceneLineOption(Guid Id, string Name, IReadOnlyList<SceneStepOption> Steps)
{
    /// <summary>ComboBox показывает элемент через <c>ToString</c>.</summary>
    public override string ToString() => Name;
}

/// <summary>Персонаж, которого можно добавить к шагу, уже привязанному к сцене.</summary>
public sealed record SceneCharacterOption(Guid Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Персонаж сцены в правой панели: имя, роль и шаг линии (ТЗ п. 27).</summary>
public sealed record SceneCharacterEntry(
    string Initial,
    string Name,
    string RoleLabel,
    string DetailText,
    bool HasDetail,
    string StepLabel,
    IBrush StepBrush,
    bool HasStep)
{
    public Guid CharacterId { get; init; }
    public IReadOnlyList<SceneStageDescription> Stages { get; init; } = [];
    public bool HasStages => Stages.Count > 0;
    public bool IsExpanded { get; set; }
}

/// <summary>
/// Правая панель карты «Сцена» (ТЗ п. 27): номер и название, хлебные крошки, краткое
/// содержание, ключевые сюжетные линии со статусами, персонажи с этапами и заметки.
/// Данные — проекция снимка проекта; правки отправляет владелец (ArchitectureViewModel).
/// </summary>
public sealed partial class SceneDetailsViewModel : ObservableObject
{
    private readonly Func<string, CancellationToken, Task<bool>> _saveDescription;
    private readonly Func<string, CancellationToken, Task<bool>> _saveNotes;
    private string _savedDescription;
    private string _savedNotes;
    private readonly Func<CancellationToken, Task> _openScene;
    private readonly Func<CancellationToken, Task> _toggleKey;
    private readonly Func<CancellationToken, Task> _toggleLock;
    private readonly Func<Guid, CancellationToken, Task> _addLine;
    private readonly Func<Guid, CancellationToken, Task> _removeLine;
    private readonly Func<Guid, Guid, CancellationToken, Task> _addCharacter;
    private readonly Guid? _characterStepId;

    public SceneDetailsViewModel(
        Guid sceneId,
        string numberLabel,
        string title,
        string breadcrumb,
        string? description,
        string statusLabel,
        string wordsLabel,
        bool isKey,
        bool isLocked,
        string? notes,
        IReadOnlyList<SceneLineEntry> lines,
        IReadOnlyList<SceneCharacterEntry> characters,
        IReadOnlyList<SceneCharacterOption> characterChoices,
        Guid? characterStepId,
        IReadOnlyList<SceneLineOption> lineChoices,
        Func<CancellationToken, Task> openScene,
        Func<CancellationToken, Task> toggleKey,
        Func<CancellationToken, Task> toggleLock,
        Func<Guid, CancellationToken, Task> addLine,
        Func<Guid, CancellationToken, Task> removeLine,
        Func<Guid, Guid, CancellationToken, Task> addCharacter,
        Func<string, CancellationToken, Task<bool>> saveDescription,
        Func<string, CancellationToken, Task<bool>> saveNotes)
    {
        SceneId = sceneId;
        NumberLabel = numberLabel;
        Title = title;
        Breadcrumb = breadcrumb;
        _description = description ?? string.Empty;
        _savedDescription = _description;
        StatusLabel = statusLabel;
        WordsLabel = wordsLabel;
        IsKey = isKey;
        IsLocked = isLocked;
        _notes = notes ?? string.Empty;
        _savedNotes = _notes;
        Lines = lines;
        Characters = characters;
        foreach (var choice in characterChoices)
        {
            CharacterChoices.Add(choice);
        }

        _characterStepId = characterStepId;
        foreach (var choice in lineChoices)
        {
            LineChoices.Add(choice);
        }

        SelectedLineChoice = LineChoices.FirstOrDefault();
        _openScene = openScene;
        _toggleKey = toggleKey;
        _toggleLock = toggleLock;
        _addLine = addLine;
        _removeLine = removeLine;
        _addCharacter = addCharacter;
        _saveDescription = saveDescription;
        _saveNotes = saveNotes;
    }

    public Guid SceneId { get; }

    public string NumberLabel { get; }

    public string Title { get; }

    public string Breadcrumb { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string _description = string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool CanSaveDescription => !string.Equals(Description, _savedDescription, StringComparison.Ordinal);

    /// <summary>Фиксирует оба поля панели и сохраняет более новые черновики после ожидания.</summary>
    public async Task<ScriSelfStudio.Contracts.Errors.Result> CommitPendingAsync(CancellationToken cancellationToken = default)
    {
        var description = Description;
        var notes = Notes;
        if (CanSaveDescription)
        {
            if (!await _saveDescription(description, cancellationToken))
                return ScriSelfStudio.Contracts.Errors.Result.FromError(ScriSelfStudio.Contracts.Errors.ErrorCode.ValidationFailed,
                    "Не удалось сохранить описание сцены. Черновик остался открытым.");
            _savedDescription = description;
            OnPropertyChanged(nameof(CanSaveDescription));
        }
        if (!string.Equals(notes, _savedNotes, StringComparison.Ordinal))
        {
            if (!await _saveNotes(notes, cancellationToken))
                return ScriSelfStudio.Contracts.Errors.Result.FromError(ScriSelfStudio.Contracts.Errors.ErrorCode.ValidationFailed,
                    "Не удалось сохранить заметки сцены. Черновик остался открытым.");
            _savedNotes = notes;
            OnPropertyChanged(nameof(CanSaveNotes));
        }
        return ScriSelfStudio.Contracts.Errors.Result.Success;
    }

    partial void OnDescriptionChanged(string value) =>
        OnPropertyChanged(nameof(CanSaveDescription));

    [RelayCommand]
    private async Task SaveDescriptionAsync(CancellationToken cancellationToken)
    {
        if (!CanSaveDescription)
        {
            return;
        }

        var description = Description;
        if (await _saveDescription(description, cancellationToken))
        {
            _savedDescription = description;
            OnPropertyChanged(nameof(CanSaveDescription));
        }
    }

    public string StatusLabel { get; }

    public string WordsLabel { get; }

    public bool IsKey { get; }

    /// <summary>Сцена заблокирована от правок (ТЗ п. 27).</summary>
    public bool IsLocked { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private string _notes = string.Empty;

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public bool CanSaveNotes => !string.Equals(Notes, _savedNotes, StringComparison.Ordinal);

    partial void OnNotesChanged(string value) =>
        OnPropertyChanged(nameof(CanSaveNotes));

    [RelayCommand]
    private async Task SaveNotesAsync(CancellationToken cancellationToken)
    {
        if (!CanSaveNotes)
        {
            return;
        }

        var notes = Notes;
        if (await _saveNotes(notes, cancellationToken))
        {
            _savedNotes = notes;
            OnPropertyChanged(nameof(CanSaveNotes));
        }
    }

    public IReadOnlyList<SceneLineEntry> Lines { get; }

    public bool HasLines => Lines.Count > 0;

    public IReadOnlyList<SceneCharacterEntry> Characters { get; }

    public bool HasCharacters => Characters.Count > 0;

    public ObservableCollection<SceneCharacterOption> CharacterChoices { get; } = [];

    public bool HasCharacterChoices => CharacterChoices.Count > 0;

    public bool CanAssignCharacters => _characterStepId is not null;

    public bool CanAddCharacter => CanAssignCharacters && SelectedCharacterChoice is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCharacterCommand))]
    private SceneCharacterOption? _selectedCharacterChoice;

    [ObservableProperty]
    private bool _isCharacterPickerOpen;

    /// <summary>Линии проекта, к этапам которых можно отнести эту сцену (карта, ADR 012).</summary>
    public ObservableCollection<SceneLineOption> LineChoices { get; } = [];

    /// <summary>Шаги выбранной линии.</summary>
    public ObservableCollection<SceneStepOption> StepChoices { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLineCommand))]
    private SceneLineOption? _selectedLineChoice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLineCommand))]
    private SceneStepOption? _selectedStepChoice;

    [ObservableProperty]
    private bool _isLinePickerOpen;

    public bool HasLineChoices => LineChoices.Count > 0;

    public bool HasStepChoices => StepChoices.Count > 0;

    public bool CanAddLine => SelectedLineChoice is not null && SelectedStepChoice is not null;

    [RelayCommand]
    private void ToggleLinePicker() => IsLinePickerOpen = !IsLinePickerOpen;

    [RelayCommand]
    private void ToggleCharacterPicker() => IsCharacterPickerOpen = !IsCharacterPickerOpen;

    [RelayCommand(CanExecute = nameof(CanAddCharacter))]
    private Task AddCharacterAsync(CancellationToken cancellationToken) =>
        SelectedCharacterChoice is { } character && _characterStepId is { } stepId
            ? _addCharacter(character.Id, stepId, cancellationToken)
            : Task.CompletedTask;

    partial void OnSelectedLineChoiceChanged(SceneLineOption? value)
    {
        StepChoices.Clear();
        if (value is not null)
        {
            foreach (var step in value.Steps)
            {
                StepChoices.Add(step);
            }
        }

        SelectedStepChoice = StepChoices.FirstOrDefault();
        OnPropertyChanged(nameof(HasStepChoices));
        OnPropertyChanged(nameof(CanAddLine));
    }

    partial void OnSelectedStepChoiceChanged(SceneStepOption? value) =>
        OnPropertyChanged(nameof(CanAddLine));

    /// <summary>Отнести сцену к выбранному этапу линии: линия появится на карте.</summary>
    [RelayCommand(CanExecute = nameof(CanAddLine))]
    private Task AddLineAsync(CancellationToken cancellationToken) =>
        SelectedStepChoice is { } step
            ? _addLine(step.Id, cancellationToken)
            : Task.CompletedTask;

    /// <summary>Снять линию со сцены: убираются все её этапы, привязанные к сцене.</summary>
    [RelayCommand]
    private Task RemoveLineAsync(SceneLineEntry? entry, CancellationToken cancellationToken) =>
        entry is null ? Task.CompletedTask : _removeLine(entry.LineId, cancellationToken);

    [RelayCommand]
    private Task OpenAsync(CancellationToken cancellationToken) => _openScene(cancellationToken);

    [RelayCommand]
    private Task ToggleKeyAsync(CancellationToken cancellationToken) => _toggleKey(cancellationToken);

    [RelayCommand]
    private Task ToggleLockAsync(CancellationToken cancellationToken) => _toggleLock(cancellationToken);
}
