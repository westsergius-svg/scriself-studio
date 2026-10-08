namespace ScriSelfStudio.Core.Tests;

using ScriSelfStudio.Core.Model;
using ScriSelfStudio.TestSupport;

public sealed class ProjectGraphTests
{
    [Fact]
    public void FindScene_returns_scene_and_parent_chapter()
    {
        var project = SampleProjects.CreateBookWithScenes();
        var scene = project.EnumerateScenes().First();
        var chapter = Assert.Single(project.EnumerateChapters());

        Assert.Same(scene, project.FindScene(scene.Id));
        Assert.Same(chapter, project.FindParentChapter(scene.Id));
    }

    [Fact]
    public void Deleted_entities_are_hidden_by_default()
    {
        var project = SampleProjects.CreateBookWithScenes();
        var scene = project.EnumerateScenes().First();

        scene.MarkDeleted(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(scene, project.EnumerateScenes());
        Assert.Contains(scene, project.EnumerateScenes(includeDeleted: true));
        Assert.Null(project.FindScene(scene.Id));
        Assert.NotNull(project.FindScene(scene.Id, includeDeleted: true));
    }

    [Fact]
    public void Unknown_ids_return_null()
    {
        var project = SampleProjects.CreateBookWithScenes();

        Assert.Null(project.FindScene(Guid.NewGuid()));
        Assert.Null(project.FindChapter(Guid.NewGuid()));
        Assert.Null(project.FindBook(Guid.NewGuid()));
        Assert.Null(project.FindSeries(Guid.NewGuid()));
        Assert.Null(project.FindParentChapter(Guid.NewGuid()));
    }
}
