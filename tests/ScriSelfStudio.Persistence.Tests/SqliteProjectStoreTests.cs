namespace ScriSelfStudio.Persistence.Tests;

using System.IO;
using Microsoft.Data.Sqlite;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Schema;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Persistence.Sqlite;
using ScriSelfStudio.TestSupport;

public sealed class SqliteProjectStoreTests
{
    [Fact]
    public async Task Save_then_load_preserves_structure_text_and_formatting()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Роман.scri");
        var store = new SqliteProjectStore();
        var project = SampleProjects.CreateBookWithScenes();
        var scene = project.EnumerateScenes().First();
        scene.Revision = 3;
        scene.Metadata.Tags.Add("ёжик");

        var saved = await store.SaveAsync(new ProjectSaveRequest { Project = project, FilePath = path, CreateBackup = false });
        Assert.True(saved.IsSuccess, saved.Error?.TechnicalDetails);

        var loaded = await store.LoadAsync(path);
        Assert.True(loaded.IsSuccess, loaded.Error?.TechnicalDetails);

        var restored = loaded.Require().Project;
        Assert.Equal(project.Id, restored.Id);
        Assert.Equal("Роман", restored.Name);
        Assert.Equal(SchemaVersion.Current, restored.SchemaVersion);

        var restoredScene = restored.EnumerateScenes().Single(candidate => candidate.Title == scene.Title);
        Assert.Equal(3, restoredScene.Revision);
        Assert.Equal(SampleProjects.SceneText, restoredScene.Document.ToPlainText());
        Assert.Equal(TextFormat.Bold, restoredScene.Document.Blocks[0].Spans[0].Style.Flags);
        Assert.Contains("ёжик", restoredScene.Metadata.Tags);
        Assert.Equal(TextFormat.None, restoredScene.Document.Blocks[1].Spans[0].Style.Flags);
    }

    [Fact]
    public async Task Save_writes_current_schema_version_and_removes_temp_file()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var store = new SqliteProjectStore();

        await store.SaveAsync(new ProjectSaveRequest
        {
            Project = SampleProjects.CreateBookWithScenes(),
            FilePath = path,
            CreateBackup = false,
        });

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(SchemaVersion.Current, ReadSchemaVersion(path));
    }

    [Fact]
    public async Task Save_creates_backup_of_existing_file()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var store = new SqliteProjectStore();
        var project = SampleProjects.CreateBookWithScenes();

        await store.SaveAsync(new ProjectSaveRequest { Project = project, FilePath = path, CreateBackup = false });
        var firstLength = new FileInfo(path).Length;

        project.Name = "Изменённое имя";
        await store.SaveAsync(new ProjectSaveRequest { Project = project, FilePath = path, CreateBackup = true });

        Assert.True(File.Exists(path + ".bak"));
        Assert.True(firstLength > 0);
        Assert.Equal("Изменённое имя", (await store.LoadAsync(path)).Require().Project.Name);
        Assert.Equal("Роман", (await store.LoadAsync(path + ".bak")).Require().Project.Name);
    }

    [Fact]
    public async Task Probe_reports_missing_corrupt_and_valid_files()
    {
        using var workspace = new TempWorkspace();
        var store = new SqliteProjectStore();
        var missing = workspace.FilePath("Нет.scri");
        var corrupt = workspace.FilePath("Битый.scri");
        var valid = workspace.FilePath("Хороший.scri");

        await File.WriteAllTextAsync(corrupt, "это не файл проекта");
        await store.SaveAsync(new ProjectSaveRequest
        {
            Project = SampleProjects.CreateBookWithScenes(),
            FilePath = valid,
            CreateBackup = false,
        });

        Assert.Equal(StoreFileState.Missing, (await store.ProbeAsync(missing)).Require().State);
        Assert.Equal(StoreFileState.Corrupt, (await store.ProbeAsync(corrupt)).Require().State);

        var validProbe = (await store.ProbeAsync(valid)).Require();
        Assert.Equal(StoreFileState.Valid, validProbe.State);
        Assert.Equal("Роман", validProbe.ProjectName);
    }

    [Fact]
    public async Task Load_reports_format_error_for_corrupt_file()
    {
        using var workspace = new TempWorkspace();
        var corrupt = workspace.FilePath("Битый.scri");
        await File.WriteAllTextAsync(corrupt, "не база данных");

        var result = await new SqliteProjectStore().LoadAsync(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ProjectFormatInvalid, result.Error!.Code);
    }

    [Fact]
    public async Task Load_reports_missing_file()
    {
        using var workspace = new TempWorkspace();

        var result = await new SqliteProjectStore().LoadAsync(workspace.FilePath("Нет.scri"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ProjectNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Future_schema_version_is_rejected_on_load_and_probe()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Будущий.scri");
        var store = new SqliteProjectStore();
        await store.SaveAsync(new ProjectSaveRequest
        {
            Project = SampleProjects.CreateBookWithScenes(),
            FilePath = path,
            CreateBackup = false,
        });

        ExecuteSql(path, $"UPDATE schema_info SET version = {SchemaVersion.Current + 5};");

        var probe = (await store.ProbeAsync(path)).Require();
        Assert.Equal(StoreFileState.FutureVersion, probe.State);

        var load = await store.LoadAsync(path);
        Assert.False(load.IsSuccess);
        Assert.Equal(ErrorCode.UnsupportedDocumentVersion, load.Error!.Code);
    }

    [Fact]
    public async Task Failed_save_keeps_existing_file_untouched()
    {
        using var workspace = new TempWorkspace();
        var store = new SqliteProjectStore();
        var blocker = workspace.FilePath("препятствие");
        await File.WriteAllTextAsync(blocker, "файл вместо папки");

        var result = await store.SaveAsync(new ProjectSaveRequest
        {
            Project = SampleProjects.CreateBookWithScenes(),
            FilePath = Path.Combine(blocker, "Проект.scri"),
            CreateBackup = true,
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.StorageWriteFailed, result.Error!.Code);
        Assert.Equal("файл вместо папки", await File.ReadAllTextAsync(blocker));
    }

    [Fact]
    public async Task Scene_without_document_row_loads_with_warning()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var store = new SqliteProjectStore();
        var project = SampleProjects.CreateBookWithScenes();
        await store.SaveAsync(new ProjectSaveRequest { Project = project, FilePath = path, CreateBackup = false });

        ExecuteSql(path, "DELETE FROM scene_document;");

        var loaded = (await store.LoadAsync(path)).Require();

        Assert.NotEmpty(loaded.Warnings);
        Assert.All(loaded.Project.EnumerateScenes(), scene => Assert.True(scene.Document.IsEmpty()));
    }

    private static int ReadSchemaVersion(string path)
    {
        using var connection = new SqliteConnection(BuildConnectionString(path));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_info WHERE id = 1;";
        return (int)(long)command.ExecuteScalar()!;
    }

    private static void ExecuteSql(string path, string sql)
    {
        using var connection = new SqliteConnection(BuildConnectionString(path));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string BuildConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false,
    }.ToString();
}
