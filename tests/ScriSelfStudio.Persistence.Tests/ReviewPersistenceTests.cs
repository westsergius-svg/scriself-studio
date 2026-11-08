namespace ScriSelfStudio.Persistence.Tests;

using Microsoft.Data.Sqlite;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Persistence.FileSystem;
using ScriSelfStudio.Persistence.Projects;
using ScriSelfStudio.Persistence.Sqlite;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.TestSupport;

public sealed class ReviewPersistenceTests
{
    [Fact]
    public async Task Review_survives_sqlite_recovery_scene_versions_and_failed_save()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Редактура.scri");
        var project = SampleProjects.CreateBookWithScenes();
        var scene = project.EnumerateScenes().First();
        var original = scene.Document.Clone();
        original.Review.TrackChanges = true;
        ReviewEditing.AddComment(original, 0, 6, "Проверить начало", DateTimeOffset.UtcNow);
        scene.Document = SampleProjects.DocumentWithText("Исправленный текст.");
        ReviewEditing.Capture(original, scene.Document, DateTimeOffset.UtcNow);
        var store = new SqliteProjectStore();
        Assert.True((await store.SaveAsync(new() { Project = project, FilePath = path, CreateBackup = false })).IsSuccess);
        var reopened = (await store.LoadAsync(path)).Require().Project.EnumerateScenes().First();
        Assert.Single(reopened.Document.Review.Comments);
        Assert.Single(reopened.Document.Review.Changes);
        Assert.True(ReviewEditing.Reject(reopened.Document, reopened.Document.Review.Changes[0].Id));
        Assert.Equal(SampleProjects.SceneText, reopened.Document.ToPlainText());
        Assert.Equal(TextFormat.Bold, reopened.Document.Blocks[0].Spans[0].Style.Flags);

        var recovery = new FileProjectRecoveryStore();
        await recovery.WriteAsync(project, path);
        var recovered = (await recovery.ReadAsync((await recovery.FindAsync(path)).Require()!)).Require();
        Assert.Single(recovered.EnumerateScenes().First().Document.Review.Changes);
        var service = new ProjectService(store, recovery, new EventBus());
        await service.OpenAsync(path);
        var version = service.CreateSceneVersion(scene.Id, 0, "Редактура").Require();
        await service.UpdateSceneDocumentAsync(new() { SceneId = scene.Id, BaseRevision = 0, Document = SampleProjects.DocumentWithText("Новый текст") });
        var restored = service.RestoreSceneVersion(scene.Id, version.Id, 1).Require();
        Assert.Single(restored.Document.Review.Comments);
        Assert.Single(restored.Document.Review.Changes);

        var failing = new InMemoryProjectStore();
        await failing.SaveAsync(new() { Project = project, FilePath = path, CreateBackup = false });
        var memory = new ProjectService(failing, recovery, new EventBus());
        await memory.OpenAsync(path);
        var copy = memory.GetSceneDocument(scene.Id).Require();
        copy.Document.Review.Comments[0].Text = "Сохранить после сбоя";
        await memory.UpdateSceneDocumentAsync(new() { SceneId = scene.Id, BaseRevision = copy.Revision, Document = copy.Document });
        failing.FailOnSave = true;
        Assert.False((await memory.SaveAsync()).IsSuccess);
        Assert.True(memory.IsDirty);
        Assert.Equal("Сохранить после сбоя", memory.GetSceneDocument(scene.Id).Require().Document.Review.Comments[0].Text);
        failing.FailOnSave = false;
        Assert.True((await memory.SaveAsync()).IsSuccess);
    }

    [Fact]
    public async Task Schema_five_migrates_with_backup_and_malformed_review_is_rejected()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("До редактуры.scri");
        var project = SampleProjects.CreateBookWithScenes();
        var store = new SqliteProjectStore();
        await store.SaveAsync(new() { Project = project, FilePath = path, CreateBackup = false });
        Execute("UPDATE schema_info SET version=5; UPDATE project SET schema_version=5;");
        var before = await File.ReadAllBytesAsync(path);
        Assert.True((await store.LoadAsync(path)).IsSuccess);
        Assert.Equal(before, await File.ReadAllBytesAsync(path + ".bak"));
        Execute("UPDATE scene_document SET document_json=json_set(document_json,'$.review.comments',json('[{\"start\":-1,\"length\":4}]'));");
        var corrupt = await File.ReadAllBytesAsync(path);
        Assert.False((await store.LoadAsync(path)).IsSuccess);
        Assert.Equal(corrupt, await File.ReadAllBytesAsync(path));
        void Execute(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
