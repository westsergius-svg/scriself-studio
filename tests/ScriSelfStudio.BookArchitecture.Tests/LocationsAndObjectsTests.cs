namespace ScriSelfStudio.BookArchitecture.Tests;

using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Persistence.Projects;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Локации и сюжетно значимые объекты книги (макет «Локации/Объекты»; ТЗ п. 27).
/// </summary>
public sealed class LocationsAndObjectsTests
{
    private const string ProjectPath = @"C:\Проекты\Рукопись.scri";

    [Fact]
    public async Task New_location_is_created_with_fields()
    {
        var context = await CreateContextAsync();

        var saved = await context.Architecture.SaveLocationAsync(new LocationDto
        {
            Name = "Дом героя",
            Description = "Стартовая точка",
            Notes = "Двухэтажный особняк",
        });

        Assert.True(saved.IsSuccess, saved.Error?.TechnicalDetails);
        var location = Assert.Single(context.Architecture.GetLocations());
        Assert.Equal("Дом героя", location.Name);
        Assert.Equal("Стартовая точка", location.Description);
        Assert.Equal("Двухэтажный особняк", location.Notes);
        Assert.Equal(0, location.Order);
    }

    [Fact]
    public async Task Updating_a_location_replaces_its_fields()
    {
        var context = await CreateContextAsync();
        var created = (await context.Architecture.SaveLocationAsync(new LocationDto { Name = "Дом" }))
            .Require().Locations[0];

        var updated = await context.Architecture.SaveLocationAsync(created with
        {
            Name = "Особняк",
            Description = "Обновлённое описание",
        });

        Assert.True(updated.IsSuccess, updated.Error?.TechnicalDetails);
        var location = Assert.Single(context.Architecture.GetLocations());
        Assert.Equal(created.Id, location.Id);
        Assert.Equal("Особняк", location.Name);
        Assert.Equal("Обновлённое описание", location.Description);
        Assert.Null(location.Notes);
    }

    [Fact]
    public async Task Location_names_stay_unique()
    {
        var context = await CreateContextAsync();
        await context.Architecture.SaveLocationAsync(new LocationDto { Name = "Таверна" });

        var duplicate = await context.Architecture.SaveLocationAsync(new LocationDto { Name = "Таверна" });

        Assert.False(duplicate.IsSuccess);
        Assert.Equal(ErrorCode.EntityAlreadyExists, duplicate.Error!.Code);
    }

    [Fact]
    public async Task Deleting_a_location_removes_it_from_the_list()
    {
        var context = await CreateContextAsync();
        var created = (await context.Architecture.SaveLocationAsync(new LocationDto { Name = "Таверна" }))
            .Require().Locations[0];

        var deleted = await context.Architecture.DeleteLocationAsync(created.Id);

        Assert.True(deleted.IsSuccess, deleted.Error?.TechnicalDetails);
        Assert.Empty(context.Architecture.GetLocations());
    }

    [Fact]
    public async Task Updating_a_missing_location_fails()
    {
        var context = await CreateContextAsync();

        var saved = await context.Architecture.SaveLocationAsync(new LocationDto
        {
            Id = Guid.NewGuid(),
            Name = "Призрак",
        });

        Assert.False(saved.IsSuccess);
        Assert.Equal(ErrorCode.EntityNotFound, saved.Error!.Code);
    }

    [Fact]
    public async Task Objects_are_created_and_deleted_independently_of_locations()
    {
        var context = await CreateContextAsync();

        var location = (await context.Architecture.SaveLocationAsync(new LocationDto { Name = "Таверна" }))
            .Require().Locations[0];
        var created = await context.Architecture.SaveStoryObjectAsync(new StoryObjectDto
        {
            Name = "Ключ",
            Description = "Открывает дверь",
        });

        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);
        var storyObject = Assert.Single(context.Architecture.GetStoryObjects());
        Assert.Equal("Ключ", storyObject.Name);

        var deleted = await context.Architecture.DeleteStoryObjectAsync(storyObject.Id);
        Assert.True(deleted.IsSuccess, deleted.Error?.TechnicalDetails);
        Assert.Empty(context.Architecture.GetStoryObjects());
        Assert.Equal(location.Id, Assert.Single(context.Architecture.GetLocations()).Id);
    }

    [Fact]
    public async Task Object_names_stay_unique_and_may_repeat_location_names()
    {
        var context = await CreateContextAsync();
        await context.Architecture.SaveStoryObjectAsync(new StoryObjectDto { Name = "Меч" });

        var duplicate = await context.Architecture.SaveStoryObjectAsync(new StoryObjectDto { Name = "Меч" });
        var sameAsLocation = await context.Architecture.SaveLocationAsync(new LocationDto { Name = "Меч" });

        Assert.False(duplicate.IsSuccess);
        Assert.Equal(ErrorCode.EntityAlreadyExists, duplicate.Error!.Code);
        Assert.True(sameAsLocation.IsSuccess, sameAsLocation.Error?.TechnicalDetails);
    }

    private static async Task<TestContext> CreateContextAsync()
    {
        var clock = new FakeClock();
        var projects = new ProjectService(new InMemoryProjectStore(), new InMemoryRecoveryStore(clock), new EventBus(), clock);
        var architecture = new BookArchitectureService(projects, clock);
        var created = await projects.CreateAsync(new CreateProjectRequest
        {
            FilePath = ProjectPath,
            Name = "Рукопись",
        });

        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);
        return new TestContext(projects, architecture);
    }

    private sealed record TestContext(ProjectService Projects, BookArchitectureService Architecture);
}
