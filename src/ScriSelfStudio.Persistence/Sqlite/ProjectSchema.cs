namespace ScriSelfStudio.Persistence.Sqlite;

using Microsoft.Data.Sqlite;

/// <summary>Схема базы данных файла проекта (ТЗ п. 5, 13).</summary>
internal static class ProjectSchema
{
    public const int CurrentVersion = Core.Schema.SchemaVersion.Current;

    private const string SchemaInfoSql = """
        CREATE TABLE IF NOT EXISTS schema_info (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            version INTEGER NOT NULL,
            app_version TEXT NULL,
            updated_at TEXT NOT NULL
        );
        """;

    private const string EntitiesSql = """
        CREATE TABLE IF NOT EXISTS project (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT NULL,
            schema_version INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            settings_json TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS series (
            id TEXT PRIMARY KEY,
            project_id TEXT NOT NULL,
            title TEXT NOT NULL,
            order_index INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            deleted_at TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS book (
            id TEXT PRIMARY KEY,
            project_id TEXT NOT NULL,
            series_id TEXT NOT NULL,
            title TEXT NOT NULL,
            order_index INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            deleted_at TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS chapter (
            id TEXT PRIMARY KEY,
            book_id TEXT NOT NULL,
            title TEXT NOT NULL,
            description TEXT NULL,
            order_index INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            deleted_at TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS scene (
            id TEXT PRIMARY KEY,
            chapter_id TEXT NOT NULL,
            title TEXT NOT NULL,
            description TEXT NULL,
            status INTEGER NOT NULL,
            order_index INTEGER NOT NULL,
            revision INTEGER NOT NULL DEFAULT 0,
            metadata_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            deleted_at TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS scene_document (
            scene_id TEXT PRIMARY KEY,
            document_json TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS plot_line (
            id TEXT PRIMARY KEY,
            project_id TEXT NOT NULL,
            name TEXT NOT NULL,
            description TEXT NULL,
            goal TEXT NULL,
            status INTEGER NOT NULL DEFAULT 2,
            notes TEXT NULL,
            color_hex TEXT NULL,
            order_index INTEGER NOT NULL,
            scene_ids_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            deleted_at TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_series_project ON series (project_id);
        CREATE INDEX IF NOT EXISTS ix_book_series ON book (series_id);
        CREATE INDEX IF NOT EXISTS ix_chapter_book ON chapter (book_id);
        CREATE INDEX IF NOT EXISTS ix_scene_chapter ON scene (chapter_id);
        CREATE INDEX IF NOT EXISTS ix_plot_line_project ON plot_line (project_id);
        """;

    public static void Ensure(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        Execute(connection, transaction, SchemaInfoSql);
        Execute(connection, transaction, EntitiesSql);
        Execute(connection, transaction, StorySql);
        Execute(connection, transaction, LibrarySql);
    }

    /// <summary>
    /// Таблицы сюжетных сущностей (персонажи, шаги линий). Добавлены в версии схемы 2:
    /// у нас они хранятся как JSON-поле рядом с колонками для порядка и выборок
    /// (ТЗ п. 27; ADR 010). Такой шаг безопасно применять к файлу версии 1.
    /// </summary>
    internal const string StorySql = """
        CREATE TABLE IF NOT EXISTS character (
            id TEXT PRIMARY KEY,
            project_id TEXT NOT NULL,
            name TEXT NOT NULL,
            order_index INTEGER NOT NULL,
            data_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            deleted_at TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS plot_line_step (
            id TEXT PRIMARY KEY,
            plot_line_id TEXT NOT NULL,
            title TEXT NOT NULL,
            order_index INTEGER NOT NULL,
            data_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            deleted_at TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_character_project ON character (project_id);
        CREATE INDEX IF NOT EXISTS ix_plot_line_step_line ON plot_line_step (plot_line_id);
        """;

    /// <summary>
    /// Версия схемы 4: локации и значимые предметы («Локации/Объекты»).
    /// Шаг только добавляет таблицы, поэтому файлы версии 3 открываются без потерь.
    /// </summary>
    internal const string LibrarySql = """
        CREATE TABLE IF NOT EXISTS location (
            id TEXT PRIMARY KEY,
            project_id TEXT NOT NULL,
            name TEXT NOT NULL,
            order_index INTEGER NOT NULL,
            data_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            deleted_at TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS story_object (
            id TEXT PRIMARY KEY,
            project_id TEXT NOT NULL,
            name TEXT NOT NULL,
            order_index INTEGER NOT NULL,
            data_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            deleted_at TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_location_project ON location (project_id);
        CREATE INDEX IF NOT EXISTS ix_story_object_project ON story_object (project_id);
        """;

    /// <summary>Версия схемы файла. Возвращает -1, если таблица отсутствует или пуста.</summary>
    public static int ReadVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_info WHERE id = 1 LIMIT 1;";
        var value = command.ExecuteScalar();
        return value is long version ? (int)version : -1;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }
}
