namespace ScriSelfStudio.Persistence.Tests;

using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Persistence.FileSystem;
using ScriSelfStudio.Persistence.Projects;
using ScriSelfStudio.Persistence.Sqlite;
using ScriSelfStudio.TestSupport;

public sealed class SaveIntegrityTests
{
    [Fact]
    public async Task Edit_while_save_finishes_stays_dirty_and_has_current_recovery()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("revision.scri");
        var store = new PausingStore();
        var recovery = new FileProjectRecoveryStore();
        using var service = new ProjectService(store, recovery, new EventBus());
        Assert.True((await service.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Original" })).IsSuccess);
        Rename(service, "Written revision");
        store.PauseNextSave(afterWrite: true);
        var saving = service.SaveAsync();
        await store.Entered.Task;
        Rename(service, "Newer revision");
        store.Release.TrySetResult();
        Assert.True((await saving).IsSuccess);

        Assert.True(service.IsDirty);
        Assert.Equal("Newer revision", service.Current!.Name);
        Assert.Equal("Written revision", (await store.LoadAsync(path)).Require().Project.Name);
        Assert.False((await service.CloseAsync(discardChanges: false)).IsSuccess);
        var info = (await recovery.FindAsync(path)).Require();
        Assert.NotNull(info);
        Assert.Equal("Newer revision", (await recovery.ReadAsync(info)).Require().Name);

        Assert.True((await service.SaveAsync()).IsSuccess);
        Assert.False(service.IsDirty);
        Assert.Equal("Newer revision", (await store.LoadAsync(path)).Require().Project.Name);
        Assert.Null((await recovery.FindAsync(path)).Require());
    }

    [Fact]
    public async Task Save_uses_a_detached_snapshot_and_queues_the_next_revision()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("queued.scri");
        var store = new PausingStore();
        using var service = new ProjectService(store, new FileProjectRecoveryStore(), new EventBus());
        Assert.True((await service.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Original" })).IsSuccess);
        Rename(service, "First");
        store.PauseNextSave(afterWrite: false);
        var first = service.SaveAsync();
        await store.Entered.Task;
        Rename(service, "Second");
        Assert.Equal("First", store.PausedProject!.Name);
        var second = service.SaveAsync();
        Assert.False(second.IsCompleted);
        store.Release.TrySetResult();
        Assert.True((await first).IsSuccess);
        Assert.True((await second).IsSuccess);

        Assert.Equal(1, store.MaxConcurrentSaves);
        Assert.Equal("Second", (await store.LoadAsync(path)).Require().Project.Name);
        Assert.False(service.IsDirty);
    }

    [Fact]
    public async Task Stale_owner_keeps_its_draft_and_can_save_a_separate_copy()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("shared.scri");
        using var first = NewService();
        using var second = NewService();
        Assert.True((await first.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Original" })).IsSuccess);
        Assert.True((await second.OpenAsync(path)).IsSuccess);
        Rename(first, "First owner's title");
        Assert.True((await first.SaveAsync()).IsSuccess);
        second.Mutate(project => { project.Description = "Second owner's draft"; return Result.Success; });

        var staleSave = await second.SaveAsync();
        Assert.Equal(ErrorCode.ProjectFileConflict, staleSave.Error!.Code);
        Assert.True(second.IsDirty);
        Assert.Equal("First owner's title", (await new SqliteProjectStore().LoadAsync(path)).Require().Project.Name);

        var copyPath = workspace.FilePath("conflict-copy.scri");
        Assert.True((await second.SaveAsAsync(copyPath)).IsSuccess);
        var copy = (await new SqliteProjectStore().LoadAsync(copyPath)).Require().Project;
        Assert.Equal("Original", copy.Name);
        Assert.Equal("Second owner's draft", copy.Description);
        Assert.Equal("First owner's title", (await new SqliteProjectStore().LoadAsync(path)).Require().Project.Name);
    }

    [Fact]
    public async Task Another_owner_cannot_open_or_save_while_the_first_is_writing()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("concurrent-owners.scri");
        var store = new PausingStore();
        var recovery = new FileProjectRecoveryStore();
        using var first = new ProjectService(store, recovery, new EventBus());
        using var second = NewService();
        using var third = NewService();
        Assert.True((await first.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Original" })).IsSuccess);
        Assert.True((await second.OpenAsync(path)).IsSuccess);
        Rename(first, "First owner's revision");
        Rename(second, "Second owner's draft");
        store.PauseNextSave(afterWrite: false);
        var saving = first.SaveAsync();
        try
        {
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ErrorCode.ProjectFileConflict, (await second.SaveAsync()).Error!.Code);
            Assert.True(second.IsDirty);
            Assert.Equal("Second owner's draft", second.Current!.Name);
            Assert.Equal(ErrorCode.ProjectFileConflict, (await third.OpenAsync(path)).Error!.Code);
            Assert.Null(third.Current);
            var info = (await recovery.FindAsync(path)).Require();
            Assert.NotNull(info);
            Assert.Equal("First owner's revision", (await recovery.ReadAsync(info)).Require().Name);
        }
        finally
        {
            store.Release.TrySetResult();
            await saving;
        }
        Assert.True((await saving).IsSuccess);
        Assert.Equal("First owner's revision", (await store.LoadAsync(path)).Require().Project.Name);
        Assert.Equal(ErrorCode.ProjectFileConflict, (await second.SaveAsync()).Error!.Code);
        Assert.True(second.IsDirty);
        Assert.True((await third.OpenAsync(path)).IsSuccess);
        Assert.Equal("First owner's revision", third.Current!.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Externally_replaced_or_deleted_file_is_not_silently_overwritten(bool delete)
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("external.scri");
        using var service = NewService();
        Assert.True((await service.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Original" })).IsSuccess);
        Rename(service, "Unsaved local title");
        if (delete) File.Delete(path);
        else await ReplaceExternallyAsync(path, "External title");

        var saved = await service.SaveAsync();
        Assert.Equal(ErrorCode.ProjectFileConflict, saved.Error!.Code);
        Assert.True(service.IsDirty);
        Assert.Equal("Unsaved local title", service.Current!.Name);
        Assert.Equal(!delete, File.Exists(path));
        if (!delete) Assert.Equal("External title", (await new SqliteProjectStore().LoadAsync(path)).Require().Project.Name);
    }

    [Fact]
    public async Task External_write_during_save_is_checked_before_atomic_replacement()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("during-save.scri");
        var store = new PausingStore();
        using var service = new ProjectService(store, new FileProjectRecoveryStore(), new EventBus());
        Assert.True((await service.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Original" })).IsSuccess);
        Rename(service, "Local draft");
        store.PauseNextSave(afterWrite: false);
        var saving = service.SaveAsync();
        await store.Entered.Task;
        await ReplaceExternallyAsync(path, "External title");
        store.Release.TrySetResult();

        Assert.Equal(ErrorCode.ProjectFileConflict, (await saving).Error!.Code);
        Assert.True(service.IsDirty);
        Assert.Equal("External title", (await store.LoadAsync(path)).Require().Project.Name);
        Assert.Empty(Directory.EnumerateFiles(workspace.Root, "*.tmp"));
    }

    [Fact]
    public async Task Close_waits_for_save_and_retains_a_newer_unsaved_revision()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("closing.scri");
        var store = new PausingStore();
        using var service = new ProjectService(store, new FileProjectRecoveryStore(), new EventBus());
        Assert.True((await service.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Original" })).IsSuccess);
        Rename(service, "Written");
        store.PauseNextSave(afterWrite: true);
        var saving = service.SaveAsync();
        await store.Entered.Task;
        var closing = service.CloseAsync(discardChanges: false);
        Assert.False(closing.IsCompleted);
        Rename(service, "Newer draft");
        store.Release.TrySetResult();
        Assert.True((await saving).IsSuccess);
        Assert.False((await closing).IsSuccess);
        Assert.Equal("Newer draft", service.Current!.Name);
        Assert.True(service.IsDirty);
    }

    [Fact]
    public async Task Corrupt_scene_json_returns_a_format_error_and_keeps_the_original_file()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("corrupt.scri");
        using var service = NewService();
        Assert.True((await service.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Original" })).IsSuccess);
        var sceneId = Guid.Empty;
        service.Mutate(project =>
        {
            var book = ProjectFactory.CreateBook(project, project.Series[0], "Book");
            var chapter = ProjectFactory.CreateChapter(book, "Chapter");
            var scene = ProjectFactory.CreateScene(chapter, "Scene");
            scene.Document = SampleProjects.DocumentWithText("Text to preserve");
            sceneId = scene.Id;
            chapter.Scenes.Add(scene);
            book.Chapters.Add(chapter);
            project.Series[0].Books.Add(book);
            return Result.Success;
        });
        Assert.True((await service.SaveAsync()).IsSuccess);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE scene_document SET document_json = '{';";
            command.ExecuteNonQuery();
        }
        var hashBefore = SHA256.HashData(File.ReadAllBytes(path));
        var loaded = await new SqliteProjectStore().LoadAsync(path);
        Assert.Equal(ErrorCode.ProjectFormatInvalid, loaded.Error!.Code);
        Assert.Contains(sceneId.ToString(), loaded.Error.TechnicalDetails);
        Assert.Equal(hashBefore, SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static ProjectService NewService() => new(new SqliteProjectStore(), new FileProjectRecoveryStore(), new EventBus());

    private static void Rename(ProjectService service, string name) =>
        Assert.True(service.Mutate(project => { project.Name = name; return Result.Success; }).IsSuccess);

    private static async Task ReplaceExternallyAsync(string path, string name)
    {
        var store = new SqliteProjectStore();
        var project = (await store.LoadAsync(path)).Require().Project;
        project.Name = name;
        Assert.True((await store.SaveAsync(new ProjectSaveRequest { Project = project, FilePath = path, CreateBackup = false })).IsSuccess);
    }

    private sealed class PausingStore : IProjectStore
    {
        private readonly SqliteProjectStore _inner = new();
        private bool _pause;
        private bool _afterWrite;
        private int _active;
        public TaskCompletionSource Entered { get; private set; } = NewSignal();
        public TaskCompletionSource Release { get; private set; } = NewSignal();
        public Project? PausedProject { get; private set; }
        public int MaxConcurrentSaves { get; private set; }

        public void PauseNextSave(bool afterWrite)
        {
            _pause = true;
            _afterWrite = afterWrite;
            Entered = NewSignal();
            Release = NewSignal();
        }

        public Task<Result<StoreProbeResult>> ProbeAsync(string filePath, CancellationToken cancellationToken = default) =>
            _inner.ProbeAsync(filePath, cancellationToken);

        public Task<Result<ProjectLoadResult>> LoadAsync(string filePath, CancellationToken cancellationToken = default) =>
            _inner.LoadAsync(filePath, cancellationToken);

        public async Task<Result> SaveAsync(ProjectSaveRequest request, CancellationToken cancellationToken = default)
        {
            var pause = _pause;
            _pause = false;
            MaxConcurrentSaves = Math.Max(MaxConcurrentSaves, Interlocked.Increment(ref _active));
            try
            {
                if (pause && !_afterWrite) await PauseAsync(request, cancellationToken);
                var saved = await _inner.SaveAsync(request, cancellationToken);
                if (pause && _afterWrite) await PauseAsync(request, cancellationToken);
                return saved;
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        private async Task PauseAsync(ProjectSaveRequest request, CancellationToken cancellationToken)
        {
            PausedProject = request.Project;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
