namespace ScriSelfStudio.Core.Model;

using System.Text.Json.Serialization;

/// <summary>Участник этапа сюжетной линии: персонаж и этап его арки на этом этапе.</summary>
public sealed class StepParticipant
{
    public Guid CharacterId { get; set; }

    /// <summary>
    /// Этап личной арки персонажа, который проходит на этом шаге линии.
    /// <c>null</c> — пока не выбран (ТЗ п. 27).
    /// </summary>
    [JsonPropertyName("characterArcStepId")]
    public Guid? CharacterArcStageId { get; set; }
}

/// <summary>
/// Упорядоченный шаг сюжетной линии (ТЗ п. 27, 35; ADR 010).
/// На этапе выбирается персонаж и этап его арки, а также сцены, где этап происходит.
/// </summary>
public sealed class PlotLineStep : ProjectEntity
{
    public Guid PlotLineId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Линия завершается на этом этапе.</summary>
    public bool IsFinal { get; set; }

    /// <summary>
    /// Ключ значка шага в ленте (макет: значок в кружке карточки).
    /// Пусто — значок выводится по номеру шага. См. <c>StepIcons</c>.
    /// </summary>
    public string? Icon { get; set; }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<StepParticipant> Participants { get; } = [];

    /// <summary>Сцены, где происходит этап. Заполняются со стороны сцены (ADR 012).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<Guid> SceneIds { get; } = [];

    public StepParticipant? FindParticipant(Guid characterId) =>
        Participants.FirstOrDefault(participant => participant.CharacterId == characterId);
}
