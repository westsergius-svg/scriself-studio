namespace ScriSelfStudio.Persistence.Formats;

using Microsoft.Data.Sqlite;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Persistence.Sqlite;

/// <summary>
/// Выбор хранилища по формату файла: собственный SQLite-проект или старый ZIP-контейнер.
/// Формат определяется по содержимому файла, а не по расширению (ТЗ п. 5).
/// </summary>
public sealed class RoutingProjectStore : IProjectStore
{
    private readonly SqliteProjectStore _sqlite;
    private readonly ScriZipProjectStore _zip;

    public RoutingProjectStore(SqliteProjectStore sqlite, ScriZipProjectStore zip)
    {
        _sqlite = sqlite;
        _zip = zip;
    }

    public Task<Result<StoreProbeResult>> ProbeAsync(string filePath, CancellationToken cancellationToken = default) =>
        ScriZipFormat.HasZipSignature(filePath)
            ? _zip.ProbeAsync(filePath, cancellationToken)
            : _sqlite.ProbeAsync(filePath, cancellationToken);

    public async Task<Result<ProjectLoadResult>> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (ScriZipFormat.HasZipSignature(filePath))
        {
            return await _zip.LoadAsync(filePath, cancellationToken);
        }

        var loaded = await _sqlite.LoadAsync(filePath, cancellationToken);
        if (loaded.IsSuccess)
        {
            return loaded;
        }

        // Файл может быть из ранней модульной сборки: это тоже SQLite, но с другой
        // схемой. Импортируем его, а не отказываем пользователю.
        if (LegacyModularImporter.IsLegacyModularFile(filePath))
        {
            try
            {
                return Result<ProjectLoadResult>.Success(
                    LegacyModularImporter.Load(filePath, DateTimeOffset.UtcNow));
            }
            catch (SqliteException exception)
            {
                return Result<ProjectLoadResult>.FromError(
                    ErrorCode.ProjectFormatInvalid,
                    "Не удалось импортировать ранний модульный формат.",
                    technicalDetails: exception.Message,
                    exception: exception);
            }
            catch (IOException exception)
            {
                return Result<ProjectLoadResult>.FromError(
                    ErrorCode.StorageReadFailed,
                    "Не удалось прочитать ранний модульный формат.",
                    technicalDetails: exception.Message,
                    exception: exception);
            }
        }

        return LegacyModularFormat.Describe(filePath) is { } legacy
            ? Result<ProjectLoadResult>.Failure(legacy)
            : loaded;
    }

    public Task<Result> SaveAsync(ProjectSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return string.Equals(request.Project.SourceFormat, ScriZipFormat.FormatId, StringComparison.Ordinal)
            ? _zip.SaveAsync(request, cancellationToken)
            : _sqlite.SaveAsync(request, cancellationToken);
    }
}

/// <summary>
/// Распознавание ранних модульных форматов (таблицы <c>entities</c>/<c>meta</c>).
/// Их поддержка запланирована; пока пользователь получает точное объяснение.
/// </summary>
internal static class LegacyModularFormat
{
    public static AppError? Describe(string filePath)
    {
        try
        {
            // Пул отключён: файл должен остаться свободным для первого сохранения.
            using var connection = LegacyModularImporter.OpenForInspection(filePath);

            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            if (!tables.Contains("entities") || !tables.Contains("meta"))
            {
                return null;
            }

            var appVersion = ReadMeta(connection, "appVersion") ?? ReadMeta(connection, "formatVersion");
            var suffix = string.IsNullOrWhiteSpace(appVersion) ? string.Empty : $" {appVersion}";
            return AppError.Create(
                ErrorCode.UnsupportedDocumentVersion,
                $"Файл создан ранней сборкой (модульный формат{suffix}). Этот формат пока не поддерживается.",
                "Сохраните копию файла: поддержка ранних форматов появится в следующем обновлении.",
                technicalDetails: $"tables: {string.Join(", ", tables.Order())}");
        }
        catch (SqliteException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? ReadMeta(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);

        var value = command.ExecuteScalar();
        return value is null || value is DBNull
            ? null
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
