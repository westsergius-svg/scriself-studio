namespace ScriSelfStudio.Core.Model;

/// <summary>Навигация по структуре проекта и поиск сущностей.</summary>
public static class ProjectGraphExtensions
{
    public static IEnumerable<Series> EnumerateSeries(this Project project, bool includeDeleted = false)
    {
        foreach (var series in project.Series)
        {
            if (includeDeleted || !series.IsDeleted)
            {
                yield return series;
            }
        }
    }

    public static IEnumerable<Book> EnumerateBooks(this Project project, bool includeDeleted = false)
    {
        foreach (var series in project.EnumerateSeries(includeDeleted))
        {
            foreach (var book in series.Books)
            {
                if (includeDeleted || !book.IsDeleted)
                {
                    yield return book;
                }
            }
        }
    }

    public static IEnumerable<Chapter> EnumerateChapters(this Project project, bool includeDeleted = false)
    {
        foreach (var book in project.EnumerateBooks(includeDeleted))
        {
            foreach (var chapter in book.Chapters)
            {
                if (includeDeleted || !chapter.IsDeleted)
                {
                    yield return chapter;
                }
            }
        }
    }

    public static IEnumerable<Scene> EnumerateScenes(this Project project, bool includeDeleted = false)
    {
        foreach (var chapter in project.EnumerateChapters(includeDeleted))
        {
            foreach (var scene in chapter.Scenes)
            {
                if (includeDeleted || !scene.IsDeleted)
                {
                    yield return scene;
                }
            }
        }
    }

    public static Series? FindSeries(this Project project, Guid seriesId, bool includeDeleted = false)
    {
        foreach (var series in project.EnumerateSeries(includeDeleted))
        {
            if (series.Id == seriesId)
            {
                return series;
            }
        }

        return null;
    }

    public static Book? FindBook(this Project project, Guid bookId, bool includeDeleted = false)
    {
        foreach (var book in project.EnumerateBooks(includeDeleted))
        {
            if (book.Id == bookId)
            {
                return book;
            }
        }

        return null;
    }

    public static Chapter? FindChapter(this Project project, Guid chapterId, bool includeDeleted = false)
    {
        foreach (var chapter in project.EnumerateChapters(includeDeleted))
        {
            if (chapter.Id == chapterId)
            {
                return chapter;
            }
        }

        return null;
    }

    public static Scene? FindScene(this Project project, Guid sceneId, bool includeDeleted = false)
    {
        foreach (var scene in project.EnumerateScenes(includeDeleted))
        {
            if (scene.Id == sceneId)
            {
                return scene;
            }
        }

        return null;
    }

    /// <summary>Глава, которой принадлежит сцена. Возвращает null, если сцена не найдена.</summary>
    public static Chapter? FindParentChapter(this Project project, Guid sceneId, bool includeDeleted = false)
    {
        foreach (var chapter in project.EnumerateChapters(includeDeleted))
        {
            foreach (var scene in chapter.Scenes)
            {
                if (scene.Id == sceneId)
                {
                    return chapter;
                }
            }
        }

        return null;
    }
}
