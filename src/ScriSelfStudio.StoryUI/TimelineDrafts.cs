namespace ScriSelfStudio.StoryUI;

using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ScriSelfStudio.Core.Model;

/// <summary>Этап арки, доступный для выбора на шаге линии.</summary>
public sealed record ArcStageChoice(Guid Id, string Title, CharacterArcStageKind Kind)
{
    public override string ToString() => Title;
}

/// <summary>Здесь и далее: сцена проекта. На экране линий сцены только показываются —
/// связь задаётся со стороны сцены (ADR 012).</summary>
public sealed record SceneChoice(Guid Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Строка списка сюжетных линий (макет: пункт левой колонки).</summary>
public sealed record PlotLineListItem(Guid Id, string Name, string? Summary, string? ColorHex, int StepCount)
{
    public IBrush Swatch => StoryLabels.BrushFromHex(ColorHex, StoryLabels.DefaultLineBrush);

    public string Subtitle => string.IsNullOrWhiteSpace(Summary) ? "без описания" : Summary;
}


/// <summary>Значок этапа в ленте (макет: значок в кружке карточки).</summary>
public sealed record StepIconChoice(string Key, string Title, string Glyph)
{
    public override string ToString() => Title;
}

/// <summary>Каталог значков этапа и цветов ленты.</summary>
public static class StepIcons
{
    /// <summary>Порядок значков по умолчанию совпадает с макетом: начало → призыв → поиск → итог.</summary>
    public static IReadOnlyList<StepIconChoice> All { get; } =
    [
        new("start", "Начало", "\uE7C1"),
        new("call", "Призыв", "\uE82D"),
        new("search", "Поиск", "\uE721"),
        new("reveal", "Открытие", "\uE734"),
        new("conflict", "Конфликт", "\uE945"),
        new("crisis", "Кризис", "\uE7BA"),
        new("resolution", "Развязка", "\uE73E"),
        new("custom", "Другой", "\uE8FD"),
    ];

    /// <summary>Значок по ключу; неизвестный ключ даёт значок «Другой».</summary>
    public static StepIconChoice Find(string? key) =>
        All.FirstOrDefault(icon => string.Equals(icon.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? All[^1];

    /// <summary>Значок по номеру шага, если автор его не выбрал (порядок макета).</summary>
    public static StepIconChoice ByNumber(int number) => All[Math.Clamp(number - 1, 0, 3)];
}

/// <summary>
/// Цвета ленты этапов: акцент номера и подложка карточки (макет: красный, золотой,
/// бирюзовый, фиолетовый — по кругу).
/// </summary>
public static class StepColors
{
    private static readonly Color[] Accents =
    [
        Color.Parse("#B3565D"),
        Color.Parse("#B99B66"),
        Color.Parse("#4A899C"),
        Color.Parse("#8161B3"),
    ];

    public static Color Accent(int number) => Accents[Math.Abs(number - 1) % Accents.Length];

    /// <summary>
    /// Подложка карточки — тот же акцент прозрачным слоем поверх полотна.
    /// Плотность подобрана по макету: слой 10% давал заметно более тёмный тон,
    /// чем пастельные подложки макета.
    /// </summary>
    public static IBrush Fill(int number)
    {
        var accent = Accent(number);
        return new SolidColorBrush(Color.FromArgb(0x0B, accent.R, accent.G, accent.B));
    }

    public static IBrush AccentBrush(int number) => new SolidColorBrush(Accent(number));
}

/// <summary>Участник шага: персонаж и этап его арки (ТЗ п. 27).</summary>
public sealed partial class StepParticipantDraft : ObservableObject
{
    private readonly Func<Guid, IReadOnlyList<ArcStageChoice>> _stepsForCharacter;

    public StepParticipantDraft(
        IReadOnlyList<CharacterChoice> characters,
        Func<Guid, IReadOnlyList<ArcStageChoice>> stepsForCharacter)
    {
        Characters = characters;
        _stepsForCharacter = stepsForCharacter;
    }

    public IReadOnlyList<CharacterChoice> Characters { get; }

    [ObservableProperty]
    private CharacterChoice? _character;

    [ObservableProperty]
    private ArcStageChoice? _arcStage;

    [ObservableProperty]
    private IReadOnlyList<ArcStageChoice> _availableSteps = [];

    /// <summary>Заполнить из модели шага: персонаж, затем этап его арки.</summary>
    public void Initialize(Guid characterId, Guid? arcStageId)
    {
        Character = Characters.FirstOrDefault(choice => choice.Id == characterId);
        if (arcStageId is { } stepId)
        {
            ArcStage = AvailableSteps.FirstOrDefault(step => step.Id == stepId);
        }
    }

    partial void OnCharacterChanged(CharacterChoice? value)
    {
        var steps = value is null ? [] : _stepsForCharacter(value.Id);
        AvailableSteps = steps;

        // Смена персонажа делает прежний этап чужим: его нужно сбросить.
        if (ArcStage is not null && !steps.Any(step => step.Id == ArcStage.Id))
        {
            ArcStage = null;
        }
    }
}

/// <summary>Сцена этапа для показа в раскрытой карточке (только чтение, ADR 012).</summary>
public sealed record StepSceneLabel(string Label);

/// <summary>Образец цвета линии в палитре макета.</summary>
public sealed record LineSwatch(string Hex, IBrush Brush)
{
    /// <summary>Выбран ли образец: кольцо вокруг кружка (макет).</summary>
    public bool IsSelected { get; init; }
}

/// <summary>Шаг сюжетной линии: название, участники и привязанные сцены.</summary>
public sealed partial class StepDraft : ObservableObject
{
    public StepDraft(Guid id, string title, string? description, bool isFinal, string? icon)
    {
        Id = id;
        _title = title;
        _description = description;
        _isFinal = isFinal;
        _iconKey = icon;
    }

    public Guid Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string? _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    private bool _isFinal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconGlyph))]
    private string? _iconKey;

    /// <summary>Номер этапа в ленте: 1..N, задаётся экраном, а не моделью.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumberText))]
    [NotifyPropertyChangedFor(nameof(AccentBrush))]
    [NotifyPropertyChangedFor(nameof(FillBrush))]
    [NotifyPropertyChangedFor(nameof(IconGlyph))]
    [NotifyPropertyChangedFor(nameof(ShowConnector))]
    private int _number;

    /// <summary>Карточка раскрыта: показываются участники и сцены этапа.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Выбранная вкладка карточки сохраняется при обновлении черновика.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>Последний этап ленты: у него нет соединительной стрелки вниз.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnector))]
    private bool _isLast;

    public ObservableCollection<StepParticipantDraft> Participants { get; } = [];

    /// <summary>Сцены этапа: только чтение, назначаются на экране сцены (ADR 012).</summary>
    public ObservableCollection<StepSceneLabel> Scenes { get; } = [];

    /// <summary>
    /// Идентификаторы сцен этапа как они лежат в модели. Экран их не меняет, но обязан
    /// вернуть при сохранении: иначе правка названия этапа стёрла бы привязку сцен.
    /// </summary>
    public List<Guid> SceneIds { get; } = [];

    public string Header => IsFinal ? Title + " (финал)" : Title;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>Стрелка вниз между этапами (макет: тонкая линия со стрелкой).</summary>
    public bool ShowConnector => !IsLast;

    /// <summary>Доступные значки этапа (макет: значок в кружке карточки).</summary>
    public static IReadOnlyList<StepIconChoice> IconChoices => StepIcons.All;

    /// <summary>
    /// Выбранный значок. Заполняется без записи в модель, чтобы открытие линии
    /// не превращалось в правку: значение уходит в <see cref="IconKey"/> только
    /// после выбора автором.
    /// </summary>
    [ObservableProperty]
    private StepIconChoice? _selectedIcon;

    private bool _isInitializingIcon;

    public void InitializeIcon(string? key)
    {
        _isInitializingIcon = true;
        SelectedIcon = string.IsNullOrWhiteSpace(key) ? StepIcons.ByNumber(Number) : StepIcons.Find(key);
        _isInitializingIcon = false;
    }

    partial void OnSelectedIconChanged(StepIconChoice? value)
    {
        if (_isInitializingIcon || value is null)
        {
            return;
        }

        IconKey = value.Key;
    }

    public string NumberText => Number.ToString("00");

    public IBrush AccentBrush => StepColors.AccentBrush(Number);

    public IBrush FillBrush => StepColors.Fill(Number);

    /// <summary>Значок: выбранный автором, иначе по номеру шага.</summary>
    public string IconGlyph => string.IsNullOrWhiteSpace(IconKey)
        ? StepIcons.ByNumber(Number).Glyph
        : StepIcons.Find(IconKey).Glyph;

    public bool HasScenes => Scenes.Count > 0;

    public bool HasParticipants => Participants.Count > 0;

    public void NotifyScenesChanged()
    {
        OnPropertyChanged(nameof(HasScenes));
        OnPropertyChanged(nameof(HasParticipants));
    }
}

/// <summary>Сюжетная линия целиком вместе с этапами.</summary>
public sealed partial class PlotLineDraft : ObservableObject
{
    public PlotLineDraft(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }

    public bool IsNew => Id == Guid.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string? _goal;

    [ObservableProperty]
    private PlotLineStatus _status = PlotLineStatus.InProgress;

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private string _colorHex = StoryLabels.DefaultLineColor;

    public ObservableCollection<StepDraft> Steps { get; } = [];
}
