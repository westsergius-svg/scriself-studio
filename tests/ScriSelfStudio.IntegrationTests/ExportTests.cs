namespace ScriSelfStudio.IntegrationTests;

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation.Export;
using ScriSelfStudio.TestSupport;

public sealed class ExportTests
{
    [Fact]
    public void Review_exports_current_manuscript_without_comments_or_deleted_text()
    {
        var project = SampleProjects.CreateBookWithScenes();
        var scene = project.EnumerateScenes().First();
        var before = scene.Document.Clone();
        before.Review.TrackChanges = true;
        ScriSelfStudio.Core.Text.ReviewEditing.AddComment(before, 0, 6, "PRIVATE COMMENT", DateTimeOffset.UtcNow);
        scene.Document = SampleProjects.DocumentWithText("Current manuscript", ScriSelfStudio.Core.Text.TextFormat.Italic);
        ScriSelfStudio.Core.Text.ReviewEditing.Capture(before, scene.Document, DateTimeOffset.UtcNow);
        var snapshot = project.ToDto("review.scri", false);
        var manuscript = ManuscriptExportBuilder.Build("Book", snapshot.Series.SelectMany(s => s.Books).ToArray(),
            id => id == scene.Id ? scene.Document : null, true);
        var options = new ExportOptions("Georgia", 12, 1.5, false, false, false);
        var text = TextExportWriter.BuildTxt(manuscript, options);
        Assert.Contains("Current manuscript", text);
        Assert.DoesNotContain("PRIVATE COMMENT", text);
        Assert.DoesNotContain("Первая строка", text);
        var runs = manuscript.Books[0].Chapters[0].Scenes[0].Paragraphs.SelectMany(p => p.Runs);
        Assert.All(runs, r => Assert.True(r.Italic));
    }
    [Theory]
    [InlineData("DOCX")]
    [InlineData("PDF")]
    [InlineData("EPUB")]
    [InlineData("TXT")]
    [InlineData("Markdown")]
    public void Preview_respects_scene_headings_title_contents_and_character_options(string format)
    {
        var manuscript = new ExportManuscript("Рукопись", "Подзаголовок", [new("Книга", [new("Начало", [
            new("Служебное название", [new(ExportBlockKind.Paragraph, [new("Реальный текст", Bold: true)])]),
            new("Вторая сцена", [new(ExportBlockKind.Paragraph, [new("Продолжение рукописи")])])])])])
        { Characters = [new("Анна", [new("Характер", "Сдержанная")])] };
        var options = new ExportOptions("Georgia", 12, 1.5, false, false, false);
        var hidden = string.Join("\n", ExportPreviewBuilder.Build(manuscript, options, format).Select(block => block.Text));
        Assert.DoesNotContain("Служебное название", hidden);
        Assert.DoesNotContain("Вторая сцена", hidden);
        Assert.DoesNotContain("Подзаголовок", hidden);
        Assert.DoesNotContain("Содержание", hidden, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Анна", hidden);
        Assert.Contains("Реальный текст", hidden);
        Assert.Contains("Продолжение рукописи", hidden);
        var enabled = options with { IncludeSceneHeadings = true, IncludeTitlePage = true, TableOfContents = true, IncludeCharacterCards = true };
        var visible = string.Join("\n", ExportPreviewBuilder.Build(manuscript, enabled, format).Select(block => block.Text));
        Assert.Contains("Сцена: Служебное название", visible);
        Assert.Contains("Сцена: Вторая сцена", visible);
        Assert.Contains("Подзаголовок", visible);
        Assert.Contains("Содержание", visible, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Анна", visible);
        if (format is "TXT" or "Markdown")
            Assert.Equal(format == "TXT" ? TextExportWriter.BuildTxt(manuscript, enabled) : TextExportWriter.BuildMarkdown(manuscript, enabled), visible);
        else
            Assert.True(ExportPreviewBuilder.Build(manuscript, options, format).Single(block => block.Text == "Реальный текст").Runs[0].Bold);
    }

    [Fact]
    public void Docx_export_keeps_scene_text_flowing_without_scene_headings_by_default()
    {
        using var workspace = new TempWorkspace();
        var output = workspace.FilePath("Сцены.docx");
        var manuscript = new ExportManuscript(
            "Книга",
            string.Empty,
            [
                new ExportBook(
                    "Книга",
                    [
                        new ExportChapter(
                            "Глава",
                            [
                                new ExportScene(
                                    "Сцена 1",
                                    [new ExportParagraph(ExportBlockKind.Paragraph, [new ExportRun("Текст 1")])]),
                                new ExportScene(
                                    "Сцена 2",
                                    [new ExportParagraph(ExportBlockKind.Paragraph, [new ExportRun("Текст 2")])]),
                            ]),
                    ]),
            ]);

        DocxExportWriter.Write(
            manuscript,
            output,
            new ExportOptions("Georgia", 12, 1.15, IncludeTitlePage: false, TableOfContents: false, PageNumbering: false));

        using var archive = ZipFile.OpenRead(output);
        var documentEntry = archive.GetEntry("word/document.xml");
        Assert.NotNull(documentEntry);
        using var reader = new StreamReader(documentEntry.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();
        Assert.Empty(Regex.Matches(xml, "<w:br w:type=\"page\""));
        Assert.DoesNotContain("Сцена 1", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Сцена 2", xml, StringComparison.Ordinal);
        Assert.True(xml.IndexOf("Текст 1", StringComparison.Ordinal) < xml.IndexOf("Текст 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Docx_export_can_include_scene_headings_without_page_breaks()
    {
        using var workspace = new TempWorkspace();
        var output = workspace.FilePath("Сцены.docx");
        var manuscript = new ExportManuscript(
            "Книга",
            string.Empty,
            [
                new ExportBook(
                    "Книга",
                    [
                        new ExportChapter(
                            "Глава",
                            [
                                new ExportScene("Утро", [new ExportParagraph(ExportBlockKind.Paragraph, [new ExportRun("Текст 1")])]),
                                new ExportScene("Вечер", [new ExportParagraph(ExportBlockKind.Paragraph, [new ExportRun("Текст 2")])]),
                            ]),
                    ]),
            ]);

        DocxExportWriter.Write(
            manuscript,
            output,
            new ExportOptions("Georgia", 12, 1.15, IncludeTitlePage: false, TableOfContents: false, PageNumbering: false,
                IncludeSceneHeadings: true));

        using var archive = ZipFile.OpenRead(output);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();
        Assert.Empty(Regex.Matches(xml, "<w:br w:type=\"page\""));
        Assert.Contains("Сцена: Утро", xml, StringComparison.Ordinal);
        Assert.Contains("Сцена: Вечер", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_writes_manuscript_in_every_supported_format()
    {
        using var workspace = new TempWorkspace();
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();
        var architecture = provider.GetRequiredService<IBookArchitectureService>();
        Assert.True((await projects.CreateAsync(new CreateProjectRequest
        {
            FilePath = workspace.FilePath("Рукопись.scri"), Name = "Рукопись",
        })).IsSuccess);
        var book = (await architecture.CreateBookAsync(new CreateBookRequest
        {
            SeriesId = architecture.GetSeries()[0].Id, Title = "Книга",
        })).Require().Series[0].Books[0];
        var chapter = (await architecture.CreateChapterAsync(new CreateChapterRequest
        {
            BookId = book.Id, Title = "Глава",
        })).Require().Series[0].Books[0].Chapters[0];
        var scene = (await architecture.CreateSceneAsync(new CreateSceneRequest
        {
            ChapterId = chapter.Id, Title = "Сцена",
        })).Require().Series[0].Books[0].Chapters[0].Scenes[0];
        Assert.True((await projects.UpdateSceneDocumentAsync(new UpdateSceneDocumentRequest
        {
            SceneId = scene.Id, BaseRevision = 0,
            Document = SampleProjects.DocumentWithText(SampleProjects.SceneText),
        })).IsSuccess);
        Assert.True((await architecture.SaveCharacterAsync(new CharacterDto
        {
            Name = "Лея",
            Age = "27 лет",
            Description = "Пилот исследовательского корабля.",
        })).IsSuccess);
        var output = workspace.FilePath("Книга.txt");
        var dialogs = new StubFileDialogService { ExportPath = output };
        using var export = new ExportViewModel(architecture, projects,
            provider.GetRequiredService<ScriSelfStudio.Contracts.Navigation.INavigationService>(), dialogs);
        export.UpdatePreview();
        export.IncludeCharacterCards = true;
        Assert.Contains(SampleProjects.SceneText.ReplaceLineEndings("\n"), string.Join("\n", export.PreviewBlocks.Select(block => block.Text)).ReplaceLineEndings("\n"));
        await export.ExportCommand.ExecuteAsync(null);
        Assert.False(export.HasError, export.ErrorMessage);
        var txt = await File.ReadAllTextAsync(output);
        Assert.Contains("Первая строка рукописи.", txt);
        Assert.Contains("Вторая строка с «ёжиком» и emoji 🚀.", txt);
        Assert.DoesNotContain("Сцена:", txt, StringComparison.Ordinal);
        Assert.Contains("Лея", txt, StringComparison.Ordinal);
        Assert.Contains("Пилот исследовательского корабля.", txt, StringComparison.Ordinal);

        foreach (var format in export.Formats)
        {
            dialogs.ExportPath = workspace.FilePath("Книга." + format.Extension);
            export.SelectedFormat = format;
            await export.ExportCommand.ExecuteAsync(null);
            Assert.False(export.HasError, $"{format.Key}: {export.ErrorMessage}");
            Assert.True(export.HasStatus, format.Key);
            Assert.True(File.Exists(dialogs.ExportPath), format.Key);

            var bytes = await File.ReadAllBytesAsync(dialogs.ExportPath);
            Assert.NotEmpty(bytes);
            if (format.Key == "PDF")
            {
                Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
            }
            else if (format.Key is "DOCX" or "EPUB")
            {
                using var archive = ZipFile.OpenRead(dialogs.ExportPath);
                Assert.NotEmpty(archive.Entries);
                var documentEntry = archive.Entries.FirstOrDefault(entry =>
                    entry.FullName == (format.Key == "DOCX" ? "word/document.xml" : "OEBPS/chapter001.xhtml"));
                Assert.NotNull(documentEntry);
                using var reader = new StreamReader(documentEntry.Open(), Encoding.UTF8);
                var xml = await reader.ReadToEndAsync();
                Assert.Contains("Первая строка рукописи.", xml);
                if (format.Key == "DOCX")
                {
                    Assert.Contains("Лея", xml, StringComparison.Ordinal);
                }
                else
                {
                    Assert.DoesNotContain("Сцена:", xml, StringComparison.Ordinal);
                    var characterEntry = archive.GetEntry("OEBPS/characters.xhtml");
                    Assert.NotNull(characterEntry);
                    using var characterReader = new StreamReader(characterEntry.Open(), Encoding.UTF8);
                    Assert.Contains("Лея", await characterReader.ReadToEndAsync(), StringComparison.Ordinal);
                }
            }
        }
    }
}
