namespace ScriSelfStudio.Persistence.Formats;

using System.Globalization;
using System.Text.Json.Nodes;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core.Model;

/// <summary>
/// Хранилище старого ZIP-формата `.scri` (<c>manifest.json</c> + <c>content.json</c>).
/// Читает его в нашу модель и записывает обратно в тот же формат, сохраняя поля,
/// которых у нас нет (ТЗ п. 5, 13).
/// </summary>
public sealed class ScriZipProjectStore : IProjectStore
{
    private const string ManifestKey = "manifest";
    private const string ContentKey = "content";

    public Task<Result<StoreProbeResult>> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!ScriZipFormat.HasZipSignature(filePath))
        {
            return Task.FromResult(Result<StoreProbeResult>.Success(new StoreProbeResult
            {
                State = StoreFileState.Corrupt,
                SchemaVersion = 0,
                Details = "Файл не является ZIP-контейнером проекта.",
            }));
        }

        var read = ScriZipFormat.Read(filePath);
        if (!read.IsSuccess)
        {
            return Task.FromResult(Result<StoreProbeResult>.Success(new StoreProbeResult
            {
                State = read.Error!.Code == ErrorCode.UnsupportedDocumentVersion
                    ? StoreFileState.FutureVersion
                    : StoreFileState.Corrupt,
                SchemaVersion = 0,
                Details = read.Error.UserMessage,
            }));
        }

        var content = read.Require();
        var version = content.Manifest["version"]?.GetValue<int>() ?? 0;
        var mapped = MapProject(content);
        if (!mapped.IsSuccess) return Task.FromResult(Result<StoreProbeResult>.Failure(mapped.Error!));
        return Task.FromResult(Result<StoreProbeResult>.Success(new StoreProbeResult
        {
            State = StoreFileState.Valid,
            SchemaVersion = version,
            ProjectName = mapped.Require().Name,
        }));
    }

    public Task<Result<ProjectLoadResult>> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var read = ScriZipFormat.Read(filePath);
        if (!read.IsSuccess)
        {
            return Task.FromResult(Result<ProjectLoadResult>.Failure(read.Error!));
        }

        var content = read.Require();
        var mapped = MapProject(content);
        if (!mapped.IsSuccess) return Task.FromResult(Result<ProjectLoadResult>.Failure(mapped.Error!));
        var project = mapped.Require();
        project.SourceDocumentJson = Pack(content.Manifest, content.Content);

        return Task.FromResult(Result<ProjectLoadResult>.Success(new ProjectLoadResult
        {
            Project = project,
            SourceSchemaVersion = content.Manifest["version"]?.GetValue<int>() ?? 0,
            Warnings =
            [
                "Проект открыт из старого формата (ZIP: manifest.json + content.json). "
                + "При сохранении файл останется в этом формате, а поля, которых нет в редакторе, сохранятся как были.",
            ],
        }));
    }

    public Task<Result> SaveAsync(ProjectSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var document = Unpack(request.Project.SourceDocumentJson);
        if (document is null)
        {
            return Task.FromResult(Result.FromError(
                ErrorCode.ProjectFormatInvalid,
                "Не удалось восстановить исходный документ проекта.",
                "Откройте файл заново."));
        }

        ScriZipMapper.ApplyToContent(request.Project, document.Manifest, document.Content, DateTimeOffset.UtcNow);
        return Task.FromResult(ScriZipFormat.Write(request.FilePath, document.Manifest, document.Content, request.CreateBackup));
    }

    private static Result<ScriSelfStudio.Core.Model.Project> MapProject(ScriZipContent content)
    {
        try
        {
            return Result<ScriSelfStudio.Core.Model.Project>.Success(
                ScriZipMapper.ToProject(content.Manifest, content.Content, DateTimeOffset.UtcNow));
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or System.IO.InvalidDataException)
        {
            return Result<ScriSelfStudio.Core.Model.Project>.FromError(ErrorCode.ProjectFormatInvalid,
                "Не удалось прочитать данные проекта или историю сцен. Исходный файл не изменён.",
                technicalDetails: error.Message, exception: error);
        }
    }

    private static string Pack(JsonObject manifest, JsonObject content) => new JsonObject
    {
        [ManifestKey] = manifest.DeepClone(),
        [ContentKey] = content.DeepClone(),
    }.ToJsonString();

    private static ScriZipContent? Unpack(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) is JsonObject root &&
                   root[ManifestKey] is JsonObject manifest &&
                   root[ContentKey] is JsonObject content
                ? new ScriZipContent(manifest, content)
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
