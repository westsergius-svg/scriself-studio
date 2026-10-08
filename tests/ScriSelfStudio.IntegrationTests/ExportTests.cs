namespace ScriSelfStudio.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Presentation.Export;
using ScriSelfStudio.TestSupport;

public sealed class ExportTests
{
    [Fact]
    public async Task Export_writes_manuscript_and_rejects_fake_document_formats()
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
        Assert.Contains(SampleProjects.SceneText, await File.ReadAllTextAsync(output));

        foreach (var format in export.Formats.Where(format => format.Key is "DOCX" or "PDF" or "EPUB"))
        {
            dialogs.ExportPath = workspace.FilePath("Книга." + format.Extension);
            export.SelectedFormat = format;
            await export.ExportCommand.ExecuteAsync(null);
            Assert.True(export.HasError);
            Assert.False(File.Exists(dialogs.ExportPath));
            Assert.False(export.HasStatus);
        }
    }
}
