namespace ScriSelfStudio.Persistence.Tests;

using Microsoft.Data.Sqlite;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Persistence.FileSystem;
using ScriSelfStudio.Persistence.Projects;
using ScriSelfStudio.Persistence.Sqlite;
using ScriSelfStudio.TestSupport;

public sealed class SceneVersionTests
{
    [Fact]
    public async Task Corrupt_history_fails_without_modifying_the_file()
    {
        using var workspace = new TempWorkspace();
        var (service, id, path) = await OpenSample(workspace);
        service.CreateSceneVersion(id, 0, "Версия");
        await service.SaveAsync();
        await service.CloseAsync(false);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE scene_version SET data_json='{broken';";
            command.ExecuteNonQuery();
        }
        var original = await File.ReadAllBytesAsync(path);
        var reopened = await service.OpenAsync(path);
        Assert.False(reopened.IsSuccess);
        Assert.Equal(ErrorCode.ProjectFormatInvalid, reopened.Error!.Code);
        Assert.Null(service.Current);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Saved_versions_survive_reopen_and_are_independent_of_live_text_and_returned_copies()
    {
        using var workspace = new TempWorkspace();
        var (service, id, path) = await OpenSample(workspace);
        var info = service.CreateSceneVersion(id, 0, "Первый вариант").Require();
        var copy = service.GetSceneVersion(id, info.Id).Require();
        copy.Document.Blocks[0].Spans[0].Text = "Изменённая копия";
        await service.UpdateSceneDocumentAsync(new() { SceneId = id, BaseRevision = 0, Document = SampleProjects.DocumentWithText("Новый текст") });
        Assert.True((await service.SaveAsync()).IsSuccess);
        await service.CloseAsync(false);
        Assert.True((await service.OpenAsync(path)).IsSuccess);
        var saved = service.GetSceneVersion(id, info.Id).Require();
        Assert.Equal(SampleProjects.SceneText, saved.Document.ToPlainText());
        Assert.Equal(TextFormat.Bold, saved.Document.Blocks[0].Spans[0].Style.Flags);
        Assert.Equal("Новый текст", service.GetSceneDocument(id).Require().Document.ToPlainText());
    }

    [Fact]
    public async Task Restore_preserves_current_text_as_a_version_and_rejects_stale_revision()
    {
        using var workspace = new TempWorkspace();
        var (service, id, _) = await OpenSample(workspace);
        var saved = service.CreateSceneVersion(id, 0, "Начало").Require();
        await service.UpdateSceneDocumentAsync(new() { SceneId = id, BaseRevision = 0,
            Document = SampleProjects.DocumentWithText("Рабочий вариант", TextFormat.Italic) });
        Assert.Equal(ErrorCode.EditorOperationConflict, service.RestoreSceneVersion(id, saved.Id, 0).Error!.Code);
        Assert.Single(service.GetSceneVersions(id).Require());
        var restored = service.RestoreSceneVersion(id, saved.Id, 1).Require();
        Assert.Equal(2, restored.Revision);
        Assert.Equal(SampleProjects.SceneText, restored.Document.ToPlainText());
        var safety = service.GetSceneVersions(id).Require().Single(v => v.IsSafetySnapshot);
        var safetyDocument = service.GetSceneVersion(id, safety.Id).Require().Document;
        Assert.Equal("Рабочий вариант", safetyDocument.ToPlainText());
        Assert.Equal(TextFormat.Italic, safetyDocument.Blocks[0].Spans[0].Style.Flags);
        Assert.True((await service.SaveAsync()).IsSuccess);
        Assert.True(service.DeleteSceneVersion(id, saved.Id).IsSuccess);
        Assert.Equal(SampleProjects.SceneText, service.GetSceneDocument(id).Require().Document.ToPlainText());
        Assert.Single(service.GetSceneVersions(id).Require());
    }

    [Fact]
    public async Task Migration_from_v4_keeps_original_backup_and_recovery_keeps_versions()
    {
        using var workspace = new TempWorkspace();
        var (service, id, path) = await OpenSample(workspace);
        await service.CloseAsync(true);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE scene_version; UPDATE schema_info SET version=4; UPDATE project SET schema_version=4;";
            command.ExecuteNonQuery();
        }
        var original = await File.ReadAllBytesAsync(path);
        Assert.True((await service.OpenAsync(path)).IsSuccess);
        Assert.Equal(original, await File.ReadAllBytesAsync(path + ".bak"));
        Assert.Empty(service.GetSceneVersions(id).Require());

        var project = SampleProjects.CreateBookWithScenes();
        var scene = project.EnumerateScenes().First();
        scene.Versions.Add(new() { Id = Guid.NewGuid(), Name = "До правки", Document = scene.Document.Clone() });
        var recovery = new FileProjectRecoveryStore();
        Assert.True((await recovery.WriteAsync(project, path)).IsSuccess);
        var found = (await recovery.FindAsync(path)).Require()!;
        var recovered = (await recovery.ReadAsync(found)).Require().EnumerateScenes().First();
        Assert.Single(recovered.Versions);
        Assert.Equal(SampleProjects.SceneText, recovered.Versions[0].Document.ToPlainText());
    }

    [Fact]
    public async Task Invalid_requests_do_not_add_versions_or_change_text()
    {
        using var workspace = new TempWorkspace();
        var (service, id, _) = await OpenSample(workspace);
        Assert.False(service.CreateSceneVersion(id, 0, "   ").IsSuccess);
        Assert.False(service.CreateSceneVersion(id, 99, "Устаревшая").IsSuccess);
        Assert.False(service.RestoreSceneVersion(id, Guid.NewGuid(), 0).IsSuccess);
        Assert.Empty(service.GetSceneVersions(id).Require());
        Assert.Equal(SampleProjects.SceneText, service.GetSceneDocument(id).Require().Document.ToPlainText());
    }

    private static async Task<(ProjectService Service, Guid Id, string Path)> OpenSample(TempWorkspace workspace)
    {
        var path = workspace.FilePath("Версии.scri");
        var project = SampleProjects.CreateBookWithScenes();
        var store = new SqliteProjectStore();
        Assert.True((await store.SaveAsync(new ProjectSaveRequest { Project = project, FilePath = path, CreateBackup = false })).IsSuccess);
        var service = new ProjectService(store, new FileProjectRecoveryStore(), new EventBus(), new FakeClock());
        Assert.True((await service.OpenAsync(path)).IsSuccess);
        return (service, project.EnumerateScenes().First().Id, path);
    }
}
