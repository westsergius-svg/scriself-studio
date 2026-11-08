namespace ScriSelfStudio.StoryUI;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation.Localization;

/// <summary>Персонаж в списке выбора: связь и участник шага линии выбирают его по имени.</summary>
public sealed record CharacterChoice(Guid Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Редактируемый этап личной арки персонажа (раздел 9 карточки; ТЗ п. 27).</summary>
public sealed partial class ArcStageDraft : ObservableObject
{
    public ArcStageDraft(Guid id, string title, CharacterArcStageKind kind, string? description)
    {
        Id = id;
        _title = title;
        _kind = StoryLabels.Find(StoryLabels.ArcStageKinds, kind);
        _description = description;
    }

    public Guid Id { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private Choice<CharacterArcStageKind>? _kind;

    [ObservableProperty]
    private string? _description;
}

/// <summary>Одна связь персонажа с другим персонажем (раздел 6 карточки).</summary>
public sealed partial class RelationDraft : ObservableObject
{
    public RelationDraft(
        IReadOnlyList<CharacterChoice> availableCharacters,
        Guid characterId,
        CharacterRelationType type,
        string? dynamic,
        string? status)
    {
        AvailableCharacters = availableCharacters;
        _character = availableCharacters.FirstOrDefault(choice => choice.Id == characterId);
        _type = StoryLabels.Find(StoryLabels.RelationTypes, type);
        _dynamic = dynamic;
        _status = status;
    }

    public IReadOnlyList<CharacterChoice> AvailableCharacters { get; }

    [ObservableProperty]
    private CharacterChoice? _character;

    [ObservableProperty]
    private Choice<CharacterRelationType>? _type;

    [ObservableProperty]
    private string? _dynamic;

    [ObservableProperty]
    private string? _status;
}

/// <summary>
/// Карточка персонажа целиком: 14 разделов ТЗ, но почти все поля — свободный текст,
/// поэтому правка возможна без разбора значений (ТЗ п. 27; ADR 010).
/// Черновик — единственная модель правки: экран отправляет карточку целиком.
/// </summary>
public sealed partial class CharacterDraft : ObservableObject
{
    private static readonly char[] TagSeparators = [',', ';', '\n', '\r'];

    private CharacterDraft(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }

    public bool IsNew => Id == Guid.Empty;

    // 1. Основное
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string? _nickname;

    [ObservableProperty]
    private Choice<CharacterRole>? _role;

    [ObservableProperty]
    private Choice<CharacterImportance>? _importance;

    [ObservableProperty]
    private Choice<CharacterStatus>? _status;

    [ObservableProperty]
    private string? _age;

    [ObservableProperty]
    private string? _gender;

    [ObservableProperty]
    private string? _species;

    [ObservableProperty]
    private string? _occupation;

    [ObservableProperty]
    private string? _world;

    /// <summary>Теги через запятую: так их удобнее набирать в одной строке.</summary>
    [ObservableProperty]
    private string _tagsText = string.Empty;

    // 2. Внешность
    [ObservableProperty]
    private string? _appearance;

    [ObservableProperty]
    private string? _voice;

    [ObservableProperty]
    private string? _mannerisms;

    [ObservableProperty]
    private string? _appearanceStates;

    [ObservableProperty]
    private string? _references;

    // 3. Личность и психология
    [ObservableProperty]
    private string? _archetype;

    [ObservableProperty]
    private string? _temperament;

    [ObservableProperty]
    private string? _values;

    [ObservableProperty]
    private string? _beliefs;

    [ObservableProperty]
    private string? _falseBelief;

    [ObservableProperty]
    private string? _internalNeed;

    [ObservableProperty]
    private string? _want;

    [ObservableProperty]
    private string? _fears;

    [ObservableProperty]
    private string? _weaknesses;

    [ObservableProperty]
    private string? _strengths;

    [ObservableProperty]
    private string? _habits;

    [ObservableProperty]
    private string? _humour;

    [ObservableProperty]
    private string? _defenceMechanisms;

    [ObservableProperty]
    private string? _copingStrategies;

    // 4. Мотивация и цели
    [ObservableProperty]
    private string? _externalGoal;

    [ObservableProperty]
    private string? _secretGoal;

    [ObservableProperty]
    private string? _motivation;

    [ObservableProperty]
    private string? _readyToDo;

    [ObservableProperty]
    private string? _neverDo;

    [ObservableProperty]
    private string? _victory;

    [ObservableProperty]
    private string? _failure;

    // 5. Навыки и способности
    [ObservableProperty]
    private string? _education;

    [ObservableProperty]
    private string? _skills;

    [ObservableProperty]
    private string? _combat;

    [ObservableProperty]
    private string? _powers;

    [ObservableProperty]
    private string? _talents;

    [ObservableProperty]
    private string? _limitations;

    [ObservableProperty]
    private string? _inventory;

    [ObservableProperty]
    private string? _companions;

    // 7. Речь и манера общения
    [ObservableProperty]
    private string? _speech;

    [ObservableProperty]
    private string? _accent;

    [ObservableProperty]
    private string? _speechHabits;

    [ObservableProperty]
    private string? _speechAvoided;

    [ObservableProperty]
    private string? _speechUnderStress;

    // 8. Биография
    [ObservableProperty]
    private string? _birthplace;

    [ObservableProperty]
    private string? _childhood;

    [ObservableProperty]
    private string? _trauma;

    [ObservableProperty]
    private string? _career;

    [ObservableProperty]
    private string? _turningPoints;

    [ObservableProperty]
    private string? _storyStart;

    [ObservableProperty]
    private string? _chronology;

    // 9. Арка персонажа
    [ObservableProperty]
    private Choice<CharacterArcType>? _arcType;

    [ObservableProperty]
    private string? _arcSummary;

    // 11. Секреты и тайны
    [ObservableProperty]
    private string? _secrets;

    [ObservableProperty]
    private string? _secretsFromSelf;

    [ObservableProperty]
    private string? _secretKeepers;

    [ObservableProperty]
    private string? _secretReveal;

    [ObservableProperty]
    private string? _secretConsequences;

    // 12. Развитие в истории
    [ObservableProperty]
    private string? _physicalChange;

    [ObservableProperty]
    private string? _psychologicalChange;

    [ObservableProperty]
    private string? _relationshipChange;

    [ObservableProperty]
    private string? _keyDecisions;

    [ObservableProperty]
    private string? _loses;

    [ObservableProperty]
    private string? _gains;

    // 13. Заметки и цитаты
    [ObservableProperty]
    private string? _sceneIdeas;

    [ObservableProperty]
    private string? _quotes;

    [ObservableProperty]
    private string? _openQuestions;

    [ObservableProperty]
    private string? _notes;

    // 14. Метаданные проработки
    [ObservableProperty]
    private Choice<CharacterDraftStatus>? _draftStatus;

    [ObservableProperty]
    private string? _author;

    public ObservableCollection<ArcStageDraft> ArcStages { get; } = [];

    public ObservableCollection<RelationDraft> Relations { get; } = [];

    /// <summary>Новый персонаж: карточка сразу получает семь канонических этапов арки.</summary>
    public static CharacterDraft New()
    {
        var draft = ApplyDefaults(new CharacterDraft(Guid.Empty));
        return draft;
    }

    /// <summary>Карточка существующего персонажа.</summary>
    public static CharacterDraft From(CharacterDto card, IReadOnlyList<CharacterChoice> allCharacters)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(allCharacters);

        var draft = ApplyDefaults(new CharacterDraft(card.Id));
        draft.Name = card.Name;
        draft.Description = card.Description;
        draft.Nickname = card.Nickname;
        draft.Role = StoryLabels.Find(StoryLabels.Roles, card.Role);
        draft.Importance = StoryLabels.Find(StoryLabels.Importances, card.Importance);
        draft.Status = StoryLabels.Find(StoryLabels.Statuses, card.Status);
        draft.Age = card.Age;
        draft.Gender = card.Gender;
        draft.Species = card.Species;
        draft.Occupation = card.Occupation;
        draft.World = card.World;
        draft.TagsText = string.Join(", ", card.Tags);
        draft.Appearance = card.Appearance;
        draft.Voice = card.Voice;
        draft.Mannerisms = card.Mannerisms;
        draft.AppearanceStates = card.AppearanceStates;
        draft.References = card.References;
        draft.Archetype = card.Archetype;
        draft.Temperament = card.Temperament;
        draft.Values = card.Values;
        draft.Beliefs = card.Beliefs;
        draft.FalseBelief = card.FalseBelief;
        draft.InternalNeed = card.InternalNeed;
        draft.Want = card.Want;
        draft.Fears = card.Fears;
        draft.Weaknesses = card.Weaknesses;
        draft.Strengths = card.Strengths;
        draft.Habits = card.Habits;
        draft.Humour = card.Humour;
        draft.DefenceMechanisms = card.DefenceMechanisms;
        draft.CopingStrategies = card.CopingStrategies;
        draft.ExternalGoal = card.ExternalGoal;
        draft.SecretGoal = card.SecretGoal;
        draft.Motivation = card.Motivation;
        draft.ReadyToDo = card.ReadyToDo;
        draft.NeverDo = card.NeverDo;
        draft.Victory = card.Victory;
        draft.Failure = card.Failure;
        draft.Education = card.Education;
        draft.Skills = card.Skills;
        draft.Combat = card.Combat;
        draft.Powers = card.Powers;
        draft.Talents = card.Talents;
        draft.Limitations = card.Limitations;
        draft.Inventory = card.Inventory;
        draft.Companions = card.Companions;
        draft.Speech = card.Speech;
        draft.Accent = card.Accent;
        draft.SpeechHabits = card.SpeechHabits;
        draft.SpeechAvoided = card.SpeechAvoided;
        draft.SpeechUnderStress = card.SpeechUnderStress;
        draft.Birthplace = card.Birthplace;
        draft.Childhood = card.Childhood;
        draft.Trauma = card.Trauma;
        draft.Career = card.Career;
        draft.TurningPoints = card.TurningPoints;
        draft.StoryStart = card.StoryStart;
        draft.Chronology = card.Chronology;
        draft.ArcType = StoryLabels.Find(StoryLabels.ArcTypes, card.ArcType);
        draft.ArcSummary = card.ArcSummary;
        draft.Secrets = card.Secrets;
        draft.SecretsFromSelf = card.SecretsFromSelf;
        draft.SecretKeepers = card.SecretKeepers;
        draft.SecretReveal = card.SecretReveal;
        draft.SecretConsequences = card.SecretConsequences;
        draft.PhysicalChange = card.PhysicalChange;
        draft.PsychologicalChange = card.PsychologicalChange;
        draft.RelationshipChange = card.RelationshipChange;
        draft.KeyDecisions = card.KeyDecisions;
        draft.Loses = card.Loses;
        draft.Gains = card.Gains;
        draft.SceneIdeas = card.SceneIdeas;
        draft.Quotes = card.Quotes;
        draft.OpenQuestions = card.OpenQuestions;
        draft.Notes = card.Notes;
        draft.DraftStatus = StoryLabels.Find(StoryLabels.DraftStatuses, card.DraftStatus);
        draft.Author = card.Author;

        draft.ArcStages.Clear();
        foreach (var step in card.ArcStages)
        {
            draft.ArcStages.Add(new ArcStageDraft(step.Id, step.Title, step.Kind, step.Description));
        }

        if (draft.IsNew && draft.ArcStages.Count == 0)
        {
            AddCanonicalSteps(draft);
        }

        draft.Relations.Clear();
        foreach (var relation in card.Relations)
        {
            draft.Relations.Add(new RelationDraft(
                OppositeCharacters(allCharacters, card.Id),
                relation.CharacterId,
                relation.Type,
                relation.Dynamic,
                relation.Status));
        }

        return draft;
    }

    /// <summary>Снимок карточки для сервиса структуры: порядок и идентификаторы этапов сохраняются.</summary>
    public CharacterDto ToDto() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        Description = Trimmed(Description),
        Nickname = Trimmed(Nickname),
        Role = Role?.Value ?? CharacterRole.Secondary,
        Importance = Importance?.Value ?? CharacterImportance.Secondary,
        Status = Status?.Value ?? CharacterStatus.Active,
        Age = Trimmed(Age),
        Gender = Trimmed(Gender),
        Species = Trimmed(Species),
        Occupation = Trimmed(Occupation),
        World = Trimmed(World),
        Tags = ParseTags(TagsText),
        Appearance = Trimmed(Appearance),
        Voice = Trimmed(Voice),
        Mannerisms = Trimmed(Mannerisms),
        AppearanceStates = Trimmed(AppearanceStates),
        References = Trimmed(References),
        Archetype = Trimmed(Archetype),
        Temperament = Trimmed(Temperament),
        Values = Trimmed(Values),
        Beliefs = Trimmed(Beliefs),
        FalseBelief = Trimmed(FalseBelief),
        InternalNeed = Trimmed(InternalNeed),
        Want = Trimmed(Want),
        Fears = Trimmed(Fears),
        Weaknesses = Trimmed(Weaknesses),
        Strengths = Trimmed(Strengths),
        Habits = Trimmed(Habits),
        Humour = Trimmed(Humour),
        DefenceMechanisms = Trimmed(DefenceMechanisms),
        CopingStrategies = Trimmed(CopingStrategies),
        ExternalGoal = Trimmed(ExternalGoal),
        SecretGoal = Trimmed(SecretGoal),
        Motivation = Trimmed(Motivation),
        ReadyToDo = Trimmed(ReadyToDo),
        NeverDo = Trimmed(NeverDo),
        Victory = Trimmed(Victory),
        Failure = Trimmed(Failure),
        Education = Trimmed(Education),
        Skills = Trimmed(Skills),
        Combat = Trimmed(Combat),
        Powers = Trimmed(Powers),
        Talents = Trimmed(Talents),
        Limitations = Trimmed(Limitations),
        Inventory = Trimmed(Inventory),
        Companions = Trimmed(Companions),
        Speech = Trimmed(Speech),
        Accent = Trimmed(Accent),
        SpeechHabits = Trimmed(SpeechHabits),
        SpeechAvoided = Trimmed(SpeechAvoided),
        SpeechUnderStress = Trimmed(SpeechUnderStress),
        Birthplace = Trimmed(Birthplace),
        Childhood = Trimmed(Childhood),
        Trauma = Trimmed(Trauma),
        Career = Trimmed(Career),
        TurningPoints = Trimmed(TurningPoints),
        StoryStart = Trimmed(StoryStart),
        Chronology = Trimmed(Chronology),
        ArcType = ArcType?.Value ?? CharacterArcType.Positive,
        ArcSummary = Trimmed(ArcSummary),
        Secrets = Trimmed(Secrets),
        SecretsFromSelf = Trimmed(SecretsFromSelf),
        SecretKeepers = Trimmed(SecretKeepers),
        SecretReveal = Trimmed(SecretReveal),
        SecretConsequences = Trimmed(SecretConsequences),
        PhysicalChange = Trimmed(PhysicalChange),
        PsychologicalChange = Trimmed(PsychologicalChange),
        RelationshipChange = Trimmed(RelationshipChange),
        KeyDecisions = Trimmed(KeyDecisions),
        Loses = Trimmed(Loses),
        Gains = Trimmed(Gains),
        SceneIdeas = Trimmed(SceneIdeas),
        Quotes = Trimmed(Quotes),
        OpenQuestions = Trimmed(OpenQuestions),
        Notes = Trimmed(Notes),
        DraftStatus = DraftStatus?.Value ?? CharacterDraftStatus.Idea,
        Author = Trimmed(Author),
        Relations =
        [
            .. Relations
                .Where(relation => relation.Character is not null)
                .Select(relation => new CharacterRelationDto
                {
                    CharacterId = relation.Character!.Id,
                    Type = relation.Type?.Value ?? CharacterRelationType.Other,
                    Dynamic = Trimmed(relation.Dynamic),
                    Status = Trimmed(relation.Status),
                }),
        ],
        ArcStages =
        [
            .. ArcStages
                .Where(step => !string.IsNullOrWhiteSpace(step.Title))
                .Select(step => new CharacterArcStageDto
                {
                    Id = step.Id,
                    Title = step.Title.Trim(),
                    Description = Trimmed(step.Description),
                    Kind = step.Kind?.Value ?? CharacterArcStageKind.Custom,
                }),
        ],
    };

    /// <summary>Список связей всегда без самого персонажа: связать его с собой нельзя.</summary>
    public IReadOnlyList<CharacterChoice> RelationCandidates(IReadOnlyList<CharacterChoice> allCharacters) =>
        OppositeCharacters(allCharacters, Id);

    private static IReadOnlyList<CharacterChoice> OppositeCharacters(
        IReadOnlyList<CharacterChoice> allCharacters,
        Guid selfId) =>
        [.. allCharacters.Where(choice => choice.Id != selfId && choice.Id != Guid.Empty)];

    private static CharacterDraft ApplyDefaults(CharacterDraft draft)
    {
        draft.Role = StoryLabels.Roles[0];
        draft.Importance = StoryLabels.Importances[1];
        draft.Status = StoryLabels.Statuses[0];
        draft.ArcType = StoryLabels.ArcTypes[0];
        draft.DraftStatus = StoryLabels.DraftStatuses[0];
        AddCanonicalSteps(draft);
        return draft;
    }

    private static void AddCanonicalSteps(CharacterDraft draft)
    {
        foreach (var (kind, title) in CharacterArcStage.DefaultStages)
        {
            draft.ArcStages.Add(new ArcStageDraft(Guid.Empty, Loc.T(title), kind, null));
        }
    }

    private static IReadOnlyList<string> ParseTags(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text
                .Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
