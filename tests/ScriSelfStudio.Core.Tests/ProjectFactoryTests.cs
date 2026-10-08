namespace ScriSelfStudio.Core.Tests;

using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Schema;
using ScriSelfStudio.TestSupport;

public sealed class ProjectFactoryTests
{
    [Fact]
    public void CreateProject_sets_current_schema_version_and_timestamps()
    {
        var now = new DateTimeOffset(2026, 5, 5, 10, 30, 0, TimeSpan.Zero);

        var project = ProjectFactory.CreateProject("Роман", "Описание", now);

        Assert.Equal(SchemaVersion.Current, project.SchemaVersion);
        Assert.Equal("Роман", project.Name);
        Assert.Equal("Описание", project.Description);
        Assert.Equal(now, project.Audit.CreatedAt);
        Assert.Equal(now, project.Audit.UpdatedAt);
        Assert.Empty(project.Series);
    }

    [Fact]
    public void CreateStructure_keeps_order_and_links_by_id()
    {
        var project = SampleProjects.CreateBookWithScenes();

        var series = Assert.Single(project.Series);
        var book = Assert.Single(series.Books);
        var chapter = Assert.Single(book.Chapters);

        Assert.Equal(project.Id, series.ProjectId);
        Assert.Equal(project.Id, book.ProjectId);
        Assert.Equal(series.Id, book.SeriesId);
        Assert.Equal(book.Id, chapter.BookId);
        Assert.Equal(0, series.Order);
        Assert.Equal(0, book.Order);
        Assert.Equal(0, chapter.Order);
        Assert.Equal([0, 1], chapter.Scenes.Select(scene => scene.Order));
        Assert.All(chapter.Scenes, scene => Assert.Equal(chapter.Id, scene.ChapterId));
    }

    [Fact]
    public void NewScene_has_empty_document_and_zero_revision()
    {
        var project = SampleProjects.CreateBookWithScenes();
        var scene = project.EnumerateScenes().Last();

        Assert.Equal(0, scene.Revision);
        Assert.True(scene.Document.IsEmpty());
        Assert.Single(scene.Document.Blocks);
    }
}
