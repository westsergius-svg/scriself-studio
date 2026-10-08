namespace ScriSelfStudio.Contracts.Dtos;

using ScriSelfStudio.Core.Model;

/// <summary>
/// Неизменяемый снимок проекта для UI. Текст сцен в снимок не входит:
/// он загружается по требованию через <c>IProjectService.GetSceneDocument</c>.
/// </summary>
public sealed record ProjectDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required int SchemaVersion { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required string FilePath { get; init; }

    public required bool IsDirty { get; init; }

    /// <summary>Копия настроек проекта (тема, шрифт, автосохранение, резервные копии).</summary>
    public required ProjectSettings Settings { get; init; }

    public required IReadOnlyList<SeriesDto> Series { get; init; }

    /// <summary>Персонажи книги с карточками (ТЗ п. 27; ADR 010).</summary>
    public IReadOnlyList<CharacterDto> Characters { get; init; } = [];

    /// <summary>Сюжетные линии с шагами (ТЗ п. 27; ADR 010).</summary>
    public IReadOnlyList<PlotLineDto> PlotLines { get; init; } = [];

    /// <summary>Локации книги (макет «Локации/Объекты»; ТЗ п. 27).</summary>
    public IReadOnlyList<LocationDto> Locations { get; init; } = [];

    /// <summary>Сюжетно значимые объекты книги (макет «Локации/Объекты»; ТЗ п. 27).</summary>
    public IReadOnlyList<StoryObjectDto> StoryObjects { get; init; } = [];
}

/// <summary>Снимок серии книг.</summary>
public sealed record SeriesDto
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string Title { get; init; }

    public required int Order { get; init; }

    public required IReadOnlyList<BookDto> Books { get; init; }
}

/// <summary>Снимок книги.</summary>
public sealed record BookDto
{
    public required Guid Id { get; init; }

    public required Guid SeriesId { get; init; }

    public required string Title { get; init; }

    public required int Order { get; init; }

    /// <summary>Суммарный объём глав книги в словах (для карты книги).</summary>
    public required int Words { get; init; }

    public required IReadOnlyList<ChapterDto> Chapters { get; init; }
}

/// <summary>Снимок главы.</summary>
public sealed record ChapterDto
{
    public required Guid Id { get; init; }

    public required Guid BookId { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public required int Order { get; init; }

    /// <summary>Суммарный объём сцен главы в словах (для карты книги).</summary>
    public required int Words { get; init; }

    public required IReadOnlyList<SceneDto> Scenes { get; init; }
}

/// <summary>Снимок сцены без текста.</summary>
public sealed record SceneDto
{
    public required Guid Id { get; init; }

    public required Guid ChapterId { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public required SceneStatus Status { get; init; }

    public required int Order { get; init; }

    public required long Revision { get; init; }

    /// <summary>Объём текста сцены в словах. Текст в снимок не входит (ТЗ п. 22).</summary>
    public required int Words { get; init; }

    /// <summary>Ключевая сцена книги (ТЗ п. 27).</summary>
    public required bool IsKey { get; init; }

    /// <summary>Сцена заблокирована от правок: на карте помечается замком (ТЗ п. 27).</summary>
    public required bool IsLocked { get; init; }

    public required bool HasText { get; init; }

    /// <summary>Свободные заметки автора по сцене; хранятся в метаданных (ТЗ п. 27).</summary>
    public string? Notes { get; init; }
}
