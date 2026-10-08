namespace ScriSelfStudio.Persistence.Sqlite;

using System.Globalization;
using Microsoft.Data.Sqlite;

/// <summary>Преобразование значений между моделью и SQLite.</summary>
internal static class SqlValue
{
    public static string ToDb(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);

    public static string ToDb(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static string? ToDb(DateTimeOffset? value) => value is null ? null : ToDb(value.Value);

    public static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

/// <summary>Чтение колонок строки результата по имени.</summary>
internal sealed class RowReader(SqliteDataReader reader)
{
    public Guid GetGuid(string column) => Guid.Parse(GetString(column));

    public string GetString(string column) => reader.GetString(reader.GetOrdinal(column));

    public string? GetStringOrNull(string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? null : reader.GetString(index);
    }

    public int GetInt(string column) => reader.GetInt32(reader.GetOrdinal(column));

    public long GetLong(string column) => reader.GetInt64(reader.GetOrdinal(column));

    public bool GetBool(string column) => reader.GetInt64(reader.GetOrdinal(column)) != 0;

    public DateTimeOffset GetTimestamp(string column) => SqlValue.ParseTimestamp(GetString(column));

    public DateTimeOffset? GetTimestampOrNull(string column)
    {
        var value = GetStringOrNull(column);
        return value is null ? null : SqlValue.ParseTimestamp(value);
    }
}
