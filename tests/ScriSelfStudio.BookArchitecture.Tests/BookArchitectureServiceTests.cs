namespace ScriSelfStudio.BookArchitecture.Tests;

using ScriSelfStudio.BookArchitecture;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Persistence.Projects;
using ScriSelfStudio.TestSupport;

public sealed class BookArchitectureServiceTests
{
    private readonly string _projectPath = Path.Combine(Path.GetTempPath(), "ScriSelfStudioTests", Guid.NewGuid().ToString("N"), "Рукопись.scri");

    [Fact]
    public async Task Create_project_provides_default_series()
    {
        var context = await CreateContextAsync();

        var series = Assert.Single(context.Architecture.GetSeries());
        Assert.Equal(ProjectService.DefaultSeriesTitle, series.Title);
        Assert.Empty(context.Architecture.GetBooks());
    }

    [Fact]
    public async Task Create_book_chapter_and_scene_updates_snapshot()
    {
        var context = await CreateContextAsync();
        var seriesId = context.Architecture.GetSeries()[0].Id;

        var book = await context.Architecture.CreateBookAsync(new CreateBookRequest { SeriesId = seriesId, Title = "Книга" });
        Assert.True(book.IsSuccess, book.Error?.TechnicalDetails);
        var bookId = Assert.Single(book.Require().Series).Books[0].Id;

        var chapter = await context.Architecture.CreateChapterAsync(new CreateChapterRequest { BookId = bookId, Title = "Глава" });
        Assert.True(chapter.IsSuccess, chapter.Error?.TechnicalDetails);
        var chapterId = chapter.Require().Series[0].Books[0].Chapters[0].Id;

        var scene = await context.Architecture.CreateSceneAsync(new CreateSceneRequest { ChapterId = chapterId, Title = "Сцена" });
        Assert.True(scene.IsSuccess, scene.Error?.TechnicalDetails);

        var snapshot = scene.Require();
        var sceneDto = Assert.Single(snapshot.Series[0].Books[0].Chapters[0].Scenes);
        Assert.Equal("Сцена", sceneDto.Title);
        Assert.Equal(0, sceneDto.Order);
        Assert.False(sceneDto.HasText);
        Assert.Equal(0, sceneDto.Revision);
    }

    [Fact]
    public async Task Duplicate_titles_are_rejected()
    {
        var context = await CreateContextAsync();
        var chapterId = await CreateChapterAsync(context);
        Assert.True((await context.Architecture.CreateSceneAsync(new CreateSceneRequest { ChapterId = chapterId, Title = "Сцена" })).IsSuccess);

        var duplicate = await context.Architecture.CreateSceneAsync(new CreateSceneRequest { ChapterId = chapterId, Title = "  сцена  " });

        Assert.False(duplicate.IsSuccess);
        Assert.Equal(ErrorCode.EntityAlreadyExists, duplicate.Error!.Code);
    }

    [Fact]
    public async Task Empty_title_is_rejected()
    {
        var context = await CreateContextAsync();
        var seriesId = context.Architecture.GetSeries()[0].Id;

        var empty = await context.Architecture.CreateBookAsync(new CreateBookRequest { SeriesId = seriesId, Title = "   " });

        Assert.False(empty.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, empty.Error!.Code);
    }

    [Fact]
    public async Task Unknown_parent_reports_entity_not_found()
    {
        var context = await CreateContextAsync();

        var result = await context.Architecture.CreateChapterAsync(new CreateChapterRequest
        {
            BookId = Guid.NewGuid(),
            Title = "Глава",
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.EntityNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Rename_changes_title_and_rejects_duplicate()
    {
        var context = await CreateContextAsync();
        var chapterId = await CreateChapterAsync(context);
        await context.Architecture.CreateSceneAsync(new CreateSceneRequest { ChapterId = chapterId, Title = "Первая" });
        var second = await context.Architecture.CreateSceneAsync(new CreateSceneRequest { ChapterId = chapterId, Title = "Вторая" });

        var secondId = second.Require().Series[0].Books[0].Chapters[0].Scenes[1].Id;
        var renamed = await context.Architecture.RenameEntityAsync(new RenameEntityRequest
        {
            Kind = EntityKind.Scene,
            Id = secondId,
            Title = "Новая сцена",
        });

        Assert.True(renamed.IsSuccess, renamed.Error?.TechnicalDetails);
        Assert.Equal("Новая сцена", renamed.Require().Series[0].Books[0].Chapters[0].Scenes[1].Title);

        var conflict = await context.Architecture.RenameEntityAsync(new RenameEntityRequest
        {
            Kind = EntityKind.Scene,
            Id = secondId,
            Title = "Первая",
        });

        Assert.False(conflict.IsSuccess);
        Assert.Equal(ErrorCode.EntityAlreadyExists, conflict.Error!.Code);
    }

    [Fact]
    public async Task Delete_soft_deletes_and_reindexes_siblings()
    {
        var context = await CreateContextAsync();
        var chapterId = await CreateChapterAsync(context);
        foreach (var title in new[] { "Первая", "Вторая", "Третья" })
        {
            await context.Architecture.CreateSceneAsync(new CreateSceneRequest { ChapterId = chapterId, Title = title });
        }

        var scenes = context.Projects.Current!.Series[0].Books[0].Chapters[0].Scenes;
        var first = scenes[0].Id;

        var deleted = await context.Architecture.DeleteEntityAsync(new DeleteEntityRequest
        {
            Kind = EntityKind.Scene,
            Id = first,
        });

        Assert.True(deleted.IsSuccess, deleted.Error?.TechnicalDetails);
        var remaining = deleted.Require().Series[0].Books[0].Chapters[0].Scenes;
        Assert.Equal(["Вторая", "Третья"], remaining.Select(scene => scene.Title));
        Assert.Equal([0, 1], remaining.Select(scene => scene.Order));
    }

    [Fact]
    public async Task Move_reorders_siblings_and_normalizes_order()
    {
        var context = await CreateContextAsync();
        var chapterId = await CreateChapterAsync(context);
        foreach (var title in new[] { "Первая", "Вторая", "Третья" })
        {
            await context.Architecture.CreateSceneAsync(new CreateSceneRequest { ChapterId = chapterId, Title = title });
        }

        var firstId = context.Projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0].Id;

        var moved = await context.Architecture.MoveEntityAsync(new MoveEntityRequest
        {
            Kind = EntityKind.Scene,
            Id = firstId,
            NewOrder = 2,
        });

        Assert.True(moved.IsSuccess, moved.Error?.TechnicalDetails);
        var scenes = moved.Require().Series[0].Books[0].Chapters[0].Scenes;
        Assert.Equal(["Вторая", "Третья", "Первая"], scenes.Select(scene => scene.Title));
        Assert.Equal([0, 1, 2], scenes.Select(scene => scene.Order));
    }

    [Fact]
    public async Task Commands_without_open_project_report_error()
    {
        var projects = new ProjectService(new InMemoryProjectStore(), new InMemoryRecoveryStore(), new EventBus(), new FakeClock());
        var architecture = new BookArchitectureService(projects, new FakeClock());

        var result = await architecture.CreateSeriesAsync(new CreateSeriesRequest { Title = "Серия" });

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ProjectNotOpen, result.Error!.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_title_rejects_blank_values(string? title)
    {
        var architecture = new BookArchitectureService(new ProjectService(new InMemoryProjectStore(), new InMemoryRecoveryStore(), new EventBus(), new FakeClock()));

        var result = architecture.ValidateTitle(EntityKind.Scene, title);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.Error!.Code);
    }

    [Fact]
    public void Validate_title_rejects_too_long_values()
    {
        var architecture = new BookArchitectureService(new ProjectService(new InMemoryProjectStore(), new InMemoryRecoveryStore(), new EventBus(), new FakeClock()));

        var result = architecture.ValidateTitle(EntityKind.Scene, new string('я', BookArchitectureService.MaxTitleLength + 1));

        Assert.False(result.IsSuccess);
    }

    private async Task<TestContext> CreateContextAsync()
    {
        var clock = new FakeClock();
        var projects = new ProjectService(new InMemoryProjectStore(), new InMemoryRecoveryStore(clock), new EventBus(), clock);
        var architecture = new BookArchitectureService(projects, clock);
        var created = await projects.CreateAsync(new CreateProjectRequest
        {
            FilePath = _projectPath,
            Name = "Рукопись",
        });

        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);
        return new TestContext(projects, architecture);
    }

    [Fact]
    public async Task Chapters_can_repeat_titles_on_creation_and_rename()
    {
        var context = await CreateContextAsync();
        var firstId = await CreateChapterAsync(context);
        var bookId = context.Architecture.GetBooks()[0].Id;
        var second = (await context.Architecture.CreateChapterAsync(new CreateChapterRequest
        { BookId = bookId, Title = "Глава" })).Require().Series[0].Books[0].Chapters[1];
        Assert.NotEqual(firstId, second.Id);
        Assert.True((await context.Architecture.RenameEntityAsync(new RenameEntityRequest
        { Kind = EntityKind.Chapter, Id = second.Id, Title = " Глава " })).IsSuccess);
        Assert.Equal(2, context.Architecture.GetBooks()[0].Chapters.Count(chapter => chapter.Title == "Глава"));
    }

    [Fact]
    public async Task Plot_lines_move_among_live_items_and_survive_reopen()
    {
        var context = await CreateContextAsync();
        foreach (var name in new[] { "Первая", "Удалённая", "Последняя" })
            Assert.True((await context.Architecture.SavePlotLineAsync(new ScriSelfStudio.Contracts.Dtos.PlotLineDto { Name = name })).IsSuccess);
        var lines = context.Architecture.GetPlotLines();
        await context.Architecture.DeletePlotLineAsync(lines[1].Id);
        Assert.True((await context.Architecture.MoveEntityAsync(new MoveEntityRequest
        { Kind = EntityKind.PlotLine, Id = lines[2].Id, NewOrder = 0 })).IsSuccess);
        Assert.Equal(new[] { "Последняя", "Первая" }, context.Architecture.GetPlotLines().Select(line => line.Name));
        Assert.Equal(new[] { 0, 1 }, context.Architecture.GetPlotLines().Select(line => line.Order));
        await context.Projects.SaveAsync();
        await context.Projects.CloseAsync(true);
        await context.Projects.OpenAsync(_projectPath);
        Assert.Equal(new[] { "Последняя", "Первая" }, context.Architecture.GetPlotLines().Select(line => line.Name));
    }

    private static async Task<Guid> CreateChapterAsync(TestContext context)
    {
        var seriesId = context.Architecture.GetSeries()[0].Id;
        var book = await context.Architecture.CreateBookAsync(new CreateBookRequest { SeriesId = seriesId, Title = "Книга" });
        var bookId = book.Require().Series[0].Books[0].Id;
        var chapter = await context.Architecture.CreateChapterAsync(new CreateChapterRequest { BookId = bookId, Title = "Глава" });
        return chapter.Require().Series[0].Books[0].Chapters[0].Id;
    }

    private sealed record TestContext(ProjectService Projects, BookArchitectureService Architecture);
}
