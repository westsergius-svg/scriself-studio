namespace ScriSelfStudio.StoryUI;

using System.Globalization;
using Avalonia.Media;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation.Localization;

/// <summary>
/// Подписи перечислений и вспомогательные данные экранов персонажей и линий.
/// Перечисления хранятся в ядре, но показываются по-русски (ТЗ п. 27).
/// </summary>
public static class StoryLabels
{
    public static IReadOnlyList<Choice<CharacterRole>> Roles { get; } =
    [
        new(CharacterRole.Protagonist, "Протагонист"),
        new(CharacterRole.Antagonist, "Антагонист"),
        new(CharacterRole.Ally, "Союзник"),
        new(CharacterRole.Mentor, "Наставник"),
        new(CharacterRole.Secondary, "Второстепенный"),
        new(CharacterRole.Episodic, "Эпизодический"),
    ];

    public static IReadOnlyList<Choice<CharacterImportance>> Importances { get; } =
    [
        new(CharacterImportance.Main, "Главный"),
        new(CharacterImportance.Secondary, "Второстепенный"),
        new(CharacterImportance.Background, "Фоновый"),
    ];

    public static IReadOnlyList<Choice<CharacterStatus>> Statuses { get; } =
    [
        new(CharacterStatus.Active, "Активен"),
        new(CharacterStatus.Dead, "Мёртв"),
        new(CharacterStatus.Missing, "Пропал"),
        new(CharacterStatus.Unknown, "Неизвестно"),
    ];

    public static IReadOnlyList<Choice<CharacterArcType>> ArcTypes { get; } =
    [
        new(CharacterArcType.Positive, "Положительная"),
        new(CharacterArcType.Negative, "Отрицательная"),
        new(CharacterArcType.Flat, "Плоская"),
    ];

    public static IReadOnlyList<Choice<CharacterDraftStatus>> DraftStatuses { get; } =
    [
        new(CharacterDraftStatus.Idea, "Идея"),
        new(CharacterDraftStatus.Draft, "Черновик"),
        new(CharacterDraftStatus.Ready, "Готов"),
    ];

    public static IReadOnlyList<Choice<CharacterRelationType>> RelationTypes { get; } =
    [
        new(CharacterRelationType.Family, "Семья"),
        new(CharacterRelationType.Friend, "Друг"),
        new(CharacterRelationType.Enemy, "Враг"),
        new(CharacterRelationType.Love, "Любовь"),
        new(CharacterRelationType.Mentor, "Наставник"),
        new(CharacterRelationType.Rival, "Соперник"),
        new(CharacterRelationType.Dependency, "Зависимость"),
        new(CharacterRelationType.Other, "Другое"),
    ];

    public static IReadOnlyList<Choice<CharacterArcStageKind>> ArcStageKinds { get; } =
    [
        new(CharacterArcStageKind.InitialState, "Начальное состояние"),
        new(CharacterArcStageKind.Trigger, "Триггер"),
        new(CharacterArcStageKind.Resistance, "Сопротивление"),
        new(CharacterArcStageKind.TurningPoint, "Перелом"),
        new(CharacterArcStageKind.Crisis, "Кризис"),
        new(CharacterArcStageKind.Realization, "Осознание"),
        new(CharacterArcStageKind.FinalState, "Финальное состояние"),
        new(CharacterArcStageKind.Custom, "Свой этап"),
    ];

    public static string RoleLabel(CharacterRole role) => Loc.T(Find(Roles, role).Label);

    public static string ImportanceLabel(CharacterImportance importance) => Loc.T(Find(Importances, importance).Label);

    public static string StatusLabel(CharacterStatus status) => Loc.T(Find(Statuses, status).Label);

    public static string ArcTypeLabel(CharacterArcType arcType) => Loc.T(Find(ArcTypes, arcType).Label);

    public static string DraftStatusLabel(CharacterDraftStatus status) => Loc.T(Find(DraftStatuses, status).Label);

    public static string ArcStageKindLabel(CharacterArcStageKind kind) => Loc.T(Find(ArcStageKinds, kind).Label);

    public static Choice<T> Find<T>(IReadOnlyList<Choice<T>> choices, T value)
        where T : struct, Enum =>
        choices.FirstOrDefault(choice => EqualityComparer<T>.Default.Equals(choice.Value, value)) ?? choices[0];

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

    /// <summary>Цвет линии по умолчанию, если автор его не выбрал (макет: красный).</summary>
    public static string DefaultLineColor => "#B3565D";

    /// <summary>Цвет линии по умолчанию как кисть.</summary>
    public static IBrush DefaultLineBrush { get; } = new SolidColorBrush(Color.Parse(DefaultLineColor));

    /// <summary>Палитра выбора цвета линии из макета (шесть образцов).</summary>
    public static IReadOnlyList<string> LineSwatches { get; } =
        ["#A84749", "#825EB7", "#45859C", "#E8A63B", "#2A78DD", "#7E838A"];

    /// <summary>Подписи статусов сюжетной линии (макет: «Статус»).</summary>
    public static IReadOnlyList<Choice<PlotLineStatus>> LineStatuses { get; } =
    [
        new(PlotLineStatus.Idea, "Идея"),
        new(PlotLineStatus.Draft, "Черновик"),
        new(PlotLineStatus.InProgress, "В работе"),
        new(PlotLineStatus.Done, "Готово"),
    ];

    public static string LineStatusLabel(PlotLineStatus status) => Loc.T(Find(LineStatuses, status).Label);

    /// <summary>Цвет точки статуса линии (макет: зелёная точка у «В работе»).</summary>
    public static IBrush StatusDot(PlotLineStatus status) => status switch
    {
        PlotLineStatus.Done => new SolidColorBrush(Color.Parse("#3F9E64")),
        PlotLineStatus.InProgress => new SolidColorBrush(Color.Parse("#5F9A46")),
        PlotLineStatus.Draft => new SolidColorBrush(Color.Parse("#C08A2E")),
        _ => new SolidColorBrush(Color.Parse("#8A8073")),
    };



    public static string FormatOrder(int number) => number.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>Пара «значение перечисления — русская подпись» для выпадающих списков.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    /// <summary>ComboBox показывает элемент через <c>ToString</c>: без этого был бы тип записи.</summary>
    public override string ToString() => Loc.T(Label);
}
