namespace ScriSelfStudio.Contracts.Books;

using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Core.Model;

/// <summary>Вид сущности структуры книги — используется командами (ТЗ п. 24).</summary>
public enum EntityKind
{
    Series = 0,
    Book = 1,
    Chapter = 2,
    Scene = 3,
    Character = 4,
    PlotLine = 5,
    PlotLineStep = 6,
}

public sealed record CreateSeriesRequest
{
    public required string Title { get; init; }
}

public sealed record CreateBookRequest
{
    public required Guid SeriesId { get; init; }

    public required string Title { get; init; }
}

public sealed record CreateChapterRequest
{
    public required Guid BookId { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }
}

public sealed record CreateSceneRequest
{
    public required Guid ChapterId { get; init; }

    public required string Title { get; init; }
}

public sealed record RenameEntityRequest
{
    public required EntityKind Kind { get; init; }

    public required Guid Id { get; init; }

    public required string Title { get; init; }
}

public sealed record DeleteEntityRequest
{
    public required EntityKind Kind { get; init; }

    public required Guid Id { get; init; }
}

public sealed record MoveEntityRequest
{
    public required EntityKind Kind { get; init; }

    public required Guid Id { get; init; }

    public required int NewOrder { get; init; }
}

/// <summary>
/// Операции над структурой книги (ТЗ п. 7, 24). Правила порядка и валидации
/// живут только здесь; UI их не дублирует.
/// </summary>
public interface IBookArchitectureService
{
    IReadOnlyList<BookDto> GetBooks();

    IReadOnlyList<SeriesDto> GetSeries();

    /// <summary>Персонажи проекта для карточек и выбора на шагах линий (ТЗ п. 27).</summary>
    IReadOnlyList<CharacterDto> GetCharacters();

    /// <summary>Сюжетные линии проекта с шагами (ТЗ п. 27).</summary>
    IReadOnlyList<PlotLineDto> GetPlotLines();

    /// <summary>Локации проекта (макет «Локации/Объекты»; ТЗ п. 27).</summary>
    IReadOnlyList<LocationDto> GetLocations();

    /// <summary>Сюжетно значимые объекты проекта (макет «Локации/Объекты»; ТЗ п. 27).</summary>
    IReadOnlyList<StoryObjectDto> GetStoryObjects();

    /// <summary>
    /// Создать или обновить персонажа: пустой <c>Id</c> — создание, иначе обновление карточки
    /// целиком (ТЗ п. 27; ADR 010).
    /// </summary>
    Task<Result<ProjectDto>> SaveCharacterAsync(CharacterDto character, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> DeleteCharacterAsync(Guid characterId, CancellationToken cancellationToken = default);

    /// <summary>Создать или обновить линию вместе с её этапами и участниками.</summary>
    Task<Result<ProjectDto>> SavePlotLineAsync(PlotLineDto plotLine, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> DeletePlotLineAsync(Guid plotLineId, CancellationToken cancellationToken = default);

    /// <summary>Создать или обновить локацию: пустой <c>Id</c> — создание (ТЗ п. 27).</summary>
    Task<Result<ProjectDto>> SaveLocationAsync(LocationDto location, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> DeleteLocationAsync(Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>Создать или обновить объект: пустой <c>Id</c> — создание (ТЗ п. 27).</summary>
    Task<Result<ProjectDto>> SaveStoryObjectAsync(StoryObjectDto storyObject, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> DeleteStoryObjectAsync(Guid storyObjectId, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> CreateSeriesAsync(CreateSeriesRequest request, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> CreateBookAsync(CreateBookRequest request, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> CreateChapterAsync(CreateChapterRequest request, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> CreateSceneAsync(CreateSceneRequest request, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> RenameEntityAsync(RenameEntityRequest request, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> DeleteEntityAsync(DeleteEntityRequest request, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> MoveEntityAsync(MoveEntityRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверка названия без изменения данных — используется UI до вызова команды.
    /// Проверяет формат; уникальность проверяется командой создания, когда известен родитель.
    /// </summary>
    Result ValidateTitle(EntityKind kind, string? title);
}
