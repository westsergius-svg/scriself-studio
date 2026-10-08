namespace ScriSelfStudio.TestSupport;

using System.Text.Json;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core.Json;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Schema;

/// <summary>
/// Хранилище в памяти для тестов правил, не связанных с SQLite. Копирует проект
/// через сериализацию, поэтому проверяется и сериализация графа.
/// </summary>
public sealed class InMemoryProjectStore : IProjectStore
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public int SaveCount { get; private set; }

    public bool FailOnSave { get; set; }

    public bool FailOnLoad { get; set; }

    public void SetRaw(string filePath, string content) => _files[filePath] = content;

    public string? GetRaw(string filePath) => _files.TryGetValue(filePath, out var content) ? content : null;

    public bool Exists(string filePath) => _files.ContainsKey(filePath);

    public Task<Result<StoreProbeResult>> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var state = _files.ContainsKey(filePath) ? StoreFileState.Valid : StoreFileState.Missing;
        return Task.FromResult(Result<StoreProbeResult>.Success(new StoreProbeResult
        {
            State = state,
            SchemaVersion = SchemaVersion.Current,
        }));
    }

    public Task<Result<ProjectLoadResult>> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (FailOnLoad)
        {
            return Task.FromResult(Result<ProjectLoadResult>.FromError(ErrorCode.StorageReadFailed));
        }

        if (!_files.TryGetValue(filePath, out var content))
        {
            return Task.FromResult(Result<ProjectLoadResult>.FromError(ErrorCode.ProjectNotFound));
        }

        var project = JsonSerializer.Deserialize(content, CoreJsonContext.Default.Project);
        return project is null
            ? Task.FromResult(Result<ProjectLoadResult>.FromError(ErrorCode.ProjectFormatInvalid))
            : Task.FromResult(Result<ProjectLoadResult>.Success(new ProjectLoadResult
            {
                Project = project,
                SourceSchemaVersion = project.SchemaVersion,
                Warnings = [],
            }));
    }

    public Task<Result> SaveAsync(ProjectSaveRequest request, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        if (FailOnSave)
        {
            return Task.FromResult(Result.FromError(ErrorCode.StorageWriteFailed));
        }

        _files[request.FilePath] = JsonSerializer.Serialize(request.Project, CoreJsonContext.Default.Project);
        return Task.FromResult(Result.Success);
    }
}
