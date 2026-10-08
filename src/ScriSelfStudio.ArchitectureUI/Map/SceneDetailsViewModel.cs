namespace ScriSelfStudio.ArchitectureUI.Map;

using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    IBrush StatusBrush);

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

/// <summary>Персонаж сцены в правой панели: имя, роль и шаг линии (ТЗ п. 27).</summary>
public sealed record SceneCharacterEntry(
    string Initial,
    string Name,
    string RoleLabel,
    string StepLabel,
    IBrush StepBrush,
    bool HasStep);

/// <summary>
/// Правая панель карты «Сцена» (ТЗ п. 27): номер и название, хлебные крошки, краткое
/// содержание, ключевые сюжетные линии со статусами, персонажи с этапами и заметки.
/// Данные — проекция снимка проекта; правки отправляет владелец (ArchitectureViewModel).
/// </summary>
public sealed partial class SceneDetailsViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task> _openScene;
    private readonly Func<CancellationToken, Task> _toggleKey;
    private readonly Func<CancellationToken, Task> _toggleLock;
    private readonly Func<Guid, CancellationToken, Task> _addLine;
    private readonly Func<Guid, CancellationToken, Task> _removeLine;

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
        IReadOnlyList<SceneLineOption> lineChoices,
        Func<CancellationToken, Task> openScene,
        Func<CancellationToken, Task> toggleKey,
        Func<CancellationToken, Task> toggleLock,
        Func<Guid, CancellationToken, Task> addLine,
        Func<Guid, CancellationToken, Task> removeLine)
    {
        SceneId = sceneId;
        NumberLabel = numberLabel;
        Title = title;
        Breadcrumb = breadcrumb;
        Description = description ?? string.Empty;
        StatusLabel = statusLabel;
        WordsLabel = wordsLabel;
        IsKey = isKey;
        IsLocked = isLocked;
        Notes = notes;
        Lines = lines;
        Characters = characters;
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
    }

    public Guid SceneId { get; }

    public string NumberLabel { get; }

    public string Title { get; }

    public string Breadcrumb { get; }

    public string Description { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public string StatusLabel { get; }

    public string WordsLabel { get; }

    public bool IsKey { get; }

    /// <summary>Сцена заблокирована от правок (ТЗ п. 27).</summary>
    public bool IsLocked { get; }

    public string? Notes { get; }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public IReadOnlyList<SceneLineEntry> Lines { get; }

    public bool HasLines => Lines.Count > 0;

    public IReadOnlyList<SceneCharacterEntry> Characters { get; }

    public bool HasCharacters => Characters.Count > 0;

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

    public bool HasLineChoices => LineChoices.Count > 0;

    public bool HasStepChoices => StepChoices.Count > 0;

    public bool CanAddLine => SelectedLineChoice is not null && SelectedStepChoice is not null;

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
