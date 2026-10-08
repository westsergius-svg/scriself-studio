namespace ScriSelfStudio.Contracts.Dtos;

using System.Text.Json.Serialization;
using ScriSelfStudio.Core.Model;

/// <summary>Этап личной арки персонажа для карточки (ТЗ п. 27; ADR 010).</summary>
public sealed record CharacterArcStageDto
{
    public Guid Id { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    /// <summary>Порядок задаёт сервис структуры, а не экран.</summary>
    public int Order { get; init; }

    public CharacterArcStageKind Kind { get; init; } = CharacterArcStageKind.Custom;
}

/// <summary>Связь персонажа с другим персонажем (раздел 6 карточки).</summary>
public sealed record CharacterRelationDto
{
    public Guid CharacterId { get; init; }

    public CharacterRelationType Type { get; init; } = CharacterRelationType.Other;

    public string? Dynamic { get; init; }

    public string? Status { get; init; }
}

/// <summary>
/// Карточка персонажа: плоский снимок всех разделов. Служит и моделью правки:
/// экран карточки отправляет её целиком, поэтому частичное обновление не нужно.
/// </summary>
public sealed record CharacterDto
{
    /// <summary>Пустой идентификатор — создание нового персонажа.</summary>
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Порядок задаёт сервис структуры, а не экран.</summary>
    public int Order { get; init; }

    public string? Nickname { get; init; }

    public CharacterRole Role { get; init; } = CharacterRole.Secondary;

    public CharacterImportance Importance { get; init; } = CharacterImportance.Secondary;

    public CharacterStatus Status { get; init; } = CharacterStatus.Active;

    public string? Age { get; init; }

    public string? Gender { get; init; }

    public string? Species { get; init; }

    public string? Occupation { get; init; }

    public string? World { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string? Appearance { get; init; }

    public string? Voice { get; init; }

    public string? Mannerisms { get; init; }

    public string? AppearanceStates { get; init; }

    public string? References { get; init; }

    public string? Archetype { get; init; }

    public string? Temperament { get; init; }

    public string? Values { get; init; }

    public string? Beliefs { get; init; }

    public string? FalseBelief { get; init; }

    public string? InternalNeed { get; init; }

    public string? Want { get; init; }

    public string? Fears { get; init; }

    public string? Weaknesses { get; init; }

    public string? Strengths { get; init; }

    public string? Habits { get; init; }

    public string? Humour { get; init; }

    public string? DefenceMechanisms { get; init; }

    public string? CopingStrategies { get; init; }

    public string? ExternalGoal { get; init; }

    public string? SecretGoal { get; init; }

    public string? Motivation { get; init; }

    public string? ReadyToDo { get; init; }

    public string? NeverDo { get; init; }

    public string? Victory { get; init; }

    public string? Failure { get; init; }

    public string? Education { get; init; }

    public string? Skills { get; init; }

    public string? Combat { get; init; }

    public string? Powers { get; init; }

    public string? Talents { get; init; }

    public string? Limitations { get; init; }

    public string? Inventory { get; init; }

    public string? Companions { get; init; }

    public string? Speech { get; init; }

    public string? Accent { get; init; }

    public string? SpeechHabits { get; init; }

    public string? SpeechAvoided { get; init; }

    public string? SpeechUnderStress { get; init; }

    public string? Birthplace { get; init; }

    public string? Childhood { get; init; }

    public string? Trauma { get; init; }

    public string? Career { get; init; }

    public string? TurningPoints { get; init; }

    public string? StoryStart { get; init; }

    public string? Chronology { get; init; }

    public CharacterArcType ArcType { get; init; } = CharacterArcType.Positive;

    public string? ArcSummary { get; init; }

    public string? Secrets { get; init; }

    public string? SecretsFromSelf { get; init; }

    public string? SecretKeepers { get; init; }

    public string? SecretReveal { get; init; }

    public string? SecretConsequences { get; init; }

    public string? PhysicalChange { get; init; }

    public string? PsychologicalChange { get; init; }

    public string? RelationshipChange { get; init; }

    public string? KeyDecisions { get; init; }

    public string? Loses { get; init; }

    public string? Gains { get; init; }

    public string? SceneIdeas { get; init; }

    public string? Quotes { get; init; }

    public string? OpenQuestions { get; init; }

    public string? Notes { get; init; }

    public CharacterDraftStatus DraftStatus { get; init; } = CharacterDraftStatus.Idea;

    public string? Author { get; init; }

    public IReadOnlyList<CharacterRelationDto> Relations { get; init; } = [];

    [JsonPropertyName("arcSteps")]
    public IReadOnlyList<CharacterArcStageDto> ArcStages { get; init; } = [];
}

/// <summary>Участник шага линии: персонаж и этап его арки (ТЗ п. 27).</summary>
public sealed record StepParticipantDto
{
    public Guid CharacterId { get; init; }

    [JsonPropertyName("characterArcStepId")]
    public Guid? CharacterArcStageId { get; init; }
}

/// <summary>Шаг сюжетной линии.</summary>
public sealed record PlotLineStepDto
{
    public Guid Id { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    /// <summary>Порядок задаёт сервис структуры, а не экран.</summary>
    public int Order { get; init; }

    public bool IsFinal { get; init; }

    /// <summary>Ключ значка шага в ленте; пусто — значок по номеру шага.</summary>
    public string? Icon { get; init; }

    public IReadOnlyList<StepParticipantDto> Participants { get; init; } = [];

    /// <summary>Сцены этапа. Заполняются со стороны сцены (ADR 012); экран линий — только чтение.</summary>
    public IReadOnlyList<Guid> SceneIds { get; init; } = [];
}

/// <summary>Сюжетная линия с этапами.</summary>
public sealed record PlotLineDto
{
    /// <summary>Пустой идентификатор — создание новой линии.</summary>
    public Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Короткая подпись линии в списке.</summary>
    public string? Description { get; init; }

    /// <summary>Цель линии (макет: «Цель»).</summary>
    public string? Goal { get; init; }

    /// <summary>Стадия готовности линии (макет: «Статус»).</summary>
    public ScriSelfStudio.Core.Model.PlotLineStatus Status { get; init; } =
        ScriSelfStudio.Core.Model.PlotLineStatus.InProgress;

    /// <summary>Свободные заметки автора (макет: «Заметки»).</summary>
    public string? Notes { get; init; }

    public string? ColorHex { get; init; }

    /// <summary>Порядок задаёт сервис структуры, а не экран.</summary>
    public int Order { get; init; }

    public IReadOnlyList<PlotLineStepDto> Steps { get; init; } = [];

    public IReadOnlyList<Guid> SceneIds { get; init; } = [];
}

/// <summary>Локация книги: место действия (макет «Локации/Объекты»; ТЗ п. 27).</summary>
public sealed record LocationDto
{
    /// <summary>Пустой идентификатор — создание новой локации.</summary>
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? Notes { get; init; }

    /// <summary>Порядок задаёт сервис структуры, а не экран.</summary>
    public int Order { get; init; }
}

/// <summary>Сюжетно значимый объект книги (макет «Локации/Объекты»; ТЗ п. 27).</summary>
public sealed record StoryObjectDto
{
    /// <summary>Пустой идентификатор — создание нового объекта.</summary>
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? Notes { get; init; }

    /// <summary>Порядок задаёт сервис структуры, а не экран.</summary>
    public int Order { get; init; }
}
