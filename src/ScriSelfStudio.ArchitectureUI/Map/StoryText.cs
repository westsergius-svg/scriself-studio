namespace ScriSelfStudio.ArchitectureUI.Map;

using Avalonia.Media;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation.Localization;

/// <summary>
/// Подписи перечислений и склонения для карты книги. Ядро хранит значения,
/// а карта показывает их по-русски (ТЗ п. 27). Дублирует экраны персонажей
/// и линий намеренно: модули не зависят друг от друга.
/// </summary>
public static class StoryText
{
    public static string SceneStatusLabel(SceneStatus status) => status switch
    {
        SceneStatus.Draft => Loc.T("черновик"),
        SceneStatus.Revised => Loc.T("в работе"),
        SceneStatus.Final => Loc.T("готово"),
        _ => Loc.T("идея"),
    };

    public static string LineStatusLabel(PlotLineStatus status) => status switch
    {
        PlotLineStatus.Idea => Loc.T("идея"),
        PlotLineStatus.Draft => Loc.T("черновик"),
        PlotLineStatus.InProgress => Loc.T("в работе"),
        PlotLineStatus.Done => Loc.T("готова"),
        _ => Loc.T("идея"),
    };

    public static string RoleLabel(CharacterRole role) => role switch
    {
        CharacterRole.Protagonist => Loc.T("Протагонист"),
        CharacterRole.Antagonist => Loc.T("Антагонист"),
        CharacterRole.Ally => Loc.T("Союзник"),
        CharacterRole.Mentor => Loc.T("Наставник"),
        CharacterRole.Episodic => Loc.T("Эпизодический"),
        _ => Loc.T("Второстепенный"),
    };

    /// <summary>Цвет точки статуса линии (макет: зелёная точка у «В работе»).</summary>
    public static IBrush StatusDot(PlotLineStatus status) => status switch
    {
        PlotLineStatus.Done => new SolidColorBrush(Color.Parse("#3F9E64")),
        PlotLineStatus.InProgress => new SolidColorBrush(Color.Parse("#5F9A46")),
        PlotLineStatus.Draft => new SolidColorBrush(Color.Parse("#C08A2E")),
        _ => new SolidColorBrush(Color.Parse("#8A8073")),
    };

    /// <summary>Цвет сегмента шкалы статусов сцены в «Быстром обзоре» (ТЗ п. 27).</summary>
    public static IBrush SceneStatusBrush(SceneStatus status) => status switch
    {
        SceneStatus.Draft => new SolidColorBrush(Color.Parse("#E8963E")),
        SceneStatus.Revised => new SolidColorBrush(Color.Parse("#4A7DFF")),
        SceneStatus.Final => new SolidColorBrush(Color.Parse("#3FBF7F")),
        _ => new SolidColorBrush(Color.Parse("#9AA0A6")),
    };

    /// <summary>Русское склонение существительного по числу: 1 сцена, 2 сцены, 5 сцен.</summary>
    public static string Plural(int count, string one, string few, string many)
    {
        var mod100 = Math.Abs(count) % 100;
        var mod10 = mod100 % 10;
        if (mod100 is >= 11 and <= 14)
        {
            return Loc.T(many);
        }

        return mod10 switch
        {
            1 => Loc.T(one),
            >= 2 and <= 4 => Loc.T(few),
            _ => Loc.T(many),
        };
    }

    /// <summary>Цвет линии как кисть; некорректное значение заменяется нейтральным.</summary>
    public static IBrush BrushFromHex(string? hex, IBrush fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}
