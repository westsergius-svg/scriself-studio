namespace ScriSelfStudio.ArchitectureUI.Map;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ScriSelfStudio.BookArchitecture.Map;
using ScriSelfStudio.Core.Model;

/// <summary>
/// Отметка сюжетной линии на карте: цвет и название для подсказки (ТЗ п. 27).
/// <see cref="IsClosed"/> — линия завершена: в легенде макета помечается замком.
/// </summary>
public sealed record PlotLineMarker(Guid Id, string Name, IBrush Brush, bool IsClosed = false);

/// <summary>
/// Ячейка карты книги — сцена главы (ТЗ п. 27). Ячейка показывает номер, замысел,
/// статус, объём и отметки линий, но не её текст: карта работает с проекцией.
/// </summary>
public sealed class BookMapSceneViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isDimmed;

    public BookMapSceneViewModel(
        ArchitectureNodeViewModel scene,
        string number,
        ICommand selectCommand,
        IReadOnlyList<PlotLineMarker> lines)
    {
        Node = scene;
        Number = number;
        SelectCommand = selectCommand;
        SetLines(lines);
    }

    public ArchitectureNodeViewModel Node { get; }

    /// <summary>Номер сцены на карте: «4.2» (глава 4, сцена 2).</summary>
    public string Number { get; }

    /// <summary>Сюжетные линии, проходящие через эту сцену (ТЗ п. 27).</summary>
    public ObservableCollection<PlotLineMarker> Lines { get; } = [];

    public bool HasLines => Lines.Count > 0;

    public Guid Id => Node.Id;

    public string Title => Node.Title;

    public string Description => string.IsNullOrWhiteSpace(Node.Description)
        ? "Замысел не описан"
        : Node.Description;

    public string StatusText => Node.StatusText;

    public string WordsLabel => Node.WordsLabel;

    public bool IsKey => Node.IsKey;

    /// <summary>Сцена заблокирована от правок: карточка показывает замок (ТЗ п. 27).</summary>
    public bool IsLocked => Node.IsLocked;

    public ICommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Сцена не проходит выбранную в фильтре линию — карточка приглушается.</summary>
    public bool IsDimmed
    {
        get => _isDimmed;
        set => SetProperty(ref _isDimmed, value);
    }

    /// <summary>Переназначить отметки линий при фильтре «Все линии / конкретная» (ТЗ п. 27).</summary>
    public void SetLines(IReadOnlyList<PlotLineMarker> lines)
    {
        Lines.Clear();
        foreach (var line in lines)
        {
            Lines.Add(line);
        }

        OnPropertyChanged(nameof(HasLines));
    }
}

/// <summary>
/// Столбец карты книги — глава (ТЗ п. 27). Ширина одинакова у всех столбцов,
/// позиция зависит только от индекса, поэтому выбор ничего не сдвигает.
/// </summary>
public sealed partial class BookMapColumnViewModel : ObservableObject
{
    private static readonly string[] Palette =
    [
        "#4A7DFF", "#2FA96B", "#E0A82E", "#B06CE0", "#E0563E", "#2FA9A0",
    ];

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private bool _isActive;

    public BookMapColumnViewModel(
        ArchitectureNodeViewModel chapter,
        int number,
        int index,
        ICommand selectCommand,
        ICommand addSceneCommand,
        IReadOnlyDictionary<Guid, IReadOnlyList<PlotLineMarker>> linesByScene)
    {
        Node = chapter;
        Number = number;
        AccentColor = Palette[index % Palette.Length];
        SelectCommand = selectCommand;
        AddSceneCommand = addSceneCommand;

        var sceneNumber = 0;
        foreach (var scene in chapter.Children)
        {
            sceneNumber++;
            Scenes.Add(new BookMapSceneViewModel(
                scene,
                $"{number}.{sceneNumber}",
                selectCommand,
                linesByScene.TryGetValue(scene.Id, out var lines) ? lines : []));
        }
    }

    public ArchitectureNodeViewModel Node { get; }

    public int Number { get; }

    public string Short => $"Гл. {Number}";

    public string Title => Node.Title;

    /// <summary>Заголовок столбца как в макете: «1. Пролог».</summary>
    public string HeaderLabel => $"{Number}. {Title}";

    public string Description => string.IsNullOrWhiteSpace(Node.Description)
        ? "Описание главы не задано"
        : Node.Description;

    public string ScenesLabel => Scenes.Count == 0
        ? "сцен нет"
        : $"{Scenes.Count} {StoryText.Plural(Scenes.Count, "сцена", "сцены", "сцен")}";

    public string WordsLabel => Node.WordsLabel;

    public string KeyLabel
    {
        get
        {
            var keys = Scenes.Count(scene => scene.IsKey);
            return keys > 0 ? $"ключевых: {keys}" : "ключевых нет";
        }
    }

    public string AccentColor { get; }

    public ICommand SelectCommand { get; }

    /// <summary>Кнопка «+» в заголовке столбца: новая сцена в этой главе.</summary>
    public ICommand AddSceneCommand { get; }

    public ObservableCollection<BookMapSceneViewModel> Scenes { get; } = [];

    /// <summary>Позиция и ширина из раскладки: одна ширина на все столбцы (ТЗ п. 27).</summary>
    public void ApplyGeometry(BookMapColumnGeometry geometry)
    {
        X = geometry.X;
        Width = geometry.Width;
    }
}

/// <summary>Сегмент шкалы статусов главы в «Быстром обзоре» (ТЗ п. 27).</summary>
public sealed record OverviewSegment(IBrush Brush, double Width);

/// <summary>
/// Карточка главы в полосе «Быстрый обзор» (ТЗ п. 27): номер, название, число сцен
/// и шкала статусов. Выбор главы подсвечивает её карточку.
/// </summary>
public sealed class ChapterOverviewViewModel : ObservableObject
{
    private const double BarWidth = 92d;

    private bool _isSelected;

    public ChapterOverviewViewModel(
        ArchitectureNodeViewModel chapter,
        int number,
        ICommand selectCommand)
    {
        Node = chapter;
        Number = number;
        SelectCommand = selectCommand;
        Title = chapter.Title;

        var scenes = chapter.Children;
        ScenesLabel = scenes.Count == 0
            ? "сцен нет"
            : $"{scenes.Count} {StoryText.Plural(scenes.Count, "сцена", "сцены", "сцен")}";

        var counts = scenes
            .GroupBy(scene => scene.Status ?? SceneStatus.Idea)
            .ToDictionary(group => group.Key, group => group.Count());
        var total = Math.Max(1, scenes.Count);
        foreach (var status in new[] { SceneStatus.Idea, SceneStatus.Draft, SceneStatus.Revised, SceneStatus.Final })
        {
            if (counts.TryGetValue(status, out var count) && count > 0)
            {
                Segments.Add(new OverviewSegment(StoryText.SceneStatusBrush(status), BarWidth * count / total));
            }
        }
    }

    public ArchitectureNodeViewModel Node { get; }

    public int Number { get; }

    public string Title { get; }

    public string ScenesLabel { get; }

    public List<OverviewSegment> Segments { get; } = [];

    public bool HasSegments => Segments.Count > 0;

    public ICommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>Пункт фильтра линий в шапке карты: «Все линии» или одна линия (ТЗ п. 27).</summary>
public sealed record PlotLineFilterOption(Guid? Id, string Name, IBrush? Brush)
{
    /// <summary>ComboBox показывает элемент через <c>ToString</c>.</summary>
    public override string ToString() => Name;
}
