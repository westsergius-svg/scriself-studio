namespace ScriSelfStudio.Persistence.Sqlite;

using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core.Json;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;

/// <summary>Чтение проекта из файла. Полный граф собирается в памяти.</summary>
internal static class SqliteProjectReader
{
    public static ProjectLoadResult Read(SqliteConnection connection, int sourceVersion, List<string> warnings)
    {
        var project = ReadProject(connection);
        project.SchemaVersion = sourceVersion;

        var series = ReadSeries(connection, project);
        var books = ReadBooks(connection, series);
        var chapters = ReadChapters(connection, books);
        var documents = ReadSceneDocuments(connection);
        ReadScenes(connection, chapters, documents, warnings);
        ReadPlotLines(connection, project);
        ReadCharacters(connection, project);
        ReadPlotLineSteps(connection, project);
        ReadLocations(connection, project);
        ReadStoryObjects(connection, project);

        return new ProjectLoadResult
        {
            Project = project,
            SourceSchemaVersion = sourceVersion,
            Warnings = warnings,
        };
    }

    private static Project ReadProject(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name, description, schema_version, created_at, updated_at, settings_json " +
            "FROM project LIMIT 1;";

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidDataException("В файле проекта отсутствует запись проекта.");
        }

        var row = new RowReader(reader);
        var project = new Project
        {
            Id = row.GetGuid("id"),
            Name = row.GetString("name"),
            Description = row.GetStringOrNull("description"),
            SchemaVersion = row.GetInt("schema_version"),
            Settings = Deserialize(row.GetString("settings_json"), CoreJsonContext.Default.ProjectSettings) ?? new ProjectSettings(),
        };

        project.Audit.CreatedAt = row.GetTimestamp("created_at");
        project.Audit.UpdatedAt = row.GetTimestamp("updated_at");
        return project;
    }

    private static Dictionary<Guid, Series> ReadSeries(SqliteConnection connection, Project project)
    {
        var result = new Dictionary<Guid, Series>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, project_id, title, order_index, created_at, updated_at, is_deleted, deleted_at " +
            "FROM series WHERE project_id = $projectId ORDER BY order_index, title;";
        command.Parameters.AddWithValue("$projectId", SqlValue.ToDb(project.Id));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new RowReader(reader);
            var series = new Series
            {
                Id = row.GetGuid("id"),
                ProjectId = row.GetGuid("project_id"),
                Title = row.GetString("title"),
                Order = row.GetInt("order_index"),
            };

            ReadAudit(row, series);
            project.Series.Add(series);
            result[series.Id] = series;
        }

        return result;
    }

    private static Dictionary<Guid, Book> ReadBooks(SqliteConnection connection, Dictionary<Guid, Series> series)
    {
        var result = new Dictionary<Guid, Book>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, project_id, series_id, title, order_index, created_at, updated_at, is_deleted, deleted_at " +
            "FROM book ORDER BY order_index, title;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new RowReader(reader);
            var book = new Book
            {
                Id = row.GetGuid("id"),
                ProjectId = row.GetGuid("project_id"),
                SeriesId = row.GetGuid("series_id"),
                Title = row.GetString("title"),
                Order = row.GetInt("order_index"),
            };

            ReadAudit(row, book);
            if (series.TryGetValue(book.SeriesId, out var parent))
            {
                parent.Books.Add(book);
                result[book.Id] = book;
            }
        }

        return result;
    }

    private static Dictionary<Guid, Chapter> ReadChapters(SqliteConnection connection, Dictionary<Guid, Book> books)
    {
        var result = new Dictionary<Guid, Chapter>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, book_id, title, description, order_index, created_at, updated_at, is_deleted, deleted_at " +
            "FROM chapter ORDER BY order_index, title;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new RowReader(reader);
            var chapter = new Chapter
            {
                Id = row.GetGuid("id"),
                BookId = row.GetGuid("book_id"),
                Title = row.GetString("title"),
                Description = row.GetStringOrNull("description"),
                Order = row.GetInt("order_index"),
            };

            ReadAudit(row, chapter);
            if (books.TryGetValue(chapter.BookId, out var parent))
            {
                parent.Chapters.Add(chapter);
                result[chapter.Id] = chapter;
            }
        }

        return result;
    }

    private static Dictionary<Guid, RichDocument> ReadSceneDocuments(SqliteConnection connection)
    {
        var result = new Dictionary<Guid, RichDocument>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT scene_id, document_json FROM scene_document;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new RowReader(reader);
            var document = Deserialize(row.GetString("document_json"), CoreJsonContext.Default.RichDocument);
            if (document is not null)
            {
                result[row.GetGuid("scene_id")] = document;
            }
        }

        return result;
    }

    private static void ReadScenes(
        SqliteConnection connection,
        Dictionary<Guid, Chapter> chapters,
        Dictionary<Guid, RichDocument> documents,
        List<string> warnings)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, chapter_id, title, description, status, order_index, revision, metadata_json, " +
            "created_at, updated_at, is_deleted, deleted_at FROM scene ORDER BY order_index, title;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new RowReader(reader);
            var scene = new Scene
            {
                Id = row.GetGuid("id"),
                ChapterId = row.GetGuid("chapter_id"),
                Title = row.GetString("title"),
                Description = row.GetStringOrNull("description"),
                Status = (SceneStatus)row.GetInt("status"),
                Order = row.GetInt("order_index"),
                Revision = row.GetLong("revision"),
                Metadata = Deserialize(row.GetString("metadata_json"), CoreJsonContext.Default.SceneMetadata) ?? new SceneMetadata(),
            };

            ReadAudit(row, scene);
            if (documents.TryGetValue(scene.Id, out var document))
            {
                scene.Document = document;
            }
            else
            {
                scene.Document = RichDocument.CreateEmpty();
                warnings.Add($"Сцена «{scene.Title}» не содержит текста: создан пустой документ.");
            }

            if (chapters.TryGetValue(scene.ChapterId, out var chapter))
            {
                chapter.Scenes.Add(scene);
            }
            else
            {
                warnings.Add($"Сцена «{scene.Title}» ссылается на отсутствующую главу.");
            }
        }
    }

    private static void ReadPlotLines(SqliteConnection connection, Project project)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, project_id, name, description, goal, status, notes, color_hex, order_index, scene_ids_json, " +
            "created_at, updated_at, is_deleted, deleted_at FROM plot_line WHERE project_id = $projectId " +
            "ORDER BY order_index, name;";
        command.Parameters.AddWithValue("$projectId", SqlValue.ToDb(project.Id));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new RowReader(reader);
            var plotLine = new PlotLine
            {
                Id = row.GetGuid("id"),
                ProjectId = row.GetGuid("project_id"),
                Name = row.GetString("name"),
                Description = row.GetStringOrNull("description"),
                Goal = row.GetStringOrNull("goal"),
                Status = ReadPlotLineStatus(row.GetInt("status")),
                Notes = row.GetStringOrNull("notes"),
                ColorHex = row.GetStringOrNull("color_hex"),
                Order = row.GetInt("order_index"),
            };

            var sceneIds = Deserialize(row.GetString("scene_ids_json"), CoreJsonContext.Default.ListGuid);
            if (sceneIds is not null)
            {
                plotLine.SceneIds.AddRange(sceneIds);
            }

            ReadAudit(row, plotLine);
            project.PlotLines.Add(plotLine);
        }
    }

    /// <summary>
    /// Статус линии из файла: неизвестное число не должно ломать открытие проекта,
    /// поэтому значение вне перечисления приводится к «В работе».
    /// </summary>
    private static PlotLineStatus ReadPlotLineStatus(int value) =>
        Enum.IsDefined(typeof(PlotLineStatus), value) ? (PlotLineStatus)value : PlotLineStatus.InProgress;

    /// <summary>
    /// Персонажи хранятся как JSON-поле: карточка состоит из десятков свободных полей,
    /// поэтому колонками описан только порядок и поиск по имени (ADR 010).
    /// </summary>
    private static void ReadCharacters(SqliteConnection connection, Project project)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT data_json FROM character WHERE project_id = $projectId ORDER BY order_index, name;";
        command.Parameters.AddWithValue("$projectId", SqlValue.ToDb(project.Id));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (Deserialize(reader.GetString(0), CoreJsonContext.Default.Character) is { } character)
            {
                project.Characters.Add(character);
            }
        }
    }

    /// <summary>Шаги линий приходят из своей таблицы и прикрепляются к линии по идентификатору.</summary>
    private static void ReadPlotLineSteps(SqliteConnection connection, Project project)
    {
        var tables = new[] { "plot_line_step", "plot_line_stage" }
            .Where(table => TableExists(connection, table))
            .ToArray();
        if (tables.Length == 0)
        {
            throw new InvalidDataException("В файле проекта отсутствует таблица этапов сюжетных линий.");
        }

        var loadedIds = new HashSet<Guid>();
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT data_json FROM {table} ORDER BY plot_line_id, order_index;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (Deserialize(reader.GetString(0), CoreJsonContext.Default.PlotLineStep) is not { } step ||
                    !loadedIds.Add(step.Id))
                {
                    continue;
                }

                project.FindPlotLine(step.PlotLineId)?.Steps.Add(step);
            }
        }
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>Локации проекта хранятся JSON-полем рядом с колонками порядка и поиска.</summary>
    private static void ReadLocations(SqliteConnection connection, Project project)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT data_json FROM location WHERE project_id = $projectId ORDER BY order_index, name;";
        command.Parameters.AddWithValue("$projectId", SqlValue.ToDb(project.Id));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (Deserialize(reader.GetString(0), CoreJsonContext.Default.Location) is { } location)
            {
                project.Locations.Add(location);
            }
        }
    }

    /// <summary>Значимые предметы истории — та же схема хранения, что и локации.</summary>
    private static void ReadStoryObjects(SqliteConnection connection, Project project)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT data_json FROM story_object WHERE project_id = $projectId ORDER BY order_index, name;";
        command.Parameters.AddWithValue("$projectId", SqlValue.ToDb(project.Id));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (Deserialize(reader.GetString(0), CoreJsonContext.Default.StoryObject) is { } item)
            {
                project.StoryObjects.Add(item);
            }
        }
    }

    private static void ReadAudit(RowReader row, ProjectEntity entity)
    {
        entity.Audit.CreatedAt = row.GetTimestamp("created_at");
        entity.Audit.UpdatedAt = row.GetTimestamp("updated_at");
        entity.Audit.IsDeleted = row.GetBool("is_deleted");
        entity.Audit.DeletedAt = row.GetTimestampOrNull("deleted_at");
    }

    private static T? Deserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class =>
        JsonSerializer.Deserialize(json, typeInfo);
}
