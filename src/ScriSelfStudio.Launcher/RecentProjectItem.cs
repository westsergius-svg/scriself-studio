namespace ScriSelfStudio.Launcher;

using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ScriSelfStudio.Contracts.Settings;

/// <summary>
/// Карточка проекта в списке лаунчера (ТЗ п. 27): имя, сводка, дата изменения и
/// обложка-монограмма. Выделение карточки ведёт список, а правая панель
/// «Контекст проекта» привязана к выбранному элементу.
/// </summary>
public sealed class RecentProjectItem
{
    /// <summary>Цвета обложек: круговой выбор по имени проекта (макет: иконки в плитке).</summary>
    private static readonly string[] CoverAccents =
    [
        "#B3565D", "#B99B66", "#4A899C", "#8161B3", "#5F9A46", "#2A78DD",
    ];

    public RecentProjectItem(
        RecentProjectEntry entry,
        ICommand openCommand,
        ICommand removeCommand,
        ICommand selectCommand,
        ICommand folderCommand)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Name = entry.Name;
        FilePath = entry.FilePath;
        LastOpenedAt = entry.LastOpenedAt;
        Description = entry.Description;
        BookCount = entry.BookCount;
        ChapterCount = entry.ChapterCount;
        SceneCount = entry.SceneCount;
        CharacterCount = entry.CharacterCount;
        PlotLineCount = entry.PlotLineCount;

        // Дата изменения: у записей старого формата её нет — берём дату открытия.
        UpdatedAt = entry.UpdatedAt == default ? entry.LastOpenedAt : entry.UpdatedAt;

        OpenCommand = openCommand;
        RemoveCommand = removeCommand;
        SelectCommand = selectCommand;
        FolderCommand = folderCommand;

        var accent = Color.Parse(CoverAccents[CoverIndex(Name)]);
        CoverFill = new ImmutableSolidColorBrush(Color.FromArgb(0x24, accent.R, accent.G, accent.B));
        CoverInk = new ImmutableSolidColorBrush(accent);
        Monogram = string.IsNullOrWhiteSpace(Name)
            ? "?"
            : Name.Trim()[..1].ToUpper(CultureInfo.CurrentCulture);
    }

    public string Name { get; }

    public string FilePath { get; }

    public string? Description { get; }

    public DateTimeOffset LastOpenedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public int BookCount { get; }

    public int ChapterCount { get; }

    public int SceneCount { get; }

    public int CharacterCount { get; }

    public int PlotLineCount { get; }

    /// <summary>Первая буква имени — обложка карточки.</summary>
    public string Monogram { get; }

    public IBrush CoverFill { get; }

    public IBrush CoverInk { get; }

    public ICommand OpenCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand SelectCommand { get; }

    /// <summary>Показать файл проекта в проводнике.</summary>
    public ICommand FolderCommand { get; }

    /// <summary>Сводка карточки: «12 глав · 48 сцен» (макет).</summary>
    public string SummaryText => BuildSummary();

    public bool HasSummary => BookCount > 0 || ChapterCount > 0 || SceneCount > 0;

    /// <summary>«Последнее изменение: сегодня, 21:34» (макет).</summary>
    public string LastChangedText => BuildLastChanged();

    public string DescriptionText => string.IsNullOrWhiteSpace(Description)
        ? "Описание пока не задано."
        : Description!;

    private static int CoverIndex(string value)
    {
        var hash = 0;
        foreach (var symbol in value)
        {
            hash = ((hash * 31) + symbol) & 0x7FFFFFFF;
        }

        return hash % CoverAccents.Length;
    }

    private string BuildSummary()
    {
        var parts = new List<string>(3);
        if (BookCount > 0)
        {
            parts.Add(Plural(BookCount, "книга", "книги", "книг"));
        }

        if (ChapterCount > 0)
        {
            parts.Add(Plural(ChapterCount, "глава", "главы", "глав"));
        }

        if (SceneCount > 0)
        {
            parts.Add(Plural(SceneCount, "сцена", "сцены", "сцен"));
        }

        return string.Join(" · ", parts);
    }

    private string BuildLastChanged()
    {
        var local = UpdatedAt.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        var day = local.Date;
        var when = day == today
            ? "сегодня"
            : day == today.AddDays(-1)
                ? "вчера"
                : local.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
        return $"Последнее изменение: {when}, {local.ToString("HH:mm", CultureInfo.CurrentCulture)}";
    }

    /// <summary>Русская форма числительного: 1 глава, 2 главы, 5 глав.</summary>
    private static string Plural(int count, string one, string few, string many)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        var word = mod100 is >= 11 and <= 14
            ? many
            : mod10 switch
            {
                1 => one,
                2 or 3 or 4 => few,
                _ => many,
            };
        return $"{count} {word}";
    }
}
