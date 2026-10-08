namespace ScriSelfStudio.Persistence.Sqlite;

using Microsoft.Data.Sqlite;

/// <summary>Один шаг миграции схемы файла проекта (ТЗ п. 5, 13).</summary>
internal interface IProjectMigration
{
    int FromVersion { get; }

    int ToVersion { get; }

    void Apply(SqliteConnection connection, SqliteTransaction transaction);
}

/// <summary>
/// Последовательное обновление схемы. Каждый шаг выполняется в транзакции;
/// при ошибке исходный файл остаётся нетронутым, потому что миграция идёт по копии.
/// </summary>
internal static class ProjectMigrations
{
    private static readonly IProjectMigration[] Steps =
        [new MigrationV1ToV2(), new MigrationV2ToV3(), new MigrationV3ToV4()];

    /// <summary>
    /// Версия 2: персонажи, шаги их арок и шаги сюжетных линий (ТЗ п. 27; ADR 010).
    /// Шаг только добавляет таблицы, поэтому файлы версии 1 открываются без потерь.
    /// </summary>
    private sealed class MigrationV1ToV2 : IProjectMigration
    {
        public int FromVersion => 1;

        public int ToVersion => 2;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = ProjectSchema.StorySql
                + "UPDATE schema_info SET version = 2 WHERE id = 1;";
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Версия 3: цель, статус и заметки сюжетной линии (макет экрана линий).
    /// Шаг только добавляет колонки со значениями по умолчанию, поэтому файлы
    /// версии 2 открываются без потерь.
    /// </summary>
    private sealed class MigrationV2ToV3 : IProjectMigration
    {
        public int FromVersion => 2;

        public int ToVersion => 3;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
            // Колонки добавляются только если их нет: повторный прогон шага не должен
            // падать с «duplicate column name» (файл мог быть записан сборкой, которая
            // уже умеет эти поля, но помечен старой версией схемы).
            AddColumnIfMissing(connection, transaction, "plot_line", "goal", "TEXT NULL");
            AddColumnIfMissing(connection, transaction, "plot_line", "status", "INTEGER NOT NULL DEFAULT 2");
            AddColumnIfMissing(connection, transaction, "plot_line", "notes", "TEXT NULL");

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE schema_info SET version = 3 WHERE id = 1;";
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Версия 4: локации и значимые предметы («Локации/Объекты»).
    /// Шаг только добавляет таблицы, поэтому файлы версии 3 открываются без потерь.
    /// </summary>
    private sealed class MigrationV3ToV4 : IProjectMigration
    {
        public int FromVersion => 3;

        public int ToVersion => 4;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = ProjectSchema.LibrarySql
                + "UPDATE schema_info SET version = 4 WHERE id = 1;";
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Есть ли колонка в таблице (PRAGMA table_info).</summary>
    private static bool ColumnExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Добавляет колонку, только если её ещё нет: шаг миграции идемпотентен.</summary>
    private static void AddColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        string definition)
    {
        if (ColumnExists(connection, transaction, table, column))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        command.ExecuteNonQuery();
    }

    /// <summary>Обновляет схему до текущей версии. Возвращает итоговую версию.</summary>
    public static int Migrate(SqliteConnection connection, int fromVersion)
    {
        var version = fromVersion;
        while (version < ProjectSchema.CurrentVersion)
        {
            var step = Find(version);
            if (step is null)
            {
                throw new InvalidOperationException(
                    $"Нет миграции со версии схемы {version} на версию {version + 1}.");
            }

            using var transaction = connection.BeginTransaction();
            step.Apply(connection, transaction);
            transaction.Commit();
            version = step.ToVersion;
        }

        return version;
    }

    private static IProjectMigration? Find(int fromVersion)
    {
        foreach (var step in Steps)
        {
            if (step.FromVersion == fromVersion)
            {
                return step;
            }
        }

        return null;
    }
}
