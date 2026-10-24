namespace ScriSelfStudio.IntegrationTests;

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Presentation.Export;
using ScriSelfStudio.TestSupport;

public sealed class ExportTests
{
    [Fact]
    public void Docx_export_starts_each_scene_on_new_page()
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
        Assert.Single(Regex.Matches(xml, "<w:br w:type=\"page\""));
        Assert.True(xml.IndexOf("Текст 1", StringComparison.Ordinal) < xml.IndexOf("Текст 2", StringComparison.Ordinal));
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
        var output = workspace.FilePath("Книга.txt");
        var dialogs = new StubFileDialogService { ExportPath = output };
        using var export = new ExportViewModel(architecture, projects,
            provider.GetRequiredService<ScriSelfStudio.Contracts.Navigation.INavigationService>(), dialogs);
        export.UpdatePreview();
        Assert.Equal(SampleProjects.SceneText, export.PreviewTextExcerpt);
        await export.ExportCommand.ExecuteAsync(null);
        Assert.False(export.HasError, export.ErrorMessage);
        var txt = await File.ReadAllTextAsync(output);
        Assert.Contains("Первая строка рукописи.", txt);
        Assert.Contains("Вторая строка с «ёжиком» и emoji 🚀.", txt);

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
            }
        }
    }
}
