namespace ScriSelfStudio.BookArchitecture;

using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Model;

/// <summary>
/// Правила структуры книги: создание, переименование, удаление и порядок
/// серий, книг, глав и сцен (ТЗ п. 7, 16, 24). UI эти правила не дублирует.
/// </summary>
public sealed class BookArchitectureService : IBookArchitectureService
{
    public const int MaxTitleLength = 200;

    private readonly IProjectService _projects;
    private readonly ISystemClock _clock;

    public BookArchitectureService(IProjectService projects, ISystemClock? clock = null)
    {
        _projects = projects;
        _clock = clock ?? new SystemClock();
    }

    public IReadOnlyList<SeriesDto> GetSeries() => _projects.Current?.Series ?? [];

    public IReadOnlyList<BookDto> GetBooks() => _projects.Current is { } project
        ? [.. project.Series.SelectMany(series => series.Books)]
        : [];

    public IReadOnlyList<CharacterDto> GetCharacters() => _projects.Current?.Characters ?? [];

    public IReadOnlyList<PlotLineDto> GetPlotLines() => _projects.Current?.PlotLines ?? [];

    public IReadOnlyList<LocationDto> GetLocations() => _projects.Current?.Locations ?? [];

    public IReadOnlyList<StoryObjectDto> GetStoryObjects() => _projects.Current?.StoryObjects ?? [];

    /// <summary>
    /// Создать или обновить персонажа. Пустой <c>Id</c> — создание: карточка получает семь
    /// канонических этапов арки, которые можно переименовать или дополнить (ТЗ п. 27; ADR 010).
    /// </summary>
    public Task<Result<ProjectDto>> SaveCharacterAsync(CharacterDto character, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            ArgumentNullException.ThrowIfNull(character);

            var existingId = character.Id == Guid.Empty ? (Guid?)null : character.Id;
            var target = existingId is { } id ? project.FindCharacter(id) : null;
            if (existingId is not null && target is null)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Персонаж не найден.");
            }

            var name = ValidateNewTitle(character.Name, Titles(project.Characters, existingId));
            if (!name.IsSuccess)
            {
                return Result.Failure(name.Error!);
            }

            if (target is null)
            {
                target = ProjectFactory.CreateCharacter(project, name.Require(), _clock.UtcNow);
                project.Characters.Add(target);

                // Новый персонаж приходит из экрана без этапов арки: канонические семь
                // шагов, созданные фабрикой, сохраняются (ADR 010). Остальные поля
                // карточки (роль, цель, статус и т. д.) применяются как обычно, иначе
                // создание без шагов молча теряло бы роль и цель персонажа.
                if (character.ArcStages.Count == 0)
                {
                    ApplyCard(target, character, _clock.UtcNow, keepExistingArcStages: true);
                    return Result.Success;
                }
            }
            else
            {
                target.Name = name.Require();
            }

            ApplyCard(target, character, _clock.UtcNow);
            return Result.Success;
        }));

    public Task<Result<ProjectDto>> DeleteCharacterAsync(Guid characterId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            if (project.FindCharacter(characterId) is not { } character || character.IsDeleted)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Персонаж не найден.");
            }

            character.MarkDeleted(_clock.UtcNow);

            // Участие удалённого персонажа в шагах линий убирается: иначе этап ссылается в пустоту.
            foreach (var step in project.PlotLines.SelectMany(line => line.Steps))
            {
                step.Participants.RemoveAll(participant => participant.CharacterId == characterId);
            }

            return Result.Success;
        }));

    /// <summary>Создать или обновить линию вместе с этапами и участниками (ТЗ п. 27).</summary>
    public Task<Result<ProjectDto>> SavePlotLineAsync(PlotLineDto plotLine, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            ArgumentNullException.ThrowIfNull(plotLine);

            var existingId = plotLine.Id == Guid.Empty ? (Guid?)null : plotLine.Id;
            var target = existingId is { } id ? project.FindPlotLine(id) : null;
            if (existingId is not null && target is null)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Сюжетная линия не найдена.");
            }

            var name = ValidateNewTitle(plotLine.Name, Titles(project.PlotLines, existingId));
            if (!name.IsSuccess)
            {
                return Result.Failure(name.Error!);
            }

            if (target is null)
            {
                target = ProjectFactory.CreatePlotLine(project, name.Require(), _clock.UtcNow);
                target.ColorHex = plotLine.ColorHex;
                project.PlotLines.Add(target);
            }
            else
            {
                target.Name = name.Require();
                if (plotLine.ColorHex is { Length: > 0 } color)
                {
                    target.ColorHex = color;
                }
            }

            target.Description = plotLine.Description;
            target.Goal = plotLine.Goal;
            target.Status = plotLine.Status;
            target.Notes = plotLine.Notes;
            target.Audit.UpdatedAt = _clock.UtcNow;
            target.SceneIds.Clear();
            target.SceneIds.AddRange(plotLine.SceneIds.Where(sceneId => project.FindScene(sceneId) is not null));

            var steps = SyncSteps(project, target, plotLine, _clock.UtcNow);
            return steps.IsSuccess ? Result.Success : Result.Failure(steps.Error!);
        }));

    public Task<Result<ProjectDto>> DeletePlotLineAsync(Guid plotLineId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            if (project.FindPlotLine(plotLineId) is not { } plotLine || plotLine.IsDeleted)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Сюжетная линия не найдена.");
            }

            plotLine.MarkDeleted(_clock.UtcNow);
            return Result.Success;
        }));

    /// <summary>Создать или обновить локацию: пустой <c>Id</c> — создание (ТЗ п. 27).</summary>
    public Task<Result<ProjectDto>> SaveLocationAsync(LocationDto location, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            ArgumentNullException.ThrowIfNull(location);

            var existingId = location.Id == Guid.Empty ? (Guid?)null : location.Id;
            var target = existingId is { } id ? project.FindLocation(id) : null;
            if (existingId is not null && target is null)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Локация не найдена.");
            }

            var name = ValidateNewTitle(location.Name, Titles(project.Locations, existingId));
            if (!name.IsSuccess)
            {
                return Result.Failure(name.Error!);
            }

            if (target is null)
            {
                target = ProjectFactory.CreateLocation(project, name.Require(), _clock.UtcNow);
                project.Locations.Add(target);
            }
            else
            {
                target.Name = name.Require();
            }

            target.Description = location.Description;
            target.Notes = location.Notes;
            target.Audit.UpdatedAt = _clock.UtcNow;
            return Result.Success;
        }));

    public Task<Result<ProjectDto>> DeleteLocationAsync(Guid locationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            if (project.FindLocation(locationId) is not { } location || location.IsDeleted)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Локация не найдена.");
            }

            location.MarkDeleted(_clock.UtcNow);
            return Result.Success;
        }));

    /// <summary>Создать или обновить объект: пустой <c>Id</c> — создание (ТЗ п. 27).</summary>
    public Task<Result<ProjectDto>> SaveStoryObjectAsync(StoryObjectDto storyObject, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            ArgumentNullException.ThrowIfNull(storyObject);

            var existingId = storyObject.Id == Guid.Empty ? (Guid?)null : storyObject.Id;
            var target = existingId is { } id ? project.FindStoryObject(id) : null;
            if (existingId is not null && target is null)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Объект не найден.");
            }

            var name = ValidateNewTitle(storyObject.Name, Titles(project.StoryObjects, existingId));
            if (!name.IsSuccess)
            {
                return Result.Failure(name.Error!);
            }

            if (target is null)
            {
                target = ProjectFactory.CreateStoryObject(project, name.Require(), _clock.UtcNow);
                project.StoryObjects.Add(target);
            }
            else
            {
                target.Name = name.Require();
            }

            target.Description = storyObject.Description;
            target.Notes = storyObject.Notes;
            target.Audit.UpdatedAt = _clock.UtcNow;
            return Result.Success;
        }));

    public Task<Result<ProjectDto>> DeleteStoryObjectAsync(Guid storyObjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            if (project.FindStoryObject(storyObjectId) is not { } storyObject || storyObject.IsDeleted)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Объект не найден.");
            }

            storyObject.MarkDeleted(_clock.UtcNow);
            return Result.Success;
        }));

    /// <summary>Переносит поля карточки персонажа в модель (ADR 010).</summary>
    private static void ApplyCard(
        Character target,
        CharacterDto card,
        DateTimeOffset now,
        bool keepExistingArcStages = false)
    {
        target.Description = card.Description;
        target.Nickname = card.Nickname;
        target.Role = card.Role;
        target.Importance = card.Importance;
        target.Status = card.Status;
        target.Age = card.Age;
        target.Gender = card.Gender;
        target.Species = card.Species;
        target.Occupation = card.Occupation;
        target.World = card.World;
        target.Tags.Clear();
        target.Tags.AddRange(card.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)));
        target.Appearance = card.Appearance;
        target.Voice = card.Voice;
        target.Mannerisms = card.Mannerisms;
        target.AppearanceStates = card.AppearanceStates;
        target.References = card.References;
        target.Archetype = card.Archetype;
        target.Temperament = card.Temperament;
        target.Values = card.Values;
        target.Beliefs = card.Beliefs;
        target.FalseBelief = card.FalseBelief;
        target.InternalNeed = card.InternalNeed;
        target.Want = card.Want;
        target.Fears = card.Fears;
        target.Weaknesses = card.Weaknesses;
        target.Strengths = card.Strengths;
        target.Habits = card.Habits;
        target.Humour = card.Humour;
        target.DefenceMechanisms = card.DefenceMechanisms;
        target.CopingStrategies = card.CopingStrategies;
        target.ExternalGoal = card.ExternalGoal;
        target.SecretGoal = card.SecretGoal;
        target.Motivation = card.Motivation;
        target.ReadyToDo = card.ReadyToDo;
        target.NeverDo = card.NeverDo;
        target.Victory = card.Victory;
        target.Failure = card.Failure;
        target.Education = card.Education;
        target.Skills = card.Skills;
        target.Combat = card.Combat;
        target.Powers = card.Powers;
        target.Talents = card.Talents;
        target.Limitations = card.Limitations;
        target.Inventory = card.Inventory;
        target.Companions = card.Companions;
        target.Speech = card.Speech;
        target.Accent = card.Accent;
        target.SpeechHabits = card.SpeechHabits;
        target.SpeechAvoided = card.SpeechAvoided;
        target.SpeechUnderStress = card.SpeechUnderStress;
        target.Birthplace = card.Birthplace;
        target.Childhood = card.Childhood;
        target.Trauma = card.Trauma;
        target.Career = card.Career;
        target.TurningPoints = card.TurningPoints;
        target.StoryStart = card.StoryStart;
        target.Chronology = card.Chronology;
        target.ArcType = card.ArcType;
        target.ArcSummary = card.ArcSummary;
        target.Secrets = card.Secrets;
        target.SecretsFromSelf = card.SecretsFromSelf;
        target.SecretKeepers = card.SecretKeepers;
        target.SecretReveal = card.SecretReveal;
        target.SecretConsequences = card.SecretConsequences;
        target.PhysicalChange = card.PhysicalChange;
        target.PsychologicalChange = card.PsychologicalChange;
        target.RelationshipChange = card.RelationshipChange;
        target.KeyDecisions = card.KeyDecisions;
        target.Loses = card.Loses;
        target.Gains = card.Gains;
        target.SceneIdeas = card.SceneIdeas;
        target.Quotes = card.Quotes;
        target.OpenQuestions = card.OpenQuestions;
        target.Notes = card.Notes;
        target.DraftStatus = card.DraftStatus;
        target.Author = card.Author;
        target.Audit.UpdatedAt = now;

        if (!keepExistingArcStages)
        {
            SyncArcStages(target, card, now);
        }

        SyncRelations(target, card);
    }

    private static void SyncArcStages(Character character, CharacterDto card, DateTimeOffset now)
    {
        var kept = new HashSet<Guid>();
        var order = 0;
        foreach (var step in card.ArcStages)
        {
            if (string.IsNullOrWhiteSpace(step.Title))
            {
                continue;
            }

            var target = step.Id == Guid.Empty ? null : character.FindArcStage(step.Id);
            if (target is null)
            {
                target = ProjectFactory.CreateArcStage(character, step.Kind, step.Title.Trim(), order, now);
                character.ArcStages.Add(target);
            }
            else
            {
                target.Title = step.Title.Trim();
                target.Kind = step.Kind;
                target.Description = step.Description;
                target.Order = order;
                target.Audit.IsDeleted = false;
                target.Audit.UpdatedAt = now;
            }

            kept.Add(target.Id);
            order++;
        }

        foreach (var removed in character.ArcStages.Where(step => !kept.Contains(step.Id) && !step.IsDeleted))
        {
            removed.MarkDeleted(now);
        }
    }

    private static void SyncRelations(Character character, CharacterDto card)
    {
        character.Relations.Clear();
        foreach (var relation in card.Relations)
        {
            character.Relations.Add(new CharacterRelation
            {
                CharacterId = relation.CharacterId,
                Type = relation.Type,
                Dynamic = relation.Dynamic,
                Status = relation.Status,
            });
        }
    }

    /// <summary>Шаги линии: существующие обновляются по идентификатору, отсутствующие скрываются.</summary>
    private static Result SyncSteps(Project project, PlotLine line, PlotLineDto source, DateTimeOffset now)
    {
        var kept = new HashSet<Guid>();
        var order = 0;
        foreach (var step in source.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Title))
            {
                return Result.FromError(ErrorCode.ValidationFailed, "У шага линии должно быть название.");
            }

            var target = step.Id == Guid.Empty ? null : line.Steps.FirstOrDefault(item => item.Id == step.Id);
            if (target is null)
            {
                target = ProjectFactory.CreatePlotLineStep(line, step.Title.Trim(), order, now);
                line.Steps.Add(target);
            }
            else
            {
                target.Title = step.Title.Trim();
                target.Audit.IsDeleted = false;
                target.Audit.UpdatedAt = now;
            }

            target.Description = step.Description;
            target.IsFinal = step.IsFinal;
            target.Icon = step.Icon;
            target.Order = order;
            target.Participants.Clear();
            foreach (var participant in step.Participants)
            {
                if (project.FindCharacter(participant.CharacterId) is null)
                {
                    return Result.FromError(ErrorCode.EntityNotFound, "Персонаж шага не найден.");
                }

                target.Participants.Add(new StepParticipant
                {
                    CharacterId = participant.CharacterId,
                    CharacterArcStageId = participant.CharacterArcStageId,
                });
            }

            target.SceneIds.Clear();
            target.SceneIds.AddRange(step.SceneIds.Where(sceneId => project.FindScene(sceneId) is not null));
            kept.Add(target.Id);
            order++;
        }

        foreach (var removed in line.Steps.Where(step => !kept.Contains(step.Id) && !step.IsDeleted))
        {
            removed.MarkDeleted(now);
        }

        return Result.Success;
    }

    public Task<Result<ProjectDto>> CreateSeriesAsync(CreateSeriesRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            var title = ValidateNewTitle(request.Title, Titles(project.Series, exceptId: null));
            if (!title.IsSuccess)
            {
                return Result.Failure(title.Error!);
            }

            project.Series.Add(ProjectFactory.CreateSeries(project, title.Require(), _clock.UtcNow));
            return Result.Success;
        }));

    public Task<Result<ProjectDto>> CreateBookAsync(CreateBookRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            var series = project.FindSeries(request.SeriesId);
            if (series is null)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Серия не найдена.");
            }

            var title = ValidateNewTitle(request.Title, Titles(series.Books, exceptId: null));
            if (!title.IsSuccess)
            {
                return Result.Failure(title.Error!);
            }

            series.Books.Add(ProjectFactory.CreateBook(project, series, title.Require(), _clock.UtcNow));
            return Result.Success;
        }));

    public Task<Result<ProjectDto>> CreateChapterAsync(CreateChapterRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            var book = project.FindBook(request.BookId);
            if (book is null)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Книга не найдена.");
            }

            var title = ValidateNewTitle(request.Title, Titles(book.Chapters, exceptId: null));
            if (!title.IsSuccess)
            {
                return Result.Failure(title.Error!);
            }

            book.Chapters.Add(ProjectFactory.CreateChapter(book, title.Require(), request.Description, _clock.UtcNow));
            return Result.Success;
        }));

    public Task<Result<ProjectDto>> CreateSceneAsync(CreateSceneRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            var chapter = project.FindChapter(request.ChapterId);
            if (chapter is null)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Глава не найдена.");
            }

            var title = ValidateNewTitle(request.Title, Titles(chapter.Scenes, exceptId: null));
            if (!title.IsSuccess)
            {
                return Result.Failure(title.Error!);
            }

            chapter.Scenes.Add(ProjectFactory.CreateScene(chapter, title.Require(), _clock.UtcNow));
            return Result.Success;
        }));

    public Task<Result<ProjectDto>> RenameEntityAsync(RenameEntityRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            var entity = FindEntity(project, request.Kind, request.Id);
            if (entity is null)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Элемент не найден.");
            }

            var title = ValidateNewTitle(request.Title, SiblingTitles(project, request.Kind, entity));
            if (!title.IsSuccess)
            {
                return Result.Failure(title.Error!);
            }

            SetTitle(entity, title.Require());
            entity.Touch(_clock.UtcNow);
            return Result.Success;
        }));

    public Task<Result<ProjectDto>> DeleteEntityAsync(DeleteEntityRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            var entity = FindEntity(project, request.Kind, request.Id);
            if (entity is null)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Элемент не найден.");
            }

            entity.MarkDeleted(_clock.UtcNow);
            Reindex(project, request.Kind, entity);
            return Result.Success;
        }));

    public Task<Result<ProjectDto>> MoveEntityAsync(MoveEntityRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Mutate(project =>
        {
            var entity = FindEntity(project, request.Kind, request.Id);
            if (entity is null)
            {
                return Result.FromError(ErrorCode.EntityNotFound, "Элемент не найден.");
            }

            var moved = MoveWithinParent(project, request.Kind, entity, request.NewOrder);
            if (!moved.IsSuccess)
            {
                return moved;
            }

            entity.Touch(_clock.UtcNow);
            return Result.Success;
        }));

    public Result ValidateTitle(EntityKind kind, string? title) =>
        ValidateTitleFormat(title) is { IsSuccess: false } failure
            ? Result.Failure(failure.Error!)
            : Result.Success;

    private static Result MoveWithinParent(Project project, EntityKind kind, ProjectEntity entity, int newOrder)
    {
        switch (kind)
        {
            case EntityKind.Series:
                return MoveWithin(project.Series, (Series)entity, newOrder);
            case EntityKind.Book:
                return project.FindSeries(((Book)entity).SeriesId) is { } series
                    ? MoveWithin(series.Books, (Book)entity, newOrder)
                    : Result.FromError(ErrorCode.EntityNotFound, "Серия не найдена.");
            case EntityKind.Chapter:
                return project.FindBook(((Chapter)entity).BookId) is { } book
                    ? MoveWithin(book.Chapters, (Chapter)entity, newOrder)
                    : Result.FromError(ErrorCode.EntityNotFound, "Книга не найдена.");
            case EntityKind.Scene:
                return project.FindChapter(((Scene)entity).ChapterId) is { } chapter
                    ? MoveWithin(chapter.Scenes, (Scene)entity, newOrder)
                    : Result.FromError(ErrorCode.EntityNotFound, "Глава не найдена.");
            default:
                return Result.FromError(ErrorCode.UnsupportedOperation, "Для этого элемента порядок не поддерживается.");
        }
    }

    private static Result MoveWithin<TEntity>(List<TEntity> items, TEntity entity, int newOrder)
        where TEntity : ProjectEntity
    {
        if (!items.Remove(entity))
        {
            return Result.FromError(ErrorCode.EntityNotFound, "Элемент не найден в списке.");
        }

        var target = Math.Clamp(newOrder, 0, items.Count);
        items.Insert(target, entity);
        for (var index = 0; index < items.Count; index++)
        {
            items[index].Order = index;
        }

        return Result.Success;
    }

    /// <summary>Плотная нумерация порядка после удаления.</summary>
    private static void Reindex(Project project, EntityKind kind, ProjectEntity entity)
    {
        switch (kind)
        {
            case EntityKind.Series:
                ReindexLevel(project.Series);
                break;
            case EntityKind.Book:
                if (project.FindSeries(((Book)entity).SeriesId) is { } series)
                {
                    ReindexLevel(series.Books);
                }

                break;
            case EntityKind.Chapter:
                if (project.FindBook(((Chapter)entity).BookId) is { } book)
                {
                    ReindexLevel(book.Chapters);
                }

                break;
            case EntityKind.Scene:
                if (project.FindChapter(((Scene)entity).ChapterId) is { } chapter)
                {
                    ReindexLevel(chapter.Scenes);
                }

                break;
            default:
                break;
        }
    }

    private static void ReindexLevel<TEntity>(List<TEntity> items)
        where TEntity : ProjectEntity
    {
        var order = 0;
        foreach (var item in items.Where(item => !item.IsDeleted))
        {
            item.Order = order++;
        }
    }

    private static IEnumerable<string> SiblingTitles(Project project, EntityKind kind, ProjectEntity entity)
    {
        switch (kind)
        {
            case EntityKind.Series:
                return Titles(project.Series, entity.Id);
            case EntityKind.Book:
                return project.FindSeries(((Book)entity).SeriesId) is { } series ? Titles(series.Books, entity.Id) : [];
            case EntityKind.Chapter:
                return project.FindBook(((Chapter)entity).BookId) is { } book ? Titles(book.Chapters, entity.Id) : [];
            case EntityKind.Scene:
                return project.FindChapter(((Scene)entity).ChapterId) is { } chapter ? Titles(chapter.Scenes, entity.Id) : [];
            default:
                return [];
        }
    }

    private static IEnumerable<string> Titles(IEnumerable<ProjectEntity> items, Guid? exceptId) =>
        items.Where(item => !item.IsDeleted && (exceptId is null || item.Id != exceptId)).Select(GetTitle);

    private static Result<string> ValidateTitleFormat(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result<string>.FromError(ErrorCode.ValidationFailed, "Укажите название.");
        }

        if (trimmed.Length > MaxTitleLength)
        {
            return Result<string>.FromError(
                ErrorCode.ValidationFailed,
                $"Название длиннее {MaxTitleLength} символов.");
        }

        return Result<string>.Success(trimmed);
    }

    private static Result<string> ValidateNewTitle(string? title, IEnumerable<string> siblingTitles)
    {
        var format = ValidateTitleFormat(title);
        if (!format.IsSuccess)
        {
            return format;
        }

        var candidate = format.Require();
        foreach (var siblingTitle in siblingTitles)
        {
            if (string.Equals(siblingTitle, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return Result<string>.FromError(
                    ErrorCode.EntityAlreadyExists,
                    $"Элемент с названием «{candidate}» уже существует.");
            }
        }

        return Result<string>.Success(candidate);
    }

    private static ProjectEntity? FindEntity(Project project, EntityKind kind, Guid id) => kind switch
    {
        EntityKind.Series => project.FindSeries(id),
        EntityKind.Book => project.FindBook(id),
        EntityKind.Chapter => project.FindChapter(id),
        EntityKind.Scene => project.FindScene(id),
        EntityKind.Character => project.FindCharacter(id),
        EntityKind.PlotLine => project.FindPlotLine(id),
        EntityKind.PlotLineStep => project.FindPlotLineStep(id),
        _ => null,
    };

    private static string GetTitle(ProjectEntity entity) => entity switch
    {
        Series series => series.Title,
        Book book => book.Title,
        Chapter chapter => chapter.Title,
        Scene scene => scene.Title,
        Character character => character.Name,
        PlotLine plotLine => plotLine.Name,
        PlotLineStep step => step.Title,
        Location location => location.Name,
        StoryObject storyObject => storyObject.Name,
        _ => string.Empty,
    };

    private static void SetTitle(ProjectEntity entity, string title)
    {
        switch (entity)
        {
            case Series series:
                series.Title = title;
                break;
            case Book book:
                book.Title = title;
                break;
            case Chapter chapter:
                chapter.Title = title;
                break;
            case Scene scene:
                scene.Title = title;
                break;
            case Character character:
                character.Name = title;
                break;
            case PlotLine plotLine:
                plotLine.Name = title;
                break;
            case PlotLineStep step:
                step.Title = title;
                break;
            case Location location:
                location.Name = title;
                break;
            case StoryObject storyObject:
                storyObject.Name = title;
                break;
            default:
                break;
        }
    }
}
