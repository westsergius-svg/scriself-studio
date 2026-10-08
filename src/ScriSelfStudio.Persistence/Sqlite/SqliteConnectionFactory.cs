namespace ScriSelfStudio.Persistence.Sqlite;

using Microsoft.Data.Sqlite;

/// <summary>
/// Создание подключений к файлу проекта. Пул отключён, чтобы файл не оставался
/// заблокированным после закрытия: сохранение подменяет файл целиком (ТЗ п. 13, 23).
/// </summary>
internal static class SqliteConnectionFactory
{
    public const string FileExtension = ".scri";

    public static SqliteConnection Open(string filePath, SqliteOpenMode mode)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        if (mode != SqliteOpenMode.ReadOnly)
        {
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode = DELETE;";
            pragma.ExecuteScalar();
        }

        return connection;
    }
}
