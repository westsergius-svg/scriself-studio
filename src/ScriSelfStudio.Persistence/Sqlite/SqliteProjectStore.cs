namespace ScriSelfStudio.Persistence.Sqlite;

using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Schema;
using ScriSelfStudio.Persistence.FileSystem;

/// <summary>
/// Файловое хранилище проекта формата <c>.scri</c> на SQLite (ТЗ п. 5, 13, 23).
/// Все операции с диском выполняются в фоне: UI-поток не должен ждать ввод-вывод (ТЗ п. 43).
/// </summary>
public sealed class SqliteProjectStore : IProjectStore
{
    private readonly ILogger<SqliteProjectStore>? _logger;

    public SqliteProjectStore(ILogger<SqliteProjectStore>? logger = null) => _logger = logger;

    public Task<Result<StoreProbeResult>> ProbeAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Result<StoreProbeResult>.Success(Probe(filePath)), cancellationToken);

    public Task<Result<ProjectLoadResult>> LoadAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(filePath), cancellationToken);

    public Task<Result> SaveAsync(ProjectSaveRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(() => Save(request), cancellationToken);

    private static StoreProbeResult Probe(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new StoreProbeResult { State = StoreFileState.Missing, SchemaVersion = -1 };
        }

        try
        {
            using var connection = SqliteConnectionFactory.Open(filePath, SqliteOpenMode.ReadOnly);
            var version = ProjectSchema.ReadVersion(connection);
            if (version < 0)
            {
                return new StoreProbeResult
                {
                    State = StoreFileState.Corrupt,
                    SchemaVersion = -1,
                    Details = "В файле отсутствует таблица schema_info.",
                };
            }

            if (SchemaVersion.IsFuture(version))
            {
                return new StoreProbeResult { State = StoreFileState.FutureVersion, SchemaVersion = version };
            }

            if (SchemaVersion.IsTooOld(version))
            {
                return new StoreProbeResult { State = StoreFileState.UnsupportedVersion, SchemaVersion = version };
            }

            return new StoreProbeResult
            {
                State = StoreFileState.Valid,
                SchemaVersion = version,
                ProjectId = ReadProjectId(connection),
                ProjectName = ReadProjectName(connection),
            };
        }
        catch (SqliteException exception)
        {
            return new StoreProbeResult
            {
                State = StoreFileState.Corrupt,
                SchemaVersion = -1,
                Details = exception.Message,
            };
        }
        catch (IOException exception)
        {
            return new StoreProbeResult
            {
                State = StoreFileState.Corrupt,
                SchemaVersion = -1,
                Details = exception.Message,
            };
        }
        catch (UnauthorizedAccessException exception)
        {
            return new StoreProbeResult
            {
                State = StoreFileState.Corrupt,
                SchemaVersion = -1,
                Details = exception.Message,
            };
        }
    }

    private Result<ProjectLoadResult> Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return Result<ProjectLoadResult>.FromError(
                ErrorCode.ProjectNotFound,
                technicalDetails: filePath);
        }

        try
        {
            var version = ReadVersion(filePath);
            if (version < 0)
            {
                return Result<ProjectLoadResult>.FromError(
                    ErrorCode.ProjectFormatInvalid,
                    technicalDetails: $"В файле {filePath} отсутствует информация о версии схемы.");
            }

            if (SchemaVersion.IsFuture(version) || SchemaVersion.IsTooOld(version))
            {
                return Result<ProjectLoadResult>.FromError(
                    ErrorCode.UnsupportedDocumentVersion,
                    technicalDetails: $"Версия схемы файла: {version}, поддерживается: {SchemaVersion.MinimumSupported}..{SchemaVersion.Current}.");
            }

            var warnings = new List<string>();
            if (version < ProjectSchema.CurrentVersion)
            {
                var migrated = MigrateCopy(filePath, version);
                if (!migrated.IsSuccess)
                {
                    return Result<ProjectLoadResult>.Failure(migrated.Error!);
                }

                version = migrated.Require();
                warnings.Add($"Файл проекта обновлён до версии схемы {version}.");
            }

            using var connection = SqliteConnectionFactory.Open(filePath, SqliteOpenMode.ReadOnly);
            var result = SqliteProjectReader.Read(connection, version, warnings);
            return Result<ProjectLoadResult>.Success(result);
        }
        catch (SqliteException exception)
        {
            _logger?.LogError(exception, "Не удалось прочитать файл проекта {FilePath}.", filePath);
            return Result<ProjectLoadResult>.FromError(
                ErrorCode.ProjectFormatInvalid,
                technicalDetails: exception.Message,
                exception: exception);
        }
        catch (InvalidDataException exception)
        {
            _logger?.LogError(exception, "Файл проекта {FilePath} имеет неполные данные.", filePath);
            return Result<ProjectLoadResult>.FromError(
                ErrorCode.ProjectFormatInvalid,
                technicalDetails: exception.Message,
                exception: exception);
        }
        catch (System.Text.Json.JsonException exception)
        {
            _logger?.LogError(exception, "Данные JSON в файле проекта {FilePath} повреждены.", filePath);
            return Result<ProjectLoadResult>.FromError(ErrorCode.ProjectFormatInvalid,
                "Данные проекта повреждены. Откройте резервную копию файла; исходный проект не изменён.",
                technicalDetails: exception.Message, exception: exception);
        }
        catch (IOException exception)
        {
            _logger?.LogError(exception, "Ошибка ввода-вывода при чтении файла проекта {FilePath}.", filePath);
            return Result<ProjectLoadResult>.FromError(
                ErrorCode.StorageReadFailed,
                technicalDetails: exception.Message,
                exception: exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger?.LogError(exception, "Нет доступа к файлу проекта {FilePath}.", filePath);
            return Result<ProjectLoadResult>.FromError(
                ErrorCode.StorageReadFailed,
                technicalDetails: exception.Message,
                exception: exception);
        }
    }

    private Result Save(ProjectSaveRequest request)
    {
        var targetPath = request.FilePath;
        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (string.IsNullOrEmpty(directory))
        {
            return Result.FromError(ErrorCode.FileOperationFailed, technicalDetails: targetPath);
        }

        if (SchemaVersion.IsFuture(request.Project.SchemaVersion))
        {
            return Result.FromError(
                ErrorCode.UnsupportedDocumentVersion,
                technicalDetails: $"Проект помечен версией схемы {request.Project.SchemaVersion}.");
        }

        var tempPath = targetPath + AtomicFileWriter.TempSuffix;

        try
        {
            Directory.CreateDirectory(directory);
            AtomicFileWriter.TryDelete(tempPath);
            SqliteProjectWriter.Write(request.Project, tempPath, DateTimeOffset.UtcNow);
        }
        catch (SqliteException exception)
        {
            _logger?.LogError(exception, "Не удалось записать временный файл проекта {TempPath}.", tempPath);
            AtomicFileWriter.TryDelete(tempPath);
            return Result.FromError(ErrorCode.StorageWriteFailed, technicalDetails: exception.Message, exception: exception);
        }
        catch (IOException exception)
        {
            _logger?.LogError(exception, "Ошибка ввода-вывода при записи проекта {FilePath}.", targetPath);
            AtomicFileWriter.TryDelete(tempPath);
            return Result.FromError(ErrorCode.StorageWriteFailed, technicalDetails: exception.Message, exception: exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger?.LogError(exception, "Нет доступа на запись проекта {FilePath}.", targetPath);
            AtomicFileWriter.TryDelete(tempPath);
            return Result.FromError(ErrorCode.StorageWriteFailed, technicalDetails: exception.Message, exception: exception);
        }

        try
        {
            var unchanged = FileSystem.ProjectFileAccess.CheckVersion(targetPath, request.ExpectedFileVersion);
            if (!unchanged.IsSuccess)
            {
                AtomicFileWriter.TryDelete(tempPath);
                return unchanged;
            }
            AtomicFileWriter.Replace(tempPath, targetPath, request.CreateBackup);
        }
        catch (IOException exception)
        {
            _logger?.LogError(exception, "Не удалось заменить файл проекта {FilePath}.", targetPath);
            AtomicFileWriter.TryDelete(tempPath);
            return Result.FromError(ErrorCode.StorageWriteFailed, technicalDetails: exception.Message, exception: exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger?.LogError(exception, "Нет доступа на замену файла проекта {FilePath}.", targetPath);
            AtomicFileWriter.TryDelete(tempPath);
            return Result.FromError(ErrorCode.StorageWriteFailed, technicalDetails: exception.Message, exception: exception);
        }

        request.Project.SchemaVersion = SchemaVersion.Current;
        return Result.Success;
    }

    private static Result<int> MigrateCopy(string filePath, int version)
    {
        var tempPath = filePath + AtomicFileWriter.TempSuffix;

        try
        {
            File.Copy(filePath, tempPath, overwrite: true);
            int migrated;
            using (var connection = SqliteConnectionFactory.Open(tempPath, SqliteOpenMode.ReadWrite))
            {
                migrated = ProjectMigrations.Migrate(connection, version);
            }

            // Замена выполняется после закрытия соединения: иначе временный файл ещё занят
            // и подмена срывается (найдено при первой реальной миграции схемы).
            AtomicFileWriter.Replace(tempPath, filePath, createBackup: true);
            return Result<int>.Success(migrated);
        }
        catch (InvalidOperationException exception)
        {
            AtomicFileWriter.TryDelete(tempPath);
            return Result<int>.FromError(ErrorCode.SchemaMigrationFailed, technicalDetails: exception.Message, exception: exception);
        }
        catch (SqliteException exception)
        {
            AtomicFileWriter.TryDelete(tempPath);
            return Result<int>.FromError(ErrorCode.SchemaMigrationFailed, technicalDetails: exception.Message, exception: exception);
        }
        catch (IOException exception)
        {
            AtomicFileWriter.TryDelete(tempPath);
            return Result<int>.FromError(ErrorCode.SchemaMigrationFailed, technicalDetails: exception.Message, exception: exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            AtomicFileWriter.TryDelete(tempPath);
            return Result<int>.FromError(ErrorCode.SchemaMigrationFailed, technicalDetails: exception.Message, exception: exception);
        }
    }

    private static int ReadVersion(string filePath)
    {
        using var connection = SqliteConnectionFactory.Open(filePath, SqliteOpenMode.ReadOnly);
        return ProjectSchema.ReadVersion(connection);
    }

    private static Guid? ReadProjectId(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM project LIMIT 1;";
        var value = command.ExecuteScalar();
        return value is string text && Guid.TryParse(text, out var id) ? id : null;
    }

    private static string? ReadProjectName(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM project LIMIT 1;";
        return command.ExecuteScalar() as string;
    }
}
