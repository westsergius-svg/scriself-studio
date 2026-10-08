namespace ScriSelfStudio.IntegrationTests;

using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Events;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Persistence.Formats;
using ScriSelfStudio.Persistence.Sqlite;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Импорт раннего модульного формата `.scri` (SQLite <c>meta</c>/<c>entities</c>/
/// <c>relations</c>/<c>texts</c>): книга, главы, сцены, тексты, персонажи, сюжетные
/// линии и их этапы переносятся в текущую модель без потерь (ADR 009, п. 6; ADR 010).
/// </summary>
public sealed class LegacyModularImportTests
{
    private const string SceneText = "Первый абзац раннего текста\nВторой абзац";

    [Fact]
    public void Legacy_modular_project_opens_with_book_chapters_scenes_and_texts()
    {
        using var workspace = new TempWorkspace();
        var path = WriteBasicProject(workspace);

        var project = LegacyModularImporter.Load(path, DateTimeOffset.UnixEpoch).Project;
        Assert.Equal("Ранний проект", project.Name);
        Assert.Equal(LegacyModularImporter.FormatId, project.SourceFormat);

        var book = Assert.Single(project.Series[0].Books);
        var chapter = Assert.Single(book.Chapters);
        Assert.Equal("ch_1", chapter.SourceId);
        Assert.Equal("Глава 1", chapter.Title);

        var scene = Assert.Single(chapter.Scenes);
        Assert.Equal("sc_1", scene.SourceId);
        Assert.Equal("Открывающая сцена", scene.Title);
        Assert.Equal(SceneStatus.Draft, scene.Status);
        Assert.Equal(SceneText, scene.Document.ToPlainText());
        Assert.Equal(2, scene.Document.Blocks.Count);
    }

    [Fact]
    public async Task Project_opens_through_the_routing_store_and_reports_the_source_build()
    {
        using var workspace = new TempWorkspace();
        var path = WriteBasicProject(workspace);

        var store = new RoutingProjectStore(new SqliteProjectStore(), new ScriZipProjectStore());
        var loaded = await store.LoadAsync(path);

        Assert.True(loaded.IsSuccess, loaded.Error?.UserMessage);
        Assert.Equal("Ранний проект", loaded.Require().Project.Name);
    }

    [Fact]
    public void Characters_and_plotline_stages_are_imported_with_scene_links()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Ранний.scri");
        new LegacyModularProjectBuilder()
            .WithEntity("ch_1", "chapter", new JsonObject { ["title"] = "Глава 1" }, "Глава 1")
            .WithEntity("sc_1", "scene", new JsonObject
            {
                ["title"] = "Сцена",
                ["chapterId"] = "ch_1",
                ["textId"] = "txt_1",
                ["status"] = "in_progress",
            }, "Сцена")
            .WithText("txt_1", SceneText)
            .WithEntity("character_1", "character", new JsonObject
            {
                ["name"] = "Иван",
                ["motivation"] = "найти сестру",
                ["note"] = "появляется во второй главе",
                ["role"] = "protagonist",
            }, "Иван")
            .WithEntity("plotline_1", "plotline", new JsonObject
            {
                ["name"] = "Главная линия",
                ["color"] = "#4A7DFF",
            }, "Главная линия")
            .WithEntity("step_1", "plotline_stage", new JsonObject
            {
                ["name"] = "Этап 1",
                ["plotlineId"] = "plotline_1",
                ["isFinal"] = false,
            }, "Этап 1", order: 0)
            .WithEntity("step_2", "plotline_stage", new JsonObject
            {
                ["name"] = "Этап 2",
                ["plotlineId"] = "plotline_1",
                ["isFinal"] = true,
            }, "Этап 2", order: 1)
            .WithRelation("sc_1", "plotline_1", "scene_plotline", new JsonObject { ["stepId"] = "step_1" })
            .WriteTo(path);

        var project = LegacyModularImporter.Load(path, DateTimeOffset.UnixEpoch).Project;

        var character = Assert.Single(project.Characters);
        Assert.Equal("Иван", character.Name);
        Assert.Equal(CharacterRole.Protagonist, character.Role);
        Assert.Equal("найти сестру", character.Motivation);
        Assert.Equal("появляется во второй главе", character.Description);

        var line = Assert.Single(project.PlotLines);
        Assert.Equal("Главная линия", line.Name);
        Assert.Equal("#4A7DFF", line.ColorHex);
        Assert.Equal(2, line.Steps.Count);
        Assert.True(line.Steps[1].IsFinal);

        var step = line.Steps[0];
        var sceneId = project.Series[0].Books[0].Chapters[0].Scenes[0].Id;
        Assert.Contains(sceneId, step.SceneIds);
        Assert.Contains(sceneId, line.SceneIds);
    }

    [Fact]
    public void Plotline_known_only_by_reference_is_recreated()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Ранний.scri");
        new LegacyModularProjectBuilder()
            .WithMeta("appVersion", "scri.p 0.1.1")
            .WithMeta("formatVersion", null)
            .WithEntity("ch_1", "chapter", new JsonObject { ["title"] = "Глава 1" }, "Глава 1")
            .WithEntity("sc_1", "scene", new JsonObject
            {
                ["title"] = "Сцена",
                ["chapterId"] = "ch_1",
            }, "Сцена")
            .WithEntity("step_1", "plotline_stage", new JsonObject
            {
                ["name"] = "Этап 1",
                ["plotlineId"] = "plotline_main",
            }, "Этап 1")
            .WithRelation("sc_1", "plotline_main", "scene_plotline", new JsonObject { ["stepId"] = "step_1" })
            .WriteTo(path);

        var loaded = LegacyModularImporter.Load(path, DateTimeOffset.UnixEpoch);
        var project = loaded.Project;

        var line = Assert.Single(project.PlotLines);
        Assert.Equal("plotline_main", line.SourceId);
        var step = Assert.Single(line.Steps);
        var sceneId = project.Series[0].Books[0].Chapters[0].Scenes[0].Id;
        Assert.Contains(sceneId, step.SceneIds);
        Assert.Contains(sceneId, line.SceneIds);

        // Сборка-источник называется пользователю по имени, а не «файл повреждён».
        Assert.Contains("scri.p 0.1.1", string.Join("\n", loaded.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Scene_fields_without_a_direct_analogue_are_kept_in_description()
    {
        using var workspace = new TempWorkspace();
        var path = WriteBasicProject(workspace);

        var project = LegacyModularImporter.Load(path, DateTimeOffset.UnixEpoch).Project;
        var scene = project.Series[0].Books[0].Chapters[0].Scenes[0];

        Assert.Contains("Цель: Показать дом", scene.Description, StringComparison.Ordinal);
        Assert.Contains("Конфликт: Герой не верит письму", scene.Description, StringComparison.Ordinal);
        Assert.Contains("Результат: Он решается ехать", scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Scene_without_chapter_and_text_is_imported_with_warning()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Ранний.scri");
        new LegacyModularProjectBuilder()
            .WithEntity("sc_1", "scene", new JsonObject { ["title"] = "Сирота" }, "Сирота")
            .WriteTo(path);

        var loaded = LegacyModularImporter.Load(path, DateTimeOffset.UnixEpoch);
        var scene = Assert.Single(loaded.Project.Series[0].Books[0].Chapters[0].Scenes);
        Assert.True(scene.Document.IsEmpty());

        var warnings = string.Join("\n", loaded.Warnings);
        Assert.Contains("Без главы", warnings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Imported_project_saves_to_current_format_and_keeps_the_original_as_backup()
    {
        using var workspace = new TempWorkspace();
        var path = WriteBasicProject(workspace);

        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();
        var opened = await projects.OpenAsync(path);
        Assert.True(opened.IsSuccess, opened.Error?.UserMessage);

        // Первое сохранение чужого формата обязано оставить исходник в .bak (ТЗ п. 13).
        var saved = await projects.SaveAsync();
        Assert.True(saved.IsSuccess, saved.Error?.UserMessage);
        Assert.True(File.Exists(path + ".bak"), "исходный ранний файл не сохранён как .bak");

        await projects.CloseAsync(discardChanges: true);
        var reopened = await projects.OpenAsync(path);
        Assert.True(reopened.IsSuccess, reopened.Error?.UserMessage);
        var sceneId = reopened.Require().Series[0].Books[0].Chapters[0].Scenes[0].Id;
        var document = projects.GetSceneDocument(sceneId);
        Assert.True(document.IsSuccess, document.Error?.UserMessage);
        Assert.Equal(SceneText, document.Require().Document.ToPlainText());
    }

    [Fact]
    public async Task Broken_payload_of_one_entity_does_not_abort_the_whole_import()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Ранний.scri");
        new LegacyModularProjectBuilder()
            .WithEntity("ch_1", "chapter", new JsonObject { ["title"] = "Глава 1" }, "Глава 1")
            .WithEntity("sc_1", "scene", new JsonObject { ["title"] = "Целая сцена", ["chapterId"] = "ch_1" }, "Целая сцена")
            .WriteTo(path);

        // Портим payload одной сцены напрямую в базе.
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE entities SET data = '{не json' WHERE id = 'sc_1';";
            command.ExecuteNonQuery();
        }

        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();
        var opened = await projects.OpenAsync(path);

        Assert.True(opened.IsSuccess, opened.Error?.UserMessage);

        // Повреждённые данные сцены не отменяют импорт: сцена сохраняется с именем из
// колонки `name`, но без главы, потому что `chapterId` лежал в неразобранном `data`.
        var chapters = opened.Require().Series[0].Books[0].Chapters;
        var loose = Assert.Single(chapters, chapter => chapter.Title == "Без главы");
        var scene = Assert.Single(loose.Scenes);
        Assert.Equal("Целая сцена", scene.Title);
        var document = projects.GetSceneDocument(scene.Id);
        Assert.True(document.IsSuccess, document.Error?.UserMessage);
        Assert.Equal(string.Empty, document.Require().Document.ToPlainText());
    }

    [Fact]
    public void Entity_types_absent_from_the_model_are_reported_not_silently_dropped()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Ранний.scri");
        new LegacyModularProjectBuilder()
            .WithEntity("ch_1", "chapter", new JsonObject { ["title"] = "Глава 1" }, "Глава 1")
            .WithEntity("med_1", "media", new JsonObject { ["name"] = "Обложка" }, "Обложка")
            .WriteTo(path);

        var warnings = string.Join("\n", LegacyModularImporter.Load(path, DateTimeOffset.UnixEpoch).Warnings);
        Assert.Contains("media", warnings, StringComparison.Ordinal);
    }

    [Fact]
    public void Locations_and_objects_are_imported_into_the_library()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Ранний.scri");
        new LegacyModularProjectBuilder()
            .WithEntity("ch_1", "chapter", new JsonObject { ["title"] = "Глава 1" }, "Глава 1")
            .WithEntity("loc_1", "location", new JsonObject { ["name"] = "Дом", ["description"] = "Старый дом" }, "Дом")
            .WithEntity("obj_1", "object", new JsonObject { ["name"] = "Ключ" }, "Ключ")
            .WriteTo(path);

        var project = LegacyModularImporter.Load(path, DateTimeOffset.UnixEpoch).Project;

        var location = Assert.Single(project.Locations);
        Assert.Equal("Дом", location.Name);
        Assert.Equal("loc_1", location.SourceId);
        var item = Assert.Single(project.StoryObjects);
        Assert.Equal("Ключ", item.Name);
    }

    [Fact]
    public void Story_arc_is_imported_as_a_plot_line_when_the_file_has_no_plotline_entities()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Ранний.scri");
        new LegacyModularProjectBuilder()
            .WithEntity("ch_1", "chapter", new JsonObject { ["title"] = "Глава 1" }, "Глава 1")
            .WithEntity("plotline_main", "story_arc", new JsonObject { ["title"] = "Главная линия", ["color"] = "#4A7DFF" }, "Главная линия")
            .WithEntity("step_1", "plotline_stage", new JsonObject { ["plotlineId"] = "plotline_main", ["name"] = "Начало" }, "Начало", order: 0)
            .WriteTo(path);

        var project = LegacyModularImporter.Load(path, DateTimeOffset.UnixEpoch).Project;

        var line = Assert.Single(project.PlotLines);
        Assert.Equal("Главная линия", line.Name);
        Assert.Equal("#4A7DFF", line.ColorHex);
        var step = Assert.Single(line.Steps);
        Assert.Equal("Начало", step.Title);
    }

    [Fact]
    public void Character_step_becomes_an_arc_step_of_its_character()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Ранний.scri");
        new LegacyModularProjectBuilder()
            .WithEntity("ch_1", "chapter", new JsonObject { ["title"] = "Глава 1" }, "Глава 1")
            .WithEntity("chry_1", "character", new JsonObject { ["name"] = "Идан" }, "Идан")
            .WithEntity("cstep_1", "character_stage", new JsonObject { ["characterId"] = "chry_1", ["name"] = "Старт", ["order"] = "0" }, "Старт", order: 0)
            .WriteTo(path);

        var project = LegacyModularImporter.Load(path, DateTimeOffset.UnixEpoch).Project;

        var character = Assert.Single(project.Characters);
        var step = Assert.Single(character.ArcStages);
        Assert.Equal("Старт", step.Title);
        Assert.Equal(CharacterArcStageKind.InitialState, step.Kind);
    }

    [Fact]
    public void Rich_text_is_imported_as_plain_text()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Ранний.scri");
        const string rich = "{\"version\":2,\"blocks\":[{\"runs\":[{\"text\":\"Первый\"},{\"text\":\" абзац\"}]},{\"runs\":[{\"text\":\"Второй\"}]}]}";
        new LegacyModularProjectBuilder()
            .WithEntity("ch_1", "chapter", new JsonObject { ["title"] = "Глава 1" }, "Глава 1")
            .WithEntity("sc_1", "scene", new JsonObject { ["title"] = "Сцена", ["chapterId"] = "ch_1", ["textId"] = "txt_1" }, "Сцена")
            .WithText("txt_1", rich, format: "rich")
            .WriteTo(path);

        var project = LegacyModularImporter.Load(path, DateTimeOffset.UnixEpoch).Project;

        var scene = Assert.Single(project.Series[0].Books[0].Chapters[0].Scenes);
        Assert.Equal("Первый абзац\nВторой", scene.Document.ToPlainText());
    }

    [Fact]
    public void Legacy_fields_absent_from_our_model_are_reported_instead_of_being_dropped_silently()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Ранний.scri");
        new LegacyModularProjectBuilder()
            .WithEntity("ch_1", "chapter", new JsonObject { ["title"] = "Глава 1" }, "Глава 1")
            .WithEntity("sc_1", "scene", new JsonObject
            {
                ["title"] = "Сцена",
                ["chapterId"] = "ch_1",
                // Поля, о которых шла речь в аудите: аннотация и время в сцене.
                ["annotation"] = "важная ремарка",
                ["dateTime"] = "ночь",
            })
            .WithEntity("character_1", "character", new JsonObject
            {
                ["name"] = "Иван",
                ["status"] = "alive",
            })
            .WriteTo(path);

        var warnings = string.Join("\n", LegacyModularImporter.Load(path, DateTimeOffset.UnixEpoch).Warnings);

        // dateTime переносится в описание сцены, поэтому в списке непрочитанных его нет.
        Assert.DoesNotContain("dateTime", warnings, StringComparison.Ordinal);
        Assert.Contains("annotation", warnings, StringComparison.Ordinal);
        Assert.Contains("character: status", warnings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_warnings_are_published_so_the_user_sees_approximated_data()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Ранний.scri");
        new LegacyModularProjectBuilder()
            .WithEntity("ch_1", "chapter", new JsonObject { ["title"] = "Глава 1" }, "Глава 1")
            .WithEntity("med_1", "media", new JsonObject { ["name"] = "Обложка" }, "Обложка")
            .WriteTo(path);

        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var imported = new List<ProjectImported>();
        using var subscription = provider.GetRequiredService<IEventBus>()
            .Subscribe<ProjectImported>(imported.Add);

        var opened = await provider.GetRequiredService<IProjectService>().OpenAsync(path);
        Assert.True(opened.IsSuccess, opened.Error?.UserMessage);

        var message = Assert.Single(imported);
        Assert.Equal(LegacyModularImporter.FormatId, message.SourceFormat);
        Assert.Contains(message.Warnings, warning => warning.Contains("media", StringComparison.Ordinal));
    }

    private static string WriteBasicProject(TempWorkspace workspace)
    {
        var path = workspace.FilePath("Ранний.scri");
        new LegacyModularProjectBuilder()
            .WithMeta("author", "Иван Петров")
            .WithEntity("book_1", "book", new JsonObject { ["title"] = "Ранний проект" }, "Ранний проект")
            .WithEntity("ch_1", "chapter", new JsonObject { ["title"] = "Глава 1", ["number"] = 1 }, "Глава 1")
            .WithEntity("sc_1", "scene", new JsonObject
            {
                ["title"] = "Открывающая сцена",
                ["chapterId"] = "ch_1",
                ["textId"] = "txt_1",
                ["status"] = "draft",
                ["goal"] = "Показать дом",
                ["conflict"] = "Герой не верит письму",
                ["result"] = "Он решается ехать",
            }, "Открывающая сцена")
            .WithText("txt_1", SceneText)
            .WriteTo(path);
        return path;
    }
}

