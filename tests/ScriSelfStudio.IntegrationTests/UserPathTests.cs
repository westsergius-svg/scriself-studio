namespace ScriSelfStudio.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.TestSupport;

public sealed class UserPathTests
{
    /// <summary>
    /// Обязательный пользовательский путь (ТЗ п. 31): создать проект → книгу → главу → сцену,
    /// написать текст → сохранить → закрыть → открыть → продолжить работу.
    /// </summary>
    [Fact]
    public async Task Create_edit_save_close_reopen_keeps_text()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Рукопись.scri");
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);

        var projects = provider.GetRequiredService<IProjectService>();
        var architecture = provider.GetRequiredService<IBookArchitectureService>();

        var created = await projects.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Рукопись" });
        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);

        var seriesId = architecture.GetSeries()[0].Id;
        var book = await architecture.CreateBookAsync(new CreateBookRequest { SeriesId = seriesId, Title = "Книга первая" });
        var bookId = book.Require().Series[0].Books[0].Id;

        var chapter = await architecture.CreateChapterAsync(new CreateChapterRequest { BookId = bookId, Title = "Глава 1" });
        var chapterId = chapter.Require().Series[0].Books[0].Chapters[0].Id;

        var scene = await architecture.CreateSceneAsync(new CreateSceneRequest { ChapterId = chapterId, Title = "Сцена 1" });
        var sceneId = scene.Require().Series[0].Books[0].Chapters[0].Scenes[0].Id;

        var document = projects.GetSceneDocument(sceneId).Require();
        Assert.Equal(0, document.Revision);

        var updated = await projects.UpdateSceneDocumentAsync(new UpdateSceneDocumentRequest
        {
            SceneId = sceneId,
            BaseRevision = document.Revision,
            Document = SampleProjects.DocumentWithText(SampleProjects.SceneText),
        });

        Assert.True(updated.IsSuccess, updated.Error?.TechnicalDetails);
        Assert.True(projects.IsDirty);

        var saved = await projects.SaveAsync();
        Assert.True(saved.IsSuccess, saved.Error?.TechnicalDetails);
        Assert.False(projects.IsDirty);

        Assert.True((await projects.CloseAsync(discardChanges: false)).IsSuccess);
        Assert.Null(projects.Current);

        var reopened = await projects.OpenAsync(path);
        Assert.True(reopened.IsSuccess, reopened.Error?.TechnicalDetails);

        var reopenedDocument = projects.GetSceneDocument(sceneId).Require();
        Assert.Equal(SampleProjects.SceneText, reopenedDocument.Document.ToPlainText());
        Assert.Equal(1, reopenedDocument.Revision);

        var hierarchy = reopened.Require();
        Assert.Equal("Книга первая", hierarchy.Series[0].Books[0].Title);
        Assert.Equal("Глава 1", hierarchy.Series[0].Books[0].Chapters[0].Title);
        Assert.Equal("Сцена 1", hierarchy.Series[0].Books[0].Chapters[0].Scenes[0].Title);
        Assert.True(hierarchy.Series[0].Books[0].Chapters[0].Scenes[0].HasText);
    }

    [Fact]
    public async Task Closing_without_saving_keeps_file_untouched()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Рукопись.scri");
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();
        var architecture = provider.GetRequiredService<IBookArchitectureService>();

        await projects.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Рукопись" });
        var seriesId = architecture.GetSeries()[0].Id;
        await architecture.CreateBookAsync(new CreateBookRequest { SeriesId = seriesId, Title = "Книга" });

        // Изменение не сохранено и отброшено: файл на диске остаётся в исходном состоянии.
        var refused = await projects.CloseAsync(discardChanges: false);
        Assert.False(refused.IsSuccess);
        Assert.True((await projects.CloseAsync(discardChanges: true)).IsSuccess);

        var reopened = await projects.OpenAsync(path);
        Assert.Empty(reopened.Require().Series[0].Books);
    }
}
