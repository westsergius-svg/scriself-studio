namespace ScriSelfStudio.Core;

using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Schema;
using ScriSelfStudio.Core.Text;

/// <summary>Создание новых объектов проекта по умолчанию.</summary>
public static class ProjectFactory
{
    public static Project CreateProject(string name, string? description = null, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var project = new Project
        {
            Name = name,
            Description = description,
            SchemaVersion = SchemaVersion.Current,
        };

        project.Audit.CreatedAt = timestamp;
        project.Audit.UpdatedAt = timestamp;
        return project;
    }

    public static Series CreateSeries(Project project, string title, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var series = new Series
        {
            ProjectId = project.Id,
            Title = title,
            Order = project.Series.Count,
        };

        series.Audit.CreatedAt = timestamp;
        series.Audit.UpdatedAt = timestamp;
        return series;
    }

    public static Book CreateBook(Project project, Series series, string title, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var book = new Book
        {
            ProjectId = project.Id,
            SeriesId = series.Id,
            Title = title,
            Order = series.Books.Count,
        };

        book.Audit.CreatedAt = timestamp;
        book.Audit.UpdatedAt = timestamp;
        return book;
    }

    public static Chapter CreateChapter(Book book, string title, string? description = null, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var chapter = new Chapter
        {
            BookId = book.Id,
            Title = title,
            Description = description,
            Order = book.Chapters.Count,
        };

        chapter.Audit.CreatedAt = timestamp;
        chapter.Audit.UpdatedAt = timestamp;
        return chapter;
    }

    public static Scene CreateScene(Chapter chapter, string title, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var scene = new Scene
        {
            ChapterId = chapter.Id,
            Title = title,
            Order = chapter.Scenes.Count,
            Document = RichDocument.CreateEmpty(),
        };

        scene.Audit.CreatedAt = timestamp;
        scene.Audit.UpdatedAt = timestamp;
        return scene;
    }

    /// <summary>
    /// Новый персонаж. Карточка сразу получает семь канонических этапов арки: автор
    /// переименовывает, дополняет или удаляет их (ТЗ п. 27; ADR 010).
    /// </summary>
    public static Character CreateCharacter(Project project, string name, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var character = new Character
        {
            ProjectId = project.Id,
            Name = name,
            Order = project.Characters.Count,
        };

        character.Audit.CreatedAt = timestamp;
        character.Audit.UpdatedAt = timestamp;

        var order = 0;
        foreach (var (kind, title) in CharacterArcStage.DefaultStages)
        {
            character.ArcStages.Add(CreateArcStage(character, kind, title, order++, timestamp));
        }

        return character;
    }

    public static CharacterArcStage CreateArcStage(
        Character character,
        CharacterArcStageKind kind,
        string title,
        int order,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(character);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var step = new CharacterArcStage
        {
            CharacterId = character.Id,
            Kind = kind,
            Title = title,
            Order = order,
        };

        step.Audit.CreatedAt = timestamp;
        step.Audit.UpdatedAt = timestamp;
        return step;
    }

    public static PlotLine CreatePlotLine(Project project, string name, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var plotLine = new PlotLine
        {
            ProjectId = project.Id,
            Name = name,
            Order = project.PlotLines.Count,
        };

        plotLine.Audit.CreatedAt = timestamp;
        plotLine.Audit.UpdatedAt = timestamp;
        return plotLine;
    }

    public static PlotLineStep CreatePlotLineStep(
        PlotLine plotLine,
        string title,
        int order,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(plotLine);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var step = new PlotLineStep
        {
            PlotLineId = plotLine.Id,
            Title = title,
            Order = order,
        };

        step.Audit.CreatedAt = timestamp;
        step.Audit.UpdatedAt = timestamp;
        return step;
    }

    /// <summary>Новая локация книги (макет «Локации/Объекты»).</summary>
    public static Location CreateLocation(Project project, string name, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var location = new Location
        {
            ProjectId = project.Id,
            Name = name,
            Order = project.Locations.Count,
        };

        location.Audit.CreatedAt = timestamp;
        location.Audit.UpdatedAt = timestamp;
        return location;
    }

    /// <summary>Новый сюжетно значимый объект книги (макет «Локации/Объекты»).</summary>
    public static StoryObject CreateStoryObject(Project project, string name, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var storyObject = new StoryObject
        {
            ProjectId = project.Id,
            Name = name,
            Order = project.StoryObjects.Count,
        };

        storyObject.Audit.CreatedAt = timestamp;
        storyObject.Audit.UpdatedAt = timestamp;
        return storyObject;
    }
}
