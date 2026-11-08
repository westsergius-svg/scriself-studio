namespace ScriSelfStudio.Persistence.Projects;

using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Events;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;

public sealed partial class ProjectService
{
    public Result<IReadOnlyList<SceneVersionInfo>> GetSceneVersions(Guid sceneId)
    {
        var found = FindVersionScene(sceneId);
        return found.IsSuccess
            ? Result<IReadOnlyList<SceneVersionInfo>>.Success(found.Require().Versions
                .OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id).Select(VersionInfo).ToArray())
            : Result<IReadOnlyList<SceneVersionInfo>>.Failure(found.Error!);
    }

    public Result<SceneVersionDocument> GetSceneVersion(Guid sceneId, Guid versionId)
    {
        var found = FindVersionScene(sceneId);
        if (!found.IsSuccess) return Result<SceneVersionDocument>.Failure(found.Error!);
        var version = found.Require().Versions.FirstOrDefault(v => v.Id == versionId);
        return version is null
            ? Result<SceneVersionDocument>.FromError(ErrorCode.EntityNotFound, "Версия сцены не найдена.")
            : Result<SceneVersionDocument>.Success(new(VersionInfo(version), version.Document.Clone()));
    }

    public Result<SceneVersionInfo> CreateSceneVersion(Guid sceneId, long baseRevision, string name)
    {
        var found = FindVersionScene(sceneId, baseRevision);
        if (!found.IsSuccess) return Result<SceneVersionInfo>.Failure(found.Error!);
        var title = name.Trim();
        if (title.Length is 0 or > 120)
            return Result<SceneVersionInfo>.FromError(ErrorCode.ValidationFailed, "Название версии должно содержать от 1 до 120 символов.");
        var scene = found.Require();
        var version = MakeVersion(scene, title, false);
        scene.Versions.Add(version);
        SetDirty(true);
        return Result<SceneVersionInfo>.Success(VersionInfo(version));
    }

    public Result<SceneDocumentDto> RestoreSceneVersion(Guid sceneId, Guid versionId, long baseRevision)
    {
        var found = FindVersionScene(sceneId, baseRevision);
        if (!found.IsSuccess) return Result<SceneDocumentDto>.Failure(found.Error!);
        var scene = found.Require();
        var version = scene.Versions.FirstOrDefault(v => v.Id == versionId);
        if (version is null) return Result<SceneDocumentDto>.FromError(ErrorCode.EntityNotFound, "Версия сцены не найдена.");
        scene.Versions.Add(MakeVersion(scene, "Перед восстановлением «" + version.Name + "»", true));
        scene.Document = version.Document.Clone();
        scene.Revision++;
        scene.Words = WordCounting.Count(scene.Document);
        scene.Touch(_clock.UtcNow);
        SetDirty(true);
        _events.Publish(new SceneChanged(scene.Id, scene.Revision));
        return GetSceneDocument(sceneId);
    }

    public Result DeleteSceneVersion(Guid sceneId, Guid versionId)
    {
        var found = FindVersionScene(sceneId);
        if (!found.IsSuccess) return Result.Failure(found.Error!);
        if (found.Require().Versions.RemoveAll(v => v.Id == versionId) == 0)
            return Result.FromError(ErrorCode.EntityNotFound, "Версия сцены не найдена.");
        SetDirty(true);
        return Result.Success;
    }

    private Result<Scene> FindVersionScene(Guid id, long? revision = null)
    {
        if (_project is null) return Result<Scene>.FromError(ErrorCode.ProjectNotOpen);
        var scene = _project.FindScene(id);
        if (scene is null || scene.IsDeleted) return Result<Scene>.FromError(ErrorCode.EntityNotFound, "Сцена не найдена.");
        if (revision is { } expected && expected != scene.Revision)
            return Result<Scene>.FromError(ErrorCode.EditorOperationConflict, "Сцена изменилась. Откройте историю заново.");
        return Result<Scene>.Success(scene);
    }

    private SceneVersion MakeVersion(Scene scene, string name, bool safety) => new()
    {
        Id = Guid.NewGuid(), Name = name, CreatedAt = _clock.UtcNow,
        SourceRevision = scene.Revision, IsSafetySnapshot = safety, Document = scene.Document.Clone(),
    };

    private static SceneVersionInfo VersionInfo(SceneVersion version) => new(version.Id, version.Name,
        version.CreatedAt, version.SourceRevision, version.IsSafetySnapshot, WordCounting.Count(version.Document));
}
