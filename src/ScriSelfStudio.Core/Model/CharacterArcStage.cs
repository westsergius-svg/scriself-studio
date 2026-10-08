namespace ScriSelfStudio.Core.Model;

using System.Text.Json.Serialization;

/// <summary>
/// Канонический этап арки персонажа. Список расширяемый: у автора может быть больше этапов,
/// поэтому <see cref="Custom"/> означает «свой этап» (ТЗ п. 27; ADR 010).
/// </summary>
public enum CharacterArcStageKind
{
    InitialState = 0,
    Trigger = 1,
    Resistance = 2,
    TurningPoint = 3,
    Crisis = 4,
    Realization = 5,
    FinalState = 6,
    Custom = 7,
}

/// <summary>Этап личной арки персонажа: как он меняется по ходу книги.</summary>
public sealed class CharacterArcStage : ProjectEntity
{
    public Guid CharacterId { get; set; }

    public CharacterArcStageKind Kind { get; set; } = CharacterArcStageKind.Custom;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Канонические семь этапов арки — то, что создаётся у нового персонажа.</summary>
    public static IReadOnlyList<(CharacterArcStageKind Kind, string Title)> DefaultStages { get; } =
    [
        (CharacterArcStageKind.InitialState, "Начальное состояние"),
        (CharacterArcStageKind.Trigger, "Триггер"),
        (CharacterArcStageKind.Resistance, "Сопротивление"),
        (CharacterArcStageKind.TurningPoint, "Перелом"),
        (CharacterArcStageKind.Crisis, "Кризис"),
        (CharacterArcStageKind.Realization, "Осознание"),
        (CharacterArcStageKind.FinalState, "Финальное состояние"),
    ];
}
