namespace ScriSelfStudio.Contracts.Dtos;

using ScriSelfStudio.Core.Model;

/// <summary>
/// Единственное место преобразования доменной модели в снимки для UI.
/// Дублирование моделей запрещено (ТЗ п. 12), поэтому преобразование собрано здесь.
/// </summary>
public static class SnapshotMapper
{
    public static ProjectDto ToDto(this Project project, string filePath, bool isDirty) => new()
    {
        Id = project.Id,
        Name = project.Name,
        Description = project.Description,
        SchemaVersion = project.SchemaVersion,
        CreatedAt = project.Audit.CreatedAt,
        UpdatedAt = project.Audit.UpdatedAt,
        FilePath = filePath,
        IsDirty = isDirty,
        Settings = project.Settings.Clone(),
        Series = project.EnumerateSeries().Select(ToDto).ToList(),
        Characters = project.Characters.Where(character => !character.IsDeleted).Select(ToDto).ToList(),
        PlotLines = project.PlotLines.Where(line => !line.IsDeleted).OrderBy(line => line.Order).Select(ToDto).ToList(),
        Locations = project.Locations.Where(location => !location.IsDeleted).Select(ToDto).ToList(),
        StoryObjects = project.StoryObjects.Where(item => !item.IsDeleted).Select(ToDto).ToList(),
    };

    public static LocationDto ToDto(this Location location) => new()
    {
        Id = location.Id,
        Name = location.Name,
        Description = location.Description,
        Notes = location.Notes,
        Order = location.Order,
    };

    public static StoryObjectDto ToDto(this StoryObject storyObject) => new()
    {
        Id = storyObject.Id,
        Name = storyObject.Name,
        Description = storyObject.Description,
        Notes = storyObject.Notes,
        Order = storyObject.Order,
    };

    public static CharacterDto ToDto(this Character character) => new()
    {
        Id = character.Id,
        Name = character.Name,
        Description = character.Description,
        Order = character.Order,
        Nickname = character.Nickname,
        Role = character.Role,
        Importance = character.Importance,
        Status = character.Status,
        Age = character.Age,
        Gender = character.Gender,
        Species = character.Species,
        Occupation = character.Occupation,
        World = character.World,
        Tags = [.. character.Tags],
        Appearance = character.Appearance,
        Voice = character.Voice,
        Mannerisms = character.Mannerisms,
        AppearanceStates = character.AppearanceStates,
        References = character.References,
        Archetype = character.Archetype,
        Temperament = character.Temperament,
        Values = character.Values,
        Beliefs = character.Beliefs,
        FalseBelief = character.FalseBelief,
        InternalNeed = character.InternalNeed,
        Want = character.Want,
        Fears = character.Fears,
        Weaknesses = character.Weaknesses,
        Strengths = character.Strengths,
        Habits = character.Habits,
        Humour = character.Humour,
        DefenceMechanisms = character.DefenceMechanisms,
        CopingStrategies = character.CopingStrategies,
        ExternalGoal = character.ExternalGoal,
        SecretGoal = character.SecretGoal,
        Motivation = character.Motivation,
        ReadyToDo = character.ReadyToDo,
        NeverDo = character.NeverDo,
        Victory = character.Victory,
        Failure = character.Failure,
        Education = character.Education,
        Skills = character.Skills,
        Combat = character.Combat,
        Powers = character.Powers,
        Talents = character.Talents,
        Limitations = character.Limitations,
        Inventory = character.Inventory,
        Companions = character.Companions,
        Speech = character.Speech,
        Accent = character.Accent,
        SpeechHabits = character.SpeechHabits,
        SpeechAvoided = character.SpeechAvoided,
        SpeechUnderStress = character.SpeechUnderStress,
        Birthplace = character.Birthplace,
        Childhood = character.Childhood,
        Trauma = character.Trauma,
        Career = character.Career,
        TurningPoints = character.TurningPoints,
        StoryStart = character.StoryStart,
        Chronology = character.Chronology,
        ArcType = character.ArcType,
        ArcSummary = character.ArcSummary,
        Secrets = character.Secrets,
        SecretsFromSelf = character.SecretsFromSelf,
        SecretKeepers = character.SecretKeepers,
        SecretReveal = character.SecretReveal,
        SecretConsequences = character.SecretConsequences,
        PhysicalChange = character.PhysicalChange,
        PsychologicalChange = character.PsychologicalChange,
        RelationshipChange = character.RelationshipChange,
        KeyDecisions = character.KeyDecisions,
        Loses = character.Loses,
        Gains = character.Gains,
        SceneIdeas = character.SceneIdeas,
        Quotes = character.Quotes,
        OpenQuestions = character.OpenQuestions,
        Notes = character.Notes,
        DraftStatus = character.DraftStatus,
        Author = character.Author,
        Relations =
        [
            .. character.Relations.Select(relation => new CharacterRelationDto
            {
                CharacterId = relation.CharacterId,
                Type = relation.Type,
                Dynamic = relation.Dynamic,
                Status = relation.Status,
            }),
        ],
        ArcStages =
        [
            .. character.ArcStages
                .Where(step => !step.IsDeleted)
                .OrderBy(step => step.Order)
                .Select(step => new CharacterArcStageDto
                {
                    Id = step.Id,
                    Title = step.Title,
                    Description = step.Description,
                    Order = step.Order,
                    Kind = step.Kind,
                }),
        ],
    };

    public static PlotLineDto ToDto(this PlotLine plotLine) => new()
    {
        Id = plotLine.Id,
        Name = plotLine.Name,
        Description = plotLine.Description,
        Goal = plotLine.Goal,
        Status = plotLine.Status,
        Notes = plotLine.Notes,
        ColorHex = plotLine.ColorHex,
        Order = plotLine.Order,
        SceneIds = [.. plotLine.SceneIds],
        Steps =
        [
            .. plotLine.Steps
                .Where(step => !step.IsDeleted)
                .OrderBy(step => step.Order)
                .Select(step => new PlotLineStepDto
                {
                    Id = step.Id,
                    Title = step.Title,
                    Description = step.Description,
                    Order = step.Order,
                    IsFinal = step.IsFinal,
                    Icon = step.Icon,
                    SceneIds = [.. step.SceneIds],
                    Participants =
                    [
                        .. step.Participants.Select(participant => new StepParticipantDto
                        {
                            CharacterId = participant.CharacterId,
                            CharacterArcStageId = participant.CharacterArcStageId,
                        }),
                    ],
                }),
        ],
    };

    public static SeriesDto ToDto(this Series series) => new()
    {
        Id = series.Id,
        ProjectId = series.ProjectId,
        Title = series.Title,
        Order = series.Order,
        Books = series.Books.Where(book => !book.IsDeleted).Select(ToDto).ToList(),
    };

    public static BookDto ToDto(this Book book) => new()
    {
        Id = book.Id,
        SeriesId = book.SeriesId,
        Title = book.Title,
        Order = book.Order,
        Words = book.Chapters.Where(chapter => !chapter.IsDeleted).Sum(ChapterWords),
        Chapters = book.Chapters.Where(chapter => !chapter.IsDeleted).Select(ToDto).ToList(),
    };

    public static ChapterDto ToDto(this Chapter chapter) => new()
    {
        Id = chapter.Id,
        BookId = chapter.BookId,
        Title = chapter.Title,
        Description = chapter.Description,
        Order = chapter.Order,
        Words = ChapterWords(chapter),
        Scenes = chapter.Scenes.Where(scene => !scene.IsDeleted).Select(ToDto).ToList(),
    };

    public static SceneDto ToDto(this Scene scene) => new()
    {
        Id = scene.Id,
        ChapterId = scene.ChapterId,
        Title = scene.Title,
        Description = scene.Description,
        Status = scene.Status,
        Order = scene.Order,
        Revision = scene.Revision,
        Words = scene.Words,
        IsKey = scene.Metadata.IsKey,
        IsLocked = scene.Metadata.IsLocked,
        HasText = !scene.Document.IsEmpty(),
        Notes = scene.Metadata.Notes,
        PlotLineStepOrder = [.. scene.Metadata.PlotLineStepOrder],
    };

    private static int ChapterWords(Chapter chapter) =>
        chapter.Scenes.Where(scene => !scene.IsDeleted).Sum(scene => scene.Words);
}
