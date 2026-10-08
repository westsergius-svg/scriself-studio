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
/// Миграция схемы 1 → 2: персонажи, их арки и шаги линий (ТЗ п. 5, 13; ADR 010).
/// Файл версии 1 открывается без потерь, получает новые таблицы и резервную копию.
/// </summary>
public sealed class SchemaMigrationTests
{
    [Fact]
    public async Task Version_one_file_is_migrated_and_keeps_its_structure()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Старый.scri");

        await CreateVersionTwoProjectAsync(workspace, path);
        DowngradeToVersionOne(path);

        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();
        var opened = await projects.OpenAsync(path);

        Assert.True(opened.IsSuccess, $"{opened.Error?.UserMessage} | {opened.Error?.TechnicalDetails}");
        Assert.Equal(SchemaVersion.Current, opened.Require().SchemaVersion);
        Assert.Equal("Глава", opened.Require().Series[0].Books[0].Chapters[0].Title);
        Assert.Equal("Сцена", opened.Require().Series[0].Books[0].Chapters[0].Scenes[0].Title);

        // Миграция идёт по копии: предыдущая версия файла сохранена.
        Assert.True(File.Exists(path + ".bak"));

        // В обновлённом файле работают новые сущности.
        var architecture = provider.GetRequiredService<IBookArchitectureService>();
        var character = await architecture.SaveCharacterAsync(new CharacterDto { Name = "Анна" });
        Assert.True(character.IsSuccess, character.Error?.UserMessage);
        Assert.True((await projects.SaveAsync()).IsSuccess);

        using var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var service = reopened.GetRequiredService<IProjectService>();
        var again = await service.OpenAsync(path);

        Assert.True(again.IsSuccess, again.Error?.UserMessage);
        Assert.Equal(SchemaVersion.Current, again.Require().SchemaVersion);
        Assert.Single(reopened.GetRequiredService<IBookArchitectureService>().GetCharacters());
        Assert.Equal("Сцена", again.Require().Series[0].Books[0].Chapters[0].Scenes[0].Title);
    }

    private static async Task CreateVersionTwoProjectAsync(TempWorkspace workspace, string path)
    {
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();
        var architecture = provider.GetRequiredService<IBookArchitectureService>();

        Assert.True((await projects.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Старый" })).IsSuccess);

        var seriesId = architecture.GetSeries()[0].Id;
        var book = await architecture.CreateBookAsync(new CreateBookRequest { SeriesId = seriesId, Title = "Книга" });
        var bookId = book.Require().Series[0].Books[0].Id;
        var chapter = await architecture.CreateChapterAsync(new CreateChapterRequest { BookId = bookId, Title = "Глава" });
        var chapterId = chapter.Require().Series[0].Books[0].Chapters[0].Id;
        Assert.True((await architecture.CreateSceneAsync(new CreateSceneRequest
        {
            ChapterId = chapterId,
            Title = "Сцена",
        })).IsSuccess);

        Assert.True((await projects.SaveAsync()).IsSuccess);
    }

    /// <summary>Приводит файл к виду версии 1: без таблиц персонажей и этапов линий.</summary>
    private static void DowngradeToVersionOne(string path)
    {
        // Пул соединений держал бы файл открытым и помешал бы миграции заменить его копией.
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "DROP TABLE IF EXISTS character; DROP TABLE IF EXISTS plot_line_step; " +
            "UPDATE schema_info SET version = 1 WHERE id = 1;";
        command.ExecuteNonQuery();
        connection.Close();
        SqliteConnection.ClearAllPools();
    }
}
