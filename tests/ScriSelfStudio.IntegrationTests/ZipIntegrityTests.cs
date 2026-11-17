namespace ScriSelfStudio.IntegrationTests;

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Json;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Persistence.FileSystem;
using ScriSelfStudio.Persistence.Formats;
using ScriSelfStudio.Persistence.Projects;
using ScriSelfStudio.TestSupport;

public sealed class ZipIntegrityTests
{
    [Fact]
    public async Task New_scenes_in_multiple_books_keep_text_format_and_versions_after_repeated_saves()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("multiple.scri");
        WriteArchive(path, Content());
        using var projects = NewService();
        Assert.True((await projects.OpenAsync(path)).IsSuccess);
        var expected = new Dictionary<Guid, string>();
        projects.Mutate(project =>
        {
            var series = project.Series[0];
            series.Books.Clear();
            for (var bookIndex = 0; bookIndex < 2; bookIndex++)
            {
                var book = ProjectFactory.CreateBook(project, series, $"Book {bookIndex}");
                series.Books.Add(book);
                for (var chapterIndex = 0; chapterIndex < 2; chapterIndex++)
                {
                    var chapter = ProjectFactory.CreateChapter(book, $"Chapter {chapterIndex}");
                    book.Chapters.Add(chapter);
                    for (var sceneIndex = 0; sceneIndex < 2; sceneIndex++)
                    {
                        var scene = ProjectFactory.CreateScene(chapter, $"Scene {sceneIndex}");
                        var text = $"UNIQUE {bookIndex}/{chapterIndex}/{sceneIndex}";
                        scene.Document = SampleProjects.DocumentWithText(text, TextFormat.Bold | TextFormat.Italic);
                        expected.Add(scene.Id, text);
                        chapter.Scenes.Add(scene);
                    }
                }
            }
            return Result.Success;
        });
        foreach (var id in expected.Keys) Assert.True(projects.CreateSceneVersion(id, 0, "Original version").IsSuccess);
        Assert.True((await projects.SaveAsync()).IsSuccess);
        Assert.True((await projects.SaveAsync()).IsSuccess);
        var content = ReadContent(path);
        var legacyIds = content["chapters"]!.AsArray().OfType<JsonObject>()
            .SelectMany(chapter => chapter["scenes"]!.AsArray().OfType<JsonObject>())
            .Select(scene => scene["id"]!.GetValue<string>()).ToArray();
        Assert.Equal(8, legacyIds.Distinct(StringComparer.Ordinal).Count());
        Assert.True((await projects.CloseAsync(false)).IsSuccess);
        Assert.True((await projects.OpenAsync(path)).IsSuccess);
        foreach (var (id, text) in expected)
        {
            var document = projects.GetSceneDocument(id).Require().Document;
            Assert.Equal(text, document.ToPlainText());
            Assert.Equal(TextFormat.Bold | TextFormat.Italic, document.Blocks[0].Spans[0].Style.Flags);
            var version = Assert.Single(projects.GetSceneVersions(id).Require());
            Assert.Equal(text, projects.GetSceneVersion(id, version.Id).Require().Document.ToPlainText());
        }

        // Старый читатель видит прежние поля и однозначные legacy-ключи.
        var extras = content["scriself"]!.AsObject();
        extras.Remove("documentsBySceneId");
        extras.Remove("sceneVersionsBySceneId");
        WriteArchive(path, content);
        using var oldShape = NewService();
        Assert.True((await oldShape.OpenAsync(path)).IsSuccess);
        foreach (var (id, text) in expected)
        {
            Assert.Equal(text, oldShape.GetSceneDocument(id).Require().Document.ToPlainText());
            Assert.Equal(TextFormat.Bold | TextFormat.Italic, oldShape.GetSceneDocument(id).Require().Document.Blocks[0].Spans[0].Style.Flags);
            Assert.Single(oldShape.GetSceneVersions(id).Require());
        }
    }

    [Fact]
    public async Task Bare_legacy_IDs_repeated_in_different_chapters_are_remapped_without_losing_text()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("bare.scri");
        WriteArchive(path, TwoChapters());
        using var projects = NewService();
        Assert.True((await projects.OpenAsync(path)).IsSuccess);
        Assert.True((await projects.SaveAsync()).IsSuccess);
        Assert.True((await projects.CloseAsync(false)).IsSuccess);
        Assert.True((await projects.OpenAsync(path)).IsSuccess);
        var chapters = projects.Current!.Series[0].Books[0].Chapters;
        Assert.Equal("ALPHA", projects.GetSceneDocument(chapters[0].Scenes[0].Id).Require().Document.ToPlainText());
        Assert.Equal("BETA", projects.GetSceneDocument(chapters[1].Scenes[0].Id).Require().Document.ToPlainText());
    }

    [Fact]
    public async Task Ambiguous_old_document_key_returns_an_error_and_keeps_the_archive()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("ambiguous.scri");
        var content = TwoChapters();
        content["scriself"] = new JsonObject { ["documents"] = new JsonObject { ["scene_001"] = Document("BETA") } };
        WriteArchive(path, content);
        var original = await File.ReadAllBytesAsync(path);
        using var projects = NewService();
        var opened = await projects.OpenAsync(path);
        Assert.Equal(ErrorCode.ProjectFormatInvalid, opened.Error!.Code);
        Assert.Contains("scene_001", opened.Error.TechnicalDetails);
        Assert.Null(projects.Current);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Canonical_document_wins_over_an_incorrect_legacy_alias()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("canonical.scri");
        WriteArchive(path, Content(Chapter("chapter_001", Scene("scene_001", "RIGHT"))));
        using (var projects = NewService())
        {
            Assert.True((await projects.OpenAsync(path)).IsSuccess);
            Assert.True((await projects.SaveAsync()).IsSuccess);
        }
        var content = ReadContent(path);
        content["scriself"]!["documents"]!["scene_001"] = Document("WRONG");
        WriteArchive(path, content);
        using var reopened = NewService();
        Assert.True((await reopened.OpenAsync(path)).IsSuccess);
        var sceneId = reopened.Current!.Series[0].Books[0].Chapters[0].Scenes[0].Id;
        Assert.Equal("RIGHT", reopened.GetSceneDocument(sceneId).Require().Document.ToPlainText());
    }

    [Fact]
    public async Task New_legacy_key_cannot_collide_with_an_existing_guid_like_key()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("reserved.scri");
        var id = Guid.NewGuid();
        WriteArchive(path, Content(Chapter("chapter_001", Scene("scene_001", "FIRST"), Scene($"scene_{id:N}", "SECOND"))));
        using var projects = NewService();
        Assert.True((await projects.OpenAsync(path)).IsSuccess);
        projects.Mutate(project =>
        {
            var chapter = project.Series[0].Books[0].Chapters[0];
            var scene = ProjectFactory.CreateScene(chapter, "Third");
            scene.Id = id;
            scene.Document = SampleProjects.DocumentWithText("THIRD");
            chapter.Scenes.Add(scene);
            return Result.Success;
        });
        Assert.True((await projects.SaveAsync()).IsSuccess);
        var ids = ReadContent(path)["chapters"]![0]!["scenes"]!.AsArray()
            .Select(scene => scene!["id"]!.GetValue<string>()).ToArray();
        Assert.Equal(3, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("scene_001", ids);
        Assert.Contains($"scene_{id:N}", ids);
        Assert.True((await projects.CloseAsync(false)).IsSuccess);
        Assert.True((await projects.OpenAsync(path)).IsSuccess);
        Assert.Equal("THIRD", projects.GetSceneDocument(id).Require().Document.ToPlainText());
    }

    [Fact]
    public async Task Duplicate_canonical_scene_ID_is_rejected_without_changing_the_file()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("duplicate-guid.scri");
        var first = Scene("scene_001", "FIRST");
        var second = Scene("scene_002", "SECOND");
        var id = Guid.NewGuid().ToString();
        first["scriselfId"] = id;
        second["scriselfId"] = id;
        WriteArchive(path, Content(Chapter("chapter_001", first, second)));
        var original = await File.ReadAllBytesAsync(path);
        using var projects = NewService();
        Assert.Equal(ErrorCode.ProjectFormatInvalid, (await projects.OpenAsync(path)).Error!.Code);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    private static ProjectService NewService() => new(new ScriZipProjectStore(), new FileProjectRecoveryStore(), new EventBus());

    private static JsonObject TwoChapters() => Content(Chapter("chapter_001", Scene("scene_001", "ALPHA")),
        Chapter("chapter_002", Scene("scene_001", "BETA")));

    private static JsonObject Content(params JsonObject[] chapters) => new()
    {
        ["book"] = new JsonObject { ["title"] = "Integrity test" },
        ["chapters"] = new JsonArray(chapters.Cast<JsonNode?>().ToArray()),
    };

    private static JsonObject Chapter(string id, params JsonObject[] scenes) => new()
    {
        ["id"] = id, ["title"] = id, ["scenes"] = new JsonArray(scenes.Cast<JsonNode?>().ToArray()),
    };

    private static JsonObject Scene(string id, string text) => new() { ["id"] = id, ["title"] = id, ["text"] = text };

    private static JsonNode? Document(string text) => JsonSerializer.SerializeToNode(
        SampleProjects.DocumentWithText(text), CoreJsonContext.Default.RichDocument);

    private static void WriteArchive(string path, JsonObject content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                writer.Write(new JsonObject { ["format"] = "scri", ["version"] = 1, ["name"] = "Integrity test" }.ToJsonString());
            using (var writer = new StreamWriter(archive.CreateEntry("content.json").Open())) writer.Write(content.ToJsonString());
        }
        File.WriteAllBytes(path, buffer.ToArray());
    }

    private static JsonObject ReadContent(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("content.json")!.Open());
        return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }
}
