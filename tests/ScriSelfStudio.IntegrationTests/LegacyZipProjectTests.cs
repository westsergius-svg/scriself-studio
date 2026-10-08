namespace ScriSelfStudio.IntegrationTests;

using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Старый ZIP-формат `.scri` (<c>manifest.json</c> + <c>content.json</c>): открытие,
/// правка и сохранение в том же формате с резервной копией (ТЗ п. 5, 13).
/// </summary>
public sealed class LegacyZipProjectTests
{
    private const string SceneText = "Первая строка старого текста\nВторая строка";

    [Fact]
    public async Task Legacy_zip_project_opens_with_structure_texts_and_plotlines()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");
        WriteLegacyProject(path, SceneText);

        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();
        var opened = await projects.OpenAsync(path);

        Assert.True(opened.IsSuccess, opened.Error?.UserMessage);
        var project = opened.Require();
        Assert.Equal(ProjectFileFormats.ZipJson, projects.CurrentSourceFormat);
        Assert.Equal("Старый проект", project.Name);
        Assert.Equal("Описание из старого файла", project.Description);

        var chapter = Assert.Single(project.Series[0].Books[0].Chapters);
        Assert.Equal("Глава 1. Завязка", chapter.Title);
        Assert.Equal("Синопсис главы", chapter.Description);

        var scene = Assert.Single(chapter.Scenes);
        Assert.Equal("Открывающая сцена", scene.Title);
        Assert.Equal(SceneStatus.Draft, scene.Status);
        Assert.Equal(SceneText, projects.GetSceneDocument(scene.Id).Require().Document.ToPlainText());
        Assert.Equal(WordCounting.Count(SceneText), scene.Words);
        Assert.Contains("Цель: Показать дом", scene.Description, StringComparison.Ordinal);
        Assert.Contains("Конфликт: Герой не верит письму", scene.Description, StringComparison.Ordinal);

    }

    [Fact]
    public async Task Characters_and_plotline_stages_survive_a_zip_round_trip()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");
        WriteLegacyProject(path, SceneText);

        Guid sceneId;
        Guid characterId;
        using (var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var projects = provider.GetRequiredService<IProjectService>();
            var architecture = provider.GetRequiredService<IBookArchitectureService>();
            Assert.True((await projects.OpenAsync(path)).IsSuccess);
            sceneId = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0].Id;

            // Старый персонаж переносится в нашу модель, карточку можно дописать.
            var legacy = Assert.Single(architecture.GetCharacters());
            Assert.Equal("Главный герой", legacy.Name);
            Assert.True((await architecture.SaveCharacterAsync(legacy with
            {
                Age = "40 лет",
                FalseBelief = "Сила решает всё",
            })).IsSuccess);

            var created = (await architecture.SaveCharacterAsync(new CharacterDto { Name = "Анна" })).Require();
            var anna = created.Characters.First(character => character.Name == "Анна");
            characterId = anna.Id;

            var line = Assert.Single(architecture.GetPlotLines());
            Assert.True((await architecture.SavePlotLineAsync(line with
            {
                Steps =
                [
                    new PlotLineStepDto
                    {
                        Title = "Завязка",
                        SceneIds = [sceneId],
                        Participants =
                        [
                            new StepParticipantDto
                            {
                                CharacterId = anna.Id,
                                CharacterArcStageId = anna.ArcStages[0].Id,
                            },
                        ],
                    },
                ],
            })).IsSuccess);

            Assert.True((await projects.SaveAsync()).IsSuccess);
        }

        using var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var service = reopened.GetRequiredService<IProjectService>();
        Assert.True((await service.OpenAsync(path)).IsSuccess);
        var restored = reopened.GetRequiredService<IBookArchitectureService>();

        var characters = restored.GetCharacters();
        Assert.Equal(2, characters.Count);
        Assert.Equal("40 лет", characters.First(character => character.Name == "Главный герой").Age);
        var annaRestored = characters.First(character => character.Name == "Анна");
        Assert.Equal(characterId, annaRestored.Id);
        Assert.Equal(7, annaRestored.ArcStages.Count);

        var plotLine = Assert.Single(restored.GetPlotLines());
        var step = Assert.Single(plotLine.Steps);
        Assert.Equal("Завязка", step.Title);
        Assert.Equal(sceneId, Assert.Single(step.SceneIds));
        var participant = Assert.Single(step.Participants);
        Assert.Equal(annaRestored.Id, participant.CharacterId);
        Assert.Equal(annaRestored.ArcStages[0].Id, participant.CharacterArcStageId);
    }

    /// <summary>
    /// Локации и объекты переносятся из старого раздела <c>library</c> и переживают
    /// сохранение: и в нашей модели, и в исходных массивах старого формата (ТЗ п. 5).
    /// </summary>
    [Fact]
    public async Task Locations_and_objects_survive_a_zip_round_trip()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");
        WriteLegacyProject(path, SceneText);

        using (var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var projects = provider.GetRequiredService<IProjectService>();
            Assert.True((await projects.OpenAsync(path)).IsSuccess);

            var legacy = Assert.Single(projects.Current!.Locations);
            Assert.Equal("Дом героя", legacy.Name);

            var mutated = projects.Mutate(project =>
            {
                project.Locations.Add(new Location
                {
                    ProjectId = project.Id,
                    Name = "Таверна",
                    Description = "Место встречи",
                });
                project.StoryObjects.Add(new StoryObject
                {
                    ProjectId = project.Id,
                    Name = "Ключ",
                    Description = "Открывает дверь",
                });
                return Result.Success;
            });
            Assert.True(mutated.IsSuccess, mutated.Error?.UserMessage);

            Assert.True((await projects.SaveAsync()).IsSuccess);
        }

        using var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var service = reopened.GetRequiredService<IProjectService>();
        var opened = await service.OpenAsync(path);
        Assert.True(opened.IsSuccess, opened.Error?.UserMessage);

        var project = service.Current!;
        Assert.Equal(2, project.Locations.Count);
        Assert.Contains(project.Locations, location => location.Name == "Таверна" && location.Description == "Место встречи");
        var storyObject = Assert.Single(project.StoryObjects);
        Assert.Equal("Ключ", storyObject.Name);

        // Исходные массивы старого формата тоже обновлены: файл читается старыми версиями.
        var library = (JsonObject)ReadEntry(path, "content.json")["library"]!;
        var legacyLocations = (JsonArray)library["locations"]!;
        Assert.Equal(2, legacyLocations.Count);
        Assert.Equal("Дом героя", legacyLocations[0]!["name"]!.GetValue<string>());
        Assert.Equal("Таверна", legacyLocations[1]!["name"]!.GetValue<string>());
        Assert.Equal("Ключ", ((JsonArray)library["objects"]!)[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Saving_keeps_the_zip_format_with_backup_and_preserves_unknown_fields()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");
        WriteLegacyProject(path, SceneText);
        var originalBytes = await File.ReadAllBytesAsync(path);

        using (var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var projects = provider.GetRequiredService<IProjectService>();
            Assert.True((await projects.OpenAsync(path)).IsSuccess);

            var sceneId = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0].Id;
            var document = projects.GetSceneDocument(sceneId).Require();
            var updated = await projects.UpdateSceneDocumentAsync(new UpdateSceneDocumentRequest
            {
                SceneId = sceneId,
                BaseRevision = document.Revision,
                Document = PlainTextDocument.FromText("Правка после открытия"),
            });
            Assert.True(updated.IsSuccess, updated.Error?.UserMessage);

            var architecture = provider.GetRequiredService<IBookArchitectureService>();
            var bookId = projects.Current!.Series[0].Books[0].Id;
            Assert.True((await architecture.CreateChapterAsync(new CreateChapterRequest
            {
                BookId = bookId,
                Title = "Новая глава",
            })).IsSuccess);

            var saved = await projects.SaveAsync();
            Assert.True(saved.IsSuccess, saved.Error?.UserMessage);
        }

        // Файл остался ZIP-контейнером старого формата.
        Assert.Equal("PK", ReadSignature(path));

        // Предыдущая версия сохранена целиком: это требование ТЗ п. 13.
        var backup = path + ".bak";
        Assert.True(File.Exists(backup));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(backup));

        var manifest = ReadEntry(path, "manifest.json");
        var content = ReadEntry(path, "content.json");

        Assert.Equal("Старый проект", manifest["name"]!.GetValue<string>());
        Assert.Equal("Роман (базовый)", manifest["template"]!.GetValue<string>());

        var chapters = (JsonArray)content["chapters"]!;
        Assert.Equal(2, chapters.Count);

        var firstChapter = (JsonObject)chapters[0]!;
        Assert.Equal("chapter_001", firstChapter["id"]!.GetValue<string>());
        Assert.Equal("Подзаголовок главы", firstChapter["subtitle"]!.GetValue<string>());
        Assert.Equal("Эпиграф", firstChapter["epigraph"]!.GetValue<string>());
        Assert.Equal("volume_001", firstChapter["volumeId"]!.GetValue<string>());

        var firstScene = (JsonObject)((JsonArray)firstChapter["scenes"]!)[0]!;
        Assert.Equal("scene_001", firstScene["id"]!.GetValue<string>());
        Assert.Equal("Правка после открытия", firstScene["text"]!.GetValue<string>());

        var secondChapter = (JsonObject)chapters[1]!;
        Assert.Equal("Новая глава", secondChapter["title"]!.GetValue<string>());
        Assert.StartsWith("chapter_", secondChapter["id"]!.GetValue<string>(), StringComparison.Ordinal);

        // Поля, которых нет в нашей модели, не потерялись.
        Assert.Equal("Идан Везнич", content["book"]!["author"]!.GetValue<string>());
        Assert.Equal("Главный герой", content["library"]!["characters"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("Том 1", content["volumes"]![0]!["title"]!.GetValue<string>());

        // Сюжетные линии сохраняются в исходном виде: id и цвет не меняются.
        var plotline = (JsonObject)((JsonArray)content["timeline"]!["plotlines"]!)[0]!;
        Assert.Equal("plotline_main", plotline["id"]!.GetValue<string>());
        Assert.Equal("Главная сюжетная линия", plotline["name"]!.GetValue<string>());
        Assert.Equal("#4A7DFF", plotline["color"]!.GetValue<string>());
    }

    [Fact]
    public async Task Saved_legacy_project_reopens_with_the_edited_text()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");
        WriteLegacyProject(path, SceneText);

        using (var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var projects = provider.GetRequiredService<IProjectService>();
            Assert.True((await projects.OpenAsync(path)).IsSuccess);
            var sceneId = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0].Id;
            var document = projects.GetSceneDocument(sceneId).Require();
            Assert.True((await projects.UpdateSceneDocumentAsync(new UpdateSceneDocumentRequest
            {
                SceneId = sceneId,
                BaseRevision = document.Revision,
                Document = PlainTextDocument.FromText("Текст после сохранения"),
            })).IsSuccess);
            Assert.True((await projects.SaveAsync()).IsSuccess);
        }

        using var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var service = reopened.GetRequiredService<IProjectService>();
        var opened = await service.OpenAsync(path);
        Assert.True(opened.IsSuccess, opened.Error?.UserMessage);

        var scene = opened.Require().Series[0].Books[0].Chapters[0].Scenes[0];
        Assert.Equal("Текст после сохранения", service.GetSceneDocument(scene.Id).Require().Document.ToPlainText());
        Assert.Equal(ProjectFileFormats.ZipJson, service.CurrentSourceFormat);
    }

    /// <summary>
    /// Составной стиль фрагмента переживает ZIP-контейнер: старый проект не умеет
    /// хранить оформление текста, поэтому документ с разными цветами, шрифтами и
    /// подсветкой должен вернуться после переоткрытия без смешивания фрагментов
    /// (ADR 003, шаг 3.1.5; ТЗ п. 43).
    /// </summary>
    [Fact]
    public async Task Composite_span_style_survives_a_zip_round_trip()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");
        WriteLegacyProject(path, SceneText);

        var styled = new RichDocument();
        var paragraph = new TextBlock();
        paragraph.Spans.Add(new TextSpan
        {
            Text = "Жирный",
            Style = new SpanStyle
            {
                Flags = TextFormat.Bold | TextFormat.Italic,
                FontFamily = "Consolas",
                FontSize = 16,
                Foreground = TextColor.Parse("#FF0000"),
                Highlight = TextColor.Parse("#FFFF00"),
            },
        });
        paragraph.Spans.Add(new TextSpan
        {
            Text = " Обычный",
            Style = new SpanStyle { FontFamily = "Georgia" },
        });
        styled.Blocks.Add(paragraph);

        using (var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var projects = provider.GetRequiredService<IProjectService>();
            Assert.True((await projects.OpenAsync(path)).IsSuccess);
            var sceneId = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0].Id;
            var revision = projects.GetSceneDocument(sceneId).Require().Revision;

            Assert.True((await projects.UpdateSceneDocumentAsync(new UpdateSceneDocumentRequest
            {
                SceneId = sceneId,
                BaseRevision = revision,
                Document = styled,
            })).IsSuccess);
            Assert.True((await projects.SaveAsync()).IsSuccess);
        }

        using var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var service = reopened.GetRequiredService<IProjectService>();
        Assert.True((await service.OpenAsync(path)).IsSuccess);

        var scene = service.Current!.Series[0].Books[0].Chapters[0].Scenes[0];
        var spans = service.GetSceneDocument(scene.Id).Require().Document.Blocks[0].Spans;

        Assert.Equal(2, spans.Count);
        Assert.Equal("Жирный", spans[0].Text);
        Assert.Equal(TextFormat.Bold | TextFormat.Italic, spans[0].Style.Flags);
        Assert.Equal("Consolas", spans[0].Style.FontFamily);
        Assert.Equal(16, spans[0].Style.FontSize);
        Assert.Equal(TextColor.Parse("#FF0000"), spans[0].Style.Foreground);
        Assert.Equal(TextColor.Parse("#FFFF00"), spans[0].Style.Highlight);

        // Второй фрагмент остался отдельным: сравнение по флагам слило бы их.
        Assert.Equal(" Обычный", spans[1].Text);
        Assert.Equal("Georgia", spans[1].Style.FontFamily);
        Assert.Equal(TextColor.None, spans[1].Style.Foreground);
    }

    [Fact]
    public async Task Zip_without_the_scriself_section_opens_with_plain_text()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");
        WriteLegacyProject(path, SceneText);

        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();

        Assert.True((await projects.OpenAsync(path)).IsSuccess);
        var scene = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0];
        var document = projects.GetSceneDocument(scene.Id).Require().Document;

        // Файла, записанного нами, ещё нет: открывается тот же проект, что и раньше.
        Assert.Equal(SceneText, document.ToPlainText());
    }

    [Fact]
    public async Task Broken_scene_document_falls_back_to_plain_text_without_failing_the_open()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");
        WriteLegacyProject(path, SceneText, new JsonObject
        {
            ["version"] = 1,
            ["documents"] = new JsonObject
            {
                ["scene_001"] = "это не документ",
            },
        });

        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();

        // Плохой документ стоит оформления, но не текста и не открытия проекта.
        Assert.True((await projects.OpenAsync(path)).IsSuccess);
        var scene = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0];
        var document = projects.GetSceneDocument(scene.Id).Require().Document;
        Assert.Equal(SceneText, document.ToPlainText());
    }

    [Fact]
    public async Task Unknown_document_keys_are_ignored()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");
        WriteLegacyProject(path, SceneText, new JsonObject
        {
            ["version"] = 1,
            ["documents"] = new JsonObject
            {
                ["scene_deleted"] = new JsonObject { ["schemaVersion"] = 1, ["blocks"] = new JsonArray() },
            },
        });

        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();

        Assert.True((await projects.OpenAsync(path)).IsSuccess);
        var scene = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0];
        var document = projects.GetSceneDocument(scene.Id).Require().Document;

        // Ключ от удалённой сцены не превращается в новую сцену: документов ровно столько,
        // сколько сцен в проекте.
        Assert.Equal(SceneText, document.ToPlainText());
        Assert.Single(projects.Current!.Series[0].Books[0].Chapters[0].Scenes);
    }

    [Fact]
    public async Task Saving_rewrites_the_documents_section_for_the_own_format()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");
        WriteLegacyProject(path, SceneText);

        using (var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var projects = provider.GetRequiredService<IProjectService>();
            Assert.True((await projects.OpenAsync(path)).IsSuccess);
            Assert.True((await projects.SaveAsync()).IsSuccess);
        }

        var extras = ReadEntry(path, "content.json")["scriself"]!.AsObject();
        var documents = extras["documents"]!.AsObject();

        // Ключ — старый идентификатор сцены, а не наш Guid: он переживает переоткрытие.
        Assert.Equal("PK", ReadSignature(path));
        Assert.True(documents.ContainsKey("scene_001"));
    }

    [Fact]
    public async Task Newer_extras_section_still_opens_because_keys_are_read_individually()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");
        WriteLegacyProject(path, SceneText, new JsonObject
        {
            // Раздел записан более новой версией. Строгая проверка цифры отклонила бы
            // файл целиком, хотя characters/plotlines/documents мы читать умеем.
            ["version"] = 99,
            ["documents"] = new JsonObject
            {
                ["scene_001"] = "это не документ",
            },
        });

        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();

        Assert.True((await projects.OpenAsync(path)).IsSuccess);
        var scene = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0];
        Assert.Equal(SceneText, projects.GetSceneDocument(scene.Id).Require().Document.ToPlainText());
    }


    private static string ReadSignature(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[2];
        stream.ReadExactly(header, 0, 2);
        return Encoding.ASCII.GetString(header);
    }

    private static JsonObject ReadEntry(string path, string entryName)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry(entryName)!;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return (JsonObject)JsonNode.Parse(reader.ReadToEnd())!;
    }

    private static void WriteLegacyProject(string path, string sceneText, JsonObject? scriSelfExtras = null)
    {
        var manifest = new JsonObject
        {
            ["format"] = "scri",
            ["version"] = 1,
            ["name"] = "Старый проект",
            ["template"] = "Роман (базовый)",
            ["createdAt"] = "2026-08-29T11:27:56.947654+00:00",
            ["updatedAt"] = "2026-09-02T16:49:42.746258+00:00",
        };

        var content = new JsonObject
        {
            ["book"] = new JsonObject
            {
                ["title"] = "Старый проект",
                ["synopsis"] = "Описание из старого файла",
                ["author"] = "Идан Везнич",
                ["genre"] = "Роман",
                ["preface"] = "Предисловие",
                ["subtitle"] = string.Empty,
                ["format"] = string.Empty,
                ["tone"] = string.Empty,
                ["audience"] = string.Empty,
            },
            ["chapters"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "chapter_001",
                    ["title"] = "Глава 1. Завязка",
                    ["number"] = 1,
                    ["subtitle"] = "Подзаголовок главы",
                    ["epigraph"] = "Эпиграф",
                    ["synopsis"] = "Синопсис главы",
                    ["notes"] = "Заметки главы",
                    ["status"] = "in_progress",
                    ["targetWordCount"] = 0,
                    ["chapterTextHtml"] = "<html/>",
                    ["volumeId"] = "volume_001",
                    ["scenes"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["id"] = "scene_001",
                            ["title"] = "Открывающая сцена",
                            ["text"] = sceneText,
                            ["goal"] = "Показать дом",
                            ["conflict"] = "Герой не верит письму",
                            ["result"] = "Он остаётся",
                            ["status"] = "draft",
                            ["plotlineIds"] = new JsonArray { "plotline_main" },
                        },
                    },
                },
            },
            ["library"] = new JsonObject
            {
                ["characters"] = new JsonArray
                {
                    new JsonObject { ["id"] = "char_001", ["name"] = "Главный герой", ["extra"] = "Протагонист" },
                },
                ["locations"] = new JsonArray { new JsonObject { ["id"] = "loc_001", ["name"] = "Дом героя" } },
                ["objects"] = new JsonArray(),
            },
            ["timeline"] = new JsonObject
            {
                ["plotlines"] = new JsonArray
                {
                    new JsonObject { ["id"] = "plotline_main", ["name"] = "Главная сюжетная линия", ["color"] = "#4A7DFF" },
                },
            },
            ["volumes"] = new JsonArray
            {
                new JsonObject { ["id"] = "volume_001", ["number"] = 1, ["title"] = "Том 1", ["synopsis"] = string.Empty },
            },
        };

        if (scriSelfExtras is not null)
        {
            content["scriself"] = scriSelfExtras;
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", manifest);
        WriteEntry(archive, "content.json", content);
    }

    private static void WriteEntry(ZipArchive archive, string entryName, JsonObject payload)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(payload.ToJsonString());
    }
}
