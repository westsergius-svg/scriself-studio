namespace ScriSelfStudio.IntegrationTests;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Schema;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Локации и объекты в собственном формате (SQLite, схема 4): запись, переоткрытие
/// и миграция файла схемы 3, в котором этих таблиц ещё нет (ТЗ п. 5, 13, 27).
/// </summary>
public sealed class LibraryEntitiesMigrationTests
{
    [Fact]
    public async Task Locations_and_objects_survive_our_own_format_round_trip()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Рукопись.scri");

        using (var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var projects = provider.GetRequiredService<IProjectService>();
            var architecture = provider.GetRequiredService<IBookArchitectureService>();
            Assert.True((await projects.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Рукопись" })).IsSuccess);

            Assert.True((await architecture.SaveLocationAsync(new LocationDto
            {
                Name = "Таверна",
                Description = "Место встречи",
            })).IsSuccess);
            Assert.True((await architecture.SaveStoryObjectAsync(new StoryObjectDto
            {
                Name = "Ключ",
                Notes = "Открывает дверь",
            })).IsSuccess);

            Assert.True((await projects.SaveAsync()).IsSuccess);
        }

        using var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var service = reopened.GetRequiredService<IProjectService>();
        var opened = await service.OpenAsync(path);
        Assert.True(opened.IsSuccess, opened.Error?.UserMessage);
        Assert.Equal(SchemaVersion.Current, opened.Require().SchemaVersion);

        var library = reopened.GetRequiredService<IBookArchitectureService>();
        var location = Assert.Single(library.GetLocations());
        Assert.Equal("Таверна", location.Name);
        Assert.Equal("Место встречи", location.Description);
        var storyObject = Assert.Single(library.GetStoryObjects());
        Assert.Equal("Ключ", storyObject.Name);
        Assert.Equal("Открывает дверь", storyObject.Notes);
    }

    [Fact]
    public async Task Version_three_file_gains_library_tables_on_migration()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");

        using (var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var projects = provider.GetRequiredService<IProjectService>();
            Assert.True((await projects.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Старый" })).IsSuccess);
            Assert.True((await projects.SaveAsync()).IsSuccess);
        }

        DowngradeToVersionThree(path);

        using var migrated = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var service = migrated.GetRequiredService<IProjectService>();
        var opened = await service.OpenAsync(path);
        Assert.True(opened.IsSuccess, $"{opened.Error?.UserMessage} | {opened.Error?.TechnicalDetails}");
        Assert.Equal(SchemaVersion.Current, opened.Require().SchemaVersion);
        Assert.True(File.Exists(path + ".bak"));

        // В обновлённом файле новые сущности сразу работают.
        var architecture = migrated.GetRequiredService<IBookArchitectureService>();
        Assert.True((await architecture.SaveLocationAsync(new LocationDto { Name = "Локация" })).IsSuccess);
        Assert.True((await architecture.SaveStoryObjectAsync(new StoryObjectDto { Name = "Объект" })).IsSuccess);
        Assert.True((await service.SaveAsync()).IsSuccess);

        using var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var again = await reopened.GetRequiredService<IProjectService>().OpenAsync(path);
        Assert.True(again.IsSuccess, again.Error?.UserMessage);
        Assert.Single(reopened.GetRequiredService<IBookArchitectureService>().GetLocations());
        Assert.Single(reopened.GetRequiredService<IBookArchitectureService>().GetStoryObjects());
    }

    /// <summary>Приводит файл к виду схемы 3: без таблиц локаций и объектов.</summary>
    private static void DowngradeToVersionThree(string path)
    {
        // Пул соединений держал бы файл открытым и помешал бы миграции заменить его копией.
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "DROP TABLE IF EXISTS location; DROP TABLE IF EXISTS story_object; " +
            "UPDATE schema_info SET version = 3 WHERE id = 1;";
        command.ExecuteNonQuery();
        connection.Close();
        SqliteConnection.ClearAllPools();
    }
}
