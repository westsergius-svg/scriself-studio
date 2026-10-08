namespace ScriSelfStudio.Contracts.Storage;

using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Core.Model;

/// <summary>Состояние файла проекта при проверке (ТЗ п. 5, 13, 26).</summary>
public enum StoreFileState
{
    Missing = 0,
    Valid = 1,
    Corrupt = 2,
    UnsupportedVersion = 3,
    FutureVersion = 4,
}

/// <summary>Результат проверки файла без полной загрузки.</summary>
public sealed record StoreProbeResult
{
    public required StoreFileState State { get; init; }

    public required int SchemaVersion { get; init; }

    public Guid? ProjectId { get; init; }

    public string? ProjectName { get; init; }

    public string? Details { get; init; }
}

/// <summary>Загруженный проект и замечания, не мешающие работе.</summary>
public sealed record ProjectLoadResult
{
    public required Project Project { get; init; }

    public required int SourceSchemaVersion { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>Запрос сохранения проекта (ТЗ п. 13, 23).</summary>
public sealed record ProjectSaveRequest
{
    public required Project Project { get; init; }

    public required string FilePath { get; init; }

    public required bool CreateBackup { get; init; }
}

/// <summary>
/// Хранилище файла проекта. Реализация — SQLite внутри файла <c>.scri</c>
/// с атомарной записью через временный файл (ТЗ п. 13).
/// </summary>
public interface IProjectStore
{
    Task<Result<StoreProbeResult>> ProbeAsync(string filePath, CancellationToken cancellationToken = default);

    Task<Result<ProjectLoadResult>> LoadAsync(string filePath, CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(ProjectSaveRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Хранилище файлов восстановления после аварийного завершения (ТЗ п. 13, 28).</summary>
public interface IProjectRecoveryStore
{
    string GetRecoveryPath(string projectFilePath);

    Task<Result> WriteAsync(Project project, string projectFilePath, CancellationToken cancellationToken = default);

    Task<Result<Project>> ReadAsync(Projects.RecoveryInfo recovery, CancellationToken cancellationToken = default);

    Task<Result<Projects.RecoveryInfo?>> FindAsync(string projectFilePath, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(string projectFilePath, CancellationToken cancellationToken = default);
}
