namespace ScriSelfStudio.IntegrationTests;

using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

/// <summary>
/// Создаёт файл раннего модульного формата `.scri` (SQLite с таблицами
/// <c>meta</c>/<c>entities</c>/<c>relations</c>/<c>texts</c>), как его писали ранние
/// сборки приложения. Форма взят с реальных файлов пользователя.
/// </summary>
internal sealed class LegacyModularProjectBuilder
{
    private readonly List<(string Id, string Type, JsonObject Data, string? Name, int Order)> _entities = [];
    private readonly List<(string FromId, string ToId, string Type, string Meta)> _relations = [];
    private readonly List<(string Id, string Text, string Format)> _texts = [];
    private readonly Dictionary<string, string> _meta = new(StringComparer.Ordinal)
    {
        ["format"] = "scriptorium",
        ["formatVersion"] = "2",
        ["title"] = "Ранний проект",
    };

    /// <summary>Значение <c>null</c> удаляет ключ: в ранних сборках метаданные отличались.</summary>
    public LegacyModularProjectBuilder WithMeta(string key, string? value)
    {
        if (value is null)
        {
            _meta.Remove(key);
        }
        else
        {
            _meta[key] = value;
        }

        return this;
    }

    public LegacyModularProjectBuilder WithEntity(
        string id,
        string type,
        JsonObject data,
        string? name = null,
        int order = 0)
    {
        _entities.Add((id, type, data, name, order));
        return this;
    }

    public LegacyModularProjectBuilder WithText(string id, string text, string format = "plain")
    {
        _texts.Add((id, text, format));
        return this;
    }

    public LegacyModularProjectBuilder WithRelation(string fromId, string toId, string type, JsonObject? meta = null)
    {
        _relations.Add((fromId, toId, type, (meta ?? new JsonObject()).ToJsonString()));
        return this;
    }

    public void WriteTo(string filePath)
    {
        // Пул отключён: после закрытия файл должен остаться свободным для замены
        // при первом сохранении импортированного проекта.
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = filePath, Pooling = false }.ToString());
        connection.Open();

        Execute(connection, """
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE entities (
                id         TEXT PRIMARY KEY,
                type       TEXT NOT NULL,
                module     TEXT NOT NULL,
                data       TEXT NOT NULL,
                name       TEXT,
                chapter_id TEXT,
                ord        REAL NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE relations (
                id         TEXT PRIMARY KEY,
                from_id    TEXT NOT NULL,
                to_id      TEXT NOT NULL,
                rel_type   TEXT NOT NULL,
                meta       TEXT NOT NULL DEFAULT '{}',
                created_at TEXT NOT NULL
            );
            CREATE TABLE texts (
                id         TEXT PRIMARY KEY,
                content    BLOB NOT NULL,
                compressed INTEGER NOT NULL DEFAULT 0,
                raw_len    INTEGER NOT NULL DEFAULT 0,
                words      INTEGER NOT NULL DEFAULT 0,
                chars      INTEGER NOT NULL DEFAULT 0,
                chars_ns   INTEGER NOT NULL DEFAULT 0,
                paragraphs INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL,
                format     TEXT NOT NULL DEFAULT 'plain',
                format_version INTEGER NOT NULL DEFAULT 0
            );
            """);

        foreach (var (key, value) in _meta)
        {
            Execute(
                connection,
                "INSERT INTO meta (key, value) VALUES ($key, $value);",
                ("$key", key),
                ("$value", value));
        }

        foreach (var (id, type, data, name, order) in _entities)
        {
            Execute(
                connection,
                """
                INSERT INTO entities (id, type, module, data, name, chapter_id, ord, created_at, updated_at)
                VALUES ($id, $type, 'imported', $data, $name, NULL, $ord, $stamp, $stamp);
                """,
                ("$id", id),
                ("$type", type),
                ("$data", data.ToJsonString()),
                ("$name", (object?)name ?? DBNull.Value),
                ("$ord", (double)order),
                ("$stamp", "2026-08-23T10:18:12.074Z"));
        }

        foreach (var (fromId, toId, type, meta) in _relations)
        {
            Execute(
                connection,
                """
                INSERT INTO relations (id, from_id, to_id, rel_type, meta, created_at)
                VALUES ($id, $from, $to, $type, $meta, $stamp);
                """,
                ("$id", Guid.NewGuid().ToString("N")),
                ("$from", fromId),
                ("$to", toId),
                ("$type", type),
                ("$meta", meta),
                ("$stamp", "2026-08-23T10:18:12.074Z"));
        }

        foreach (var (id, text, format) in _texts)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            Execute(
                connection,
                """
                INSERT INTO texts (id, content, compressed, raw_len, words, chars, chars_ns, paragraphs, updated_at, format, format_version)
                VALUES ($id, $content, 0, $len, 0, $len, 0, 0, $stamp, $format, 0);
                """,
                ("$id", id),
                ("$content", bytes),
                ("$len", (double)bytes.Length),
                ("$stamp", "2026-08-23T10:18:12.074Z"),
                ("$format", format));
        }
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
