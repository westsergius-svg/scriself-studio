namespace ScriSelfStudio.TestSupport;

using System.Text.Json;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Json;
using ScriSelfStudio.Core.Model;

/// <summary>Хранилище восстановления в памяти.</summary>
public sealed class InMemoryRecoveryStore : IProjectRecoveryStore
{
    private readonly Dictionary<string, string> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISystemClock _clock;

    public InMemoryRecoveryStore(ISystemClock? clock = null) => _clock = clock ?? new FakeClock();

    public int WriteCount { get; private set; }

    public int DeleteCount { get; private set; }

    public string GetRecoveryPath(string projectFilePath) => projectFilePath + ".recovery";

    public Task<Result> WriteAsync(Project project, string projectFilePath, CancellationToken cancellationToken = default)
    {
        WriteCount++;
        _snapshots[GetRecoveryPath(projectFilePath)] = JsonSerializer.Serialize(project, CoreJsonContext.Default.Project);
        LastWrittenAt = _clock.UtcNow;
        return Task.FromResult(Result.Success);
    }

    public Task<Result<Project>> ReadAsync(RecoveryInfo recovery, CancellationToken cancellationToken = default)
    {
        if (!_snapshots.TryGetValue(recovery.RecoveryFilePath, out var content))
        {
            return Task.FromResult(Result<Project>.FromError(ErrorCode.RecoveryReadFailed));
        }

        var project = JsonSerializer.Deserialize(content, CoreJsonContext.Default.Project);
        return project is null
            ? Task.FromResult(Result<Project>.FromError(ErrorCode.RecoveryReadFailed))
            : Task.FromResult(Result<Project>.Success(project));
    }

    public Task<Result<RecoveryInfo?>> FindAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        var path = GetRecoveryPath(projectFilePath);
        if (!_snapshots.TryGetValue(path, out var content))
        {
            return Task.FromResult(Result<RecoveryInfo?>.Success(null));
        }

        var project = JsonSerializer.Deserialize(content, CoreJsonContext.Default.Project);
        return Task.FromResult(Result<RecoveryInfo?>.Success(new RecoveryInfo
        {
            ProjectFilePath = projectFilePath,
            RecoveryFilePath = path,
            WrittenAt = LastWrittenAt,
            ProjectId = project?.Id ?? Guid.Empty,
            ProjectName = project?.Name ?? string.Empty,
        }));
    }

    public Task<Result> DeleteAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        DeleteCount++;
        _snapshots.Remove(GetRecoveryPath(projectFilePath));
        return Task.FromResult(Result.Success);
    }

    public DateTimeOffset LastWrittenAt { get; private set; }
}
