namespace ScriSelfStudio.Persistence.Sqlite;

using Microsoft.Data.Sqlite;
using System.Text.Json;
using ScriSelfStudio.Core.Json;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Schema;

/// <summary>
/// Полная запись проекта. Файл всегда создаётся заново, поэтому удалённые элементы
/// не остаются мусором, а частично записанный проект не может попасть в целевой файл.
/// </summary>
internal static class SqliteProjectWriter
{
    public static void Write(Project project, string filePath, DateTimeOffset now)
    {
        using var connection = SqliteConnectionFactory.Open(filePath, SqliteOpenMode.ReadWriteCreate);
        ProjectSchema.Ensure(connection);
        using var transaction = connection.BeginTransaction();

        WriteSchemaInfo(connection, transaction, now);
        WriteProject(connection, transaction, project);
        WritePlotLines(connection, transaction, project);
        WritePlotLineSteps(connection, transaction, project);
        WriteCharacters(connection, transaction, project);
        WriteLocations(connection, transaction, project);
        WriteStoryObjects(connection, transaction, project);
        WriteStructure(connection, transaction, project, now);

        transaction.Commit();
    }

    private static void WriteSchemaInfo(SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO schema_info (id, version, app_version, updated_at) VALUES (1, $version, $appVersion, $updatedAt);";
        command.Parameters.AddWithValue("$version", SchemaVersion.Current);
        command.Parameters.AddWithValue("$appVersion", typeof(SqliteProjectWriter).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(now));
        command.ExecuteNonQuery();
    }

    private static void WriteProject(SqliteConnection connection, SqliteTransaction transaction, Project project)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO project (id, name, description, schema_version, created_at, updated_at, settings_json) " +
            "VALUES ($id, $name, $description, $schemaVersion, $createdAt, $updatedAt, $settings);";
        command.Parameters.AddWithValue("$id", SqlValue.ToDb(project.Id));
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$description", (object?)project.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$schemaVersion", SchemaVersion.Current);
        command.Parameters.AddWithValue("$createdAt", SqlValue.ToDb(project.Audit.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(project.Audit.UpdatedAt));
        command.Parameters.AddWithValue("$settings", JsonSerializer.Serialize(project.Settings, CoreJsonContext.Default.ProjectSettings));
        command.ExecuteNonQuery();
    }

    private static void WritePlotLines(SqliteConnection connection, SqliteTransaction transaction, Project project)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO plot_line (id, project_id, name, description, goal, status, notes, color_hex, " +
            "order_index, scene_ids_json, created_at, updated_at, is_deleted, deleted_at) " +
            "VALUES ($id, $projectId, $name, $description, $goal, $status, $notes, $colorHex, $order, " +
            "$sceneIds, $createdAt, $updatedAt, $isDeleted, $deletedAt);";

        foreach (var plotLine in project.PlotLines)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", SqlValue.ToDb(plotLine.Id));
            command.Parameters.AddWithValue("$projectId", SqlValue.ToDb(plotLine.ProjectId));
            command.Parameters.AddWithValue("$name", plotLine.Name);
            command.Parameters.AddWithValue("$description", (object?)plotLine.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("$goal", (object?)plotLine.Goal ?? DBNull.Value);
            command.Parameters.AddWithValue("$status", (int)plotLine.Status);
            command.Parameters.AddWithValue("$notes", (object?)plotLine.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("$colorHex", (object?)plotLine.ColorHex ?? DBNull.Value);
            command.Parameters.AddWithValue("$order", plotLine.Order);
            command.Parameters.AddWithValue("$sceneIds", JsonSerializer.Serialize(plotLine.SceneIds, CoreJsonContext.Default.ListGuid));
            command.Parameters.AddWithValue("$createdAt", SqlValue.ToDb(plotLine.Audit.CreatedAt));
            command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(plotLine.Audit.UpdatedAt));
            command.Parameters.AddWithValue("$isDeleted", plotLine.Audit.IsDeleted ? 1 : 0);
            command.Parameters.AddWithValue("$deletedAt", (object?)SqlValue.ToDb(plotLine.Audit.DeletedAt) ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Шаги линий: карточка шага целиком уходит в JSON-поле (ADR 010).</summary>
    private static void WritePlotLineSteps(SqliteConnection connection, SqliteTransaction transaction, Project project)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO plot_line_step (id, plot_line_id, title, order_index, data_json, " +
            "created_at, updated_at, is_deleted, deleted_at) " +
            "VALUES ($id, $lineId, $title, $order, $data, $createdAt, $updatedAt, $isDeleted, $deletedAt);";

        foreach (var step in project.PlotLines.SelectMany(line => line.Steps))
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", SqlValue.ToDb(step.Id));
            command.Parameters.AddWithValue("$lineId", SqlValue.ToDb(step.PlotLineId));
            command.Parameters.AddWithValue("$title", step.Title);
            command.Parameters.AddWithValue("$order", step.Order);
            command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(step, CoreJsonContext.Default.PlotLineStep));
            command.Parameters.AddWithValue("$createdAt", SqlValue.ToDb(step.Audit.CreatedAt));
            command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(step.Audit.UpdatedAt));
            command.Parameters.AddWithValue("$isDeleted", step.Audit.IsDeleted ? 1 : 0);
            command.Parameters.AddWithValue("$deletedAt", (object?)SqlValue.ToDb(step.Audit.DeletedAt) ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Персонажи: карточка целиком уходит в JSON-поле (ADR 010).</summary>
    private static void WriteCharacters(SqliteConnection connection, SqliteTransaction transaction, Project project)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO character (id, project_id, name, order_index, data_json, " +
            "created_at, updated_at, is_deleted, deleted_at) " +
            "VALUES ($id, $projectId, $name, $order, $data, $createdAt, $updatedAt, $isDeleted, $deletedAt);";

        foreach (var character in project.Characters)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", SqlValue.ToDb(character.Id));
            command.Parameters.AddWithValue("$projectId", SqlValue.ToDb(character.ProjectId));
            command.Parameters.AddWithValue("$name", character.Name);
            command.Parameters.AddWithValue("$order", character.Order);
            command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(character, CoreJsonContext.Default.Character));
            command.Parameters.AddWithValue("$createdAt", SqlValue.ToDb(character.Audit.CreatedAt));
            command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(character.Audit.UpdatedAt));
            command.Parameters.AddWithValue("$isDeleted", character.Audit.IsDeleted ? 1 : 0);
            command.Parameters.AddWithValue("$deletedAt", (object?)SqlValue.ToDb(character.Audit.DeletedAt) ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Локации: карточка целиком уходит в JSON-поле.</summary>
    private static void WriteLocations(SqliteConnection connection, SqliteTransaction transaction, Project project)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO location (id, project_id, name, order_index, data_json, " +
            "created_at, updated_at, is_deleted, deleted_at) " +
            "VALUES ($id, $projectId, $name, $order, $data, $createdAt, $updatedAt, $isDeleted, $deletedAt);";

        foreach (var location in project.Locations)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", SqlValue.ToDb(location.Id));
            command.Parameters.AddWithValue("$projectId", SqlValue.ToDb(location.ProjectId));
            command.Parameters.AddWithValue("$name", location.Name);
            command.Parameters.AddWithValue("$order", location.Order);
            command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(location, CoreJsonContext.Default.Location));
            command.Parameters.AddWithValue("$createdAt", SqlValue.ToDb(location.Audit.CreatedAt));
            command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(location.Audit.UpdatedAt));
            command.Parameters.AddWithValue("$isDeleted", location.Audit.IsDeleted ? 1 : 0);
            command.Parameters.AddWithValue("$deletedAt", (object?)SqlValue.ToDb(location.Audit.DeletedAt) ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Значимые предметы истории — та же схема хранения, что и локации.</summary>
    private static void WriteStoryObjects(SqliteConnection connection, SqliteTransaction transaction, Project project)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO story_object (id, project_id, name, order_index, data_json, " +
            "created_at, updated_at, is_deleted, deleted_at) " +
            "VALUES ($id, $projectId, $name, $order, $data, $createdAt, $updatedAt, $isDeleted, $deletedAt);";

        foreach (var item in project.StoryObjects)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", SqlValue.ToDb(item.Id));
            command.Parameters.AddWithValue("$projectId", SqlValue.ToDb(item.ProjectId));
            command.Parameters.AddWithValue("$name", item.Name);
            command.Parameters.AddWithValue("$order", item.Order);
            command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(item, CoreJsonContext.Default.StoryObject));
            command.Parameters.AddWithValue("$createdAt", SqlValue.ToDb(item.Audit.CreatedAt));
            command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(item.Audit.UpdatedAt));
            command.Parameters.AddWithValue("$isDeleted", item.Audit.IsDeleted ? 1 : 0);
            command.Parameters.AddWithValue("$deletedAt", (object?)SqlValue.ToDb(item.Audit.DeletedAt) ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    private static void WriteStructure(SqliteConnection connection, SqliteTransaction transaction, Project project, DateTimeOffset now)
    {
        foreach (var series in project.Series)
        {
            InsertSeries(connection, transaction, series);

            foreach (var book in series.Books)
            {
                InsertBook(connection, transaction, book);

                foreach (var chapter in book.Chapters)
                {
                    InsertChapter(connection, transaction, chapter);

                    foreach (var scene in chapter.Scenes)
                    {
                        InsertScene(connection, transaction, scene);
                        InsertSceneDocument(connection, transaction, scene, now);
                    }
                }
            }
        }
    }

    private static void InsertSeries(SqliteConnection connection, SqliteTransaction transaction, Series series)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO series (id, project_id, title, order_index, created_at, updated_at, is_deleted, deleted_at) " +
            "VALUES ($id, $projectId, $title, $order, $createdAt, $updatedAt, $isDeleted, $deletedAt);";
        command.Parameters.AddWithValue("$id", SqlValue.ToDb(series.Id));
        command.Parameters.AddWithValue("$projectId", SqlValue.ToDb(series.ProjectId));
        command.Parameters.AddWithValue("$title", series.Title);
        command.Parameters.AddWithValue("$order", series.Order);
        command.Parameters.AddWithValue("$createdAt", SqlValue.ToDb(series.Audit.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(series.Audit.UpdatedAt));
        command.Parameters.AddWithValue("$isDeleted", series.Audit.IsDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$deletedAt", (object?)SqlValue.ToDb(series.Audit.DeletedAt) ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void InsertBook(SqliteConnection connection, SqliteTransaction transaction, Book book)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO book (id, project_id, series_id, title, order_index, created_at, updated_at, is_deleted, deleted_at) " +
            "VALUES ($id, $projectId, $seriesId, $title, $order, $createdAt, $updatedAt, $isDeleted, $deletedAt);";
        command.Parameters.AddWithValue("$id", SqlValue.ToDb(book.Id));
        command.Parameters.AddWithValue("$projectId", SqlValue.ToDb(book.ProjectId));
        command.Parameters.AddWithValue("$seriesId", SqlValue.ToDb(book.SeriesId));
        command.Parameters.AddWithValue("$title", book.Title);
        command.Parameters.AddWithValue("$order", book.Order);
        command.Parameters.AddWithValue("$createdAt", SqlValue.ToDb(book.Audit.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(book.Audit.UpdatedAt));
        command.Parameters.AddWithValue("$isDeleted", book.Audit.IsDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$deletedAt", (object?)SqlValue.ToDb(book.Audit.DeletedAt) ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void InsertChapter(SqliteConnection connection, SqliteTransaction transaction, Chapter chapter)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO chapter (id, book_id, title, description, order_index, created_at, updated_at, is_deleted, deleted_at) " +
            "VALUES ($id, $bookId, $title, $description, $order, $createdAt, $updatedAt, $isDeleted, $deletedAt);";
        command.Parameters.AddWithValue("$id", SqlValue.ToDb(chapter.Id));
        command.Parameters.AddWithValue("$bookId", SqlValue.ToDb(chapter.BookId));
        command.Parameters.AddWithValue("$title", chapter.Title);
        command.Parameters.AddWithValue("$description", (object?)chapter.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$order", chapter.Order);
        command.Parameters.AddWithValue("$createdAt", SqlValue.ToDb(chapter.Audit.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(chapter.Audit.UpdatedAt));
        command.Parameters.AddWithValue("$isDeleted", chapter.Audit.IsDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$deletedAt", (object?)SqlValue.ToDb(chapter.Audit.DeletedAt) ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void InsertScene(SqliteConnection connection, SqliteTransaction transaction, Scene scene)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO scene (id, chapter_id, title, description, status, order_index, revision, metadata_json, " +
            "created_at, updated_at, is_deleted, deleted_at) " +
            "VALUES ($id, $chapterId, $title, $description, $status, $order, $revision, $metadata, $createdAt, $updatedAt, $isDeleted, $deletedAt);";
        command.Parameters.AddWithValue("$id", SqlValue.ToDb(scene.Id));
        command.Parameters.AddWithValue("$chapterId", SqlValue.ToDb(scene.ChapterId));
        command.Parameters.AddWithValue("$title", scene.Title);
        command.Parameters.AddWithValue("$description", (object?)scene.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)scene.Status);
        command.Parameters.AddWithValue("$order", scene.Order);
        command.Parameters.AddWithValue("$revision", scene.Revision);
        command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(scene.Metadata, CoreJsonContext.Default.SceneMetadata));
        command.Parameters.AddWithValue("$createdAt", SqlValue.ToDb(scene.Audit.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(scene.Audit.UpdatedAt));
        command.Parameters.AddWithValue("$isDeleted", scene.Audit.IsDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$deletedAt", (object?)SqlValue.ToDb(scene.Audit.DeletedAt) ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void InsertSceneDocument(SqliteConnection connection, SqliteTransaction transaction, Scene scene, DateTimeOffset now)
    {
        foreach (var version in scene.Versions)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO scene_version (id, scene_id, data_json) VALUES ($id, $scene, $data);";
            insert.Parameters.AddWithValue("$id", SqlValue.ToDb(version.Id));
            insert.Parameters.AddWithValue("$scene", SqlValue.ToDb(scene.Id));
            insert.Parameters.AddWithValue("$data", JsonSerializer.Serialize(version, CoreJsonContext.Default.SceneVersion));
            insert.ExecuteNonQuery();
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO scene_document (scene_id, document_json, updated_at) VALUES ($sceneId, $document, $updatedAt);";
        command.Parameters.AddWithValue("$sceneId", SqlValue.ToDb(scene.Id));
        command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(scene.Document, CoreJsonContext.Default.RichDocument));
        command.Parameters.AddWithValue("$updatedAt", SqlValue.ToDb(now));
        command.ExecuteNonQuery();
    }
}
