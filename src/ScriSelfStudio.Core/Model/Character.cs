namespace ScriSelfStudio.Core.Model;

using System.Text.Json.Serialization;

/// <summary>Роль персонажа в истории (ТЗ п. 27, 35).</summary>
public enum CharacterRole
{
    Protagonist = 0,
    Antagonist = 1,
    Ally = 2,
    Mentor = 3,
    Secondary = 4,
    Episodic = 5,
}

/// <summary>Значимость персонажа для книги.</summary>
public enum CharacterImportance
{
    Main = 0,
    Secondary = 1,
    Background = 2,
}

/// <summary>Состояние персонажа в истории.</summary>
public enum CharacterStatus
{
    Active = 0,
    Dead = 1,
    Missing = 2,
    Unknown = 3,
}

/// <summary>Тип арки персонажа: растёт, деградирует или не меняется.</summary>
public enum CharacterArcType
{
    Positive = 0,
    Negative = 1,
    Flat = 2,
}

/// <summary>Стадия проработки карточки персонажа.</summary>
public enum CharacterDraftStatus
{
    Idea = 0,
    Draft = 1,
    Ready = 2,
}

/// <summary>
/// Персонаж книги с подробной карточкой (ТЗ п. 27, 35; ADR 010).
///
/// Почти все поля — свободный текст: автор пишет «25 лет» или «неизвестен» так, как ему
/// удобно, а приложение хранит и показывает значение, не разбирая его. Перечисления
/// используются только там, где без них нельзя: роль, значимость, состояние, тип арки.
/// </summary>
public sealed class Character : ProjectEntity
{
    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Краткое описание для списков и карты.</summary>
    public string? Description { get; set; }

    // 1. Основное
    public string? Nickname { get; set; }

    public CharacterRole Role { get; set; } = CharacterRole.Secondary;

    public CharacterImportance Importance { get; set; } = CharacterImportance.Secondary;

    public CharacterStatus Status { get; set; } = CharacterStatus.Active;

    public string? Age { get; set; }

    public string? Gender { get; set; }

    public string? Species { get; set; }

    public string? Occupation { get; set; }

    public string? World { get; set; }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<string> Tags { get; } = [];

    // 2. Внешность
    public string? Appearance { get; set; }

    public string? Voice { get; set; }

    public string? Mannerisms { get; set; }

    public string? AppearanceStates { get; set; }

    public string? References { get; set; }

    // 3. Личность и психология
    public string? Archetype { get; set; }

    public string? Temperament { get; set; }

    public string? Values { get; set; }

    public string? Beliefs { get; set; }

    public string? FalseBelief { get; set; }

    public string? InternalNeed { get; set; }

    public string? Want { get; set; }

    public string? Fears { get; set; }

    public string? Weaknesses { get; set; }

    public string? Strengths { get; set; }

    public string? Habits { get; set; }

    public string? Humour { get; set; }

    public string? DefenceMechanisms { get; set; }

    public string? CopingStrategies { get; set; }

    // 4. Мотивация и цели
    public string? ExternalGoal { get; set; }

    public string? SecretGoal { get; set; }

    public string? Motivation { get; set; }

    public string? ReadyToDo { get; set; }

    public string? NeverDo { get; set; }

    public string? Victory { get; set; }

    public string? Failure { get; set; }

    // 5. Навыки и способности
    public string? Education { get; set; }

    public string? Skills { get; set; }

    public string? Combat { get; set; }

    public string? Powers { get; set; }

    public string? Talents { get; set; }

    public string? Limitations { get; set; }

    public string? Inventory { get; set; }

    public string? Companions { get; set; }

    // 7. Речь и манера общения
    public string? Speech { get; set; }

    public string? Accent { get; set; }

    public string? SpeechHabits { get; set; }

    public string? SpeechAvoided { get; set; }

    public string? SpeechUnderStress { get; set; }

    // 8. Биография
    public string? Birthplace { get; set; }

    public string? Childhood { get; set; }

    public string? Trauma { get; set; }

    public string? Career { get; set; }

    public string? TurningPoints { get; set; }

    public string? StoryStart { get; set; }

    public string? Chronology { get; set; }

    // 9. Арка персонажа
    public CharacterArcType ArcType { get; set; } = CharacterArcType.Positive;

    public string? ArcSummary { get; set; }

    // 11. Секреты
    public string? Secrets { get; set; }

    public string? SecretsFromSelf { get; set; }

    public string? SecretKeepers { get; set; }

    public string? SecretReveal { get; set; }

    public string? SecretConsequences { get; set; }

    // 12. Развитие в истории
    public string? PhysicalChange { get; set; }

    public string? PsychologicalChange { get; set; }

    public string? RelationshipChange { get; set; }

    public string? KeyDecisions { get; set; }

    public string? Loses { get; set; }

    public string? Gains { get; set; }

    // 13. Заметки и цитаты
    public string? SceneIdeas { get; set; }

    public string? Quotes { get; set; }

    public string? OpenQuestions { get; set; }

    public string? Notes { get; set; }

    // 14. Метаданные проработки
    public CharacterDraftStatus DraftStatus { get; set; } = CharacterDraftStatus.Idea;

    public string? Author { get; set; }

    /// <summary>Связи с другими персонажами (раздел 6).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<CharacterRelation> Relations { get; } = [];

    /// <summary>Этапы личной арки персонажа (раздел 9).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    [JsonPropertyName("arcSteps")]
    public List<CharacterArcStage> ArcStages { get; } = [];

    public CharacterArcStage? FindArcStage(Guid stageId) =>
        ArcStages.FirstOrDefault(stage => stage.Id == stageId);
}

/// <summary>Тип связи между персонажами (раздел 6 карточки).</summary>
public enum CharacterRelationType
{
    Family = 0,
    Friend = 1,
    Enemy = 2,
    Love = 3,
    Mentor = 4,
    Rival = 5,
    Dependency = 6,
    Other = 7,
}

/// <summary>Связь персонажа с другим персонажем.</summary>
public sealed class CharacterRelation
{
    public Guid CharacterId { get; set; }

    public CharacterRelationType Type { get; set; } = CharacterRelationType.Other;

    /// <summary>Как связь меняется по ходу книги.</summary>
    public string? Dynamic { get; set; }

    /// <summary>Текущий статус связи: вместе, в ссоре, мёртв…</summary>
    public string? Status { get; set; }
}
