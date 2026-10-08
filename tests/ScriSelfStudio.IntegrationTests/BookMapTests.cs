namespace ScriSelfStudio.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Карта книги в Архитекторе: столбец на главу, стабильная геометрия, выбор,
/// объём в словах и ключевые сцены (ТЗ п. 27).
/// </summary>
public sealed class BookMapTests
{
    private const double Viewport = 1600d;

    [Fact]
    public async Task Map_has_one_column_per_chapter_in_order()
    {
        using var workspace = new TempWorkspace();
        using var provider = await CreateProjectAsync(workspace, chapters: 3, scenesPerChapter: 2);
        var map = Prepare(provider);

        Assert.True(map.IsMapMode);
        Assert.True(map.IsListMode is false);
        Assert.Equal(3, map.MapColumns.Count);
        Assert.Equal(["Гл. 1", "Гл. 2", "Гл. 3"], map.MapColumns.Select(column => column.Short));
        Assert.All(map.MapColumns, column => Assert.Equal(2, column.Scenes.Count));
        Assert.True(map.MapFits);

        // Столбцы одинаковой ширины (236) с зазором 14: позиция зависит только
        // от индекса, поэтому карта прокручивается, а не растягивается.
        Assert.Equal(236d, map.MapColumns[0].Width);
        Assert.Equal(0d, map.MapColumns[0].X);
        Assert.Equal(250d, map.MapColumns[1].X);
        Assert.Equal(500d, map.MapColumns[2].X);
        // Номера сцен на карте: «глава.сцена».
        Assert.Equal("1.1", map.MapColumns[0].Scenes[0].Number);
        Assert.Equal("1.2", map.MapColumns[0].Scenes[1].Number);
    }

    [Fact]
    public async Task Selecting_a_chapter_highlights_it_without_moving_neighbours()
    {
        using var workspace = new TempWorkspace();
        using var provider = await CreateProjectAsync(workspace, chapters: 20, scenesPerChapter: 1);
        var map = Prepare(provider);

        // Ширина столбца не зависит от числа глав: карта прокручивается, не сжимается.
        var positions = map.MapColumns.Select(column => column.X).ToList();
        Assert.Equal(250d, map.MapColumns[1].X - map.MapColumns[0].X);

        map.SelectNodeCommand.Execute(map.MapColumns[3].Node);

        Assert.True(map.MapColumns[3].IsActive);
        Assert.False(map.MapColumns[4].IsActive);
        // Выбор ничего не сдвигает и не меняет ширину столбцов.
        Assert.Equal(236d, map.MapColumns[3].Width);
        Assert.Equal(236d, map.MapColumns[4].Width);
        Assert.Equal(positions, map.MapColumns.Select(column => column.X));
    }

    [Fact]
    public async Task Selecting_a_scene_on_the_map_is_the_screen_selection()
    {
        using var workspace = new TempWorkspace();
        using var provider = await CreateProjectAsync(workspace, chapters: 2, scenesPerChapter: 2);
        var map = Prepare(provider);

        var cell = map.MapColumns[1].Scenes[0];
        map.SelectNodeCommand.Execute(cell.Node);

        Assert.Same(cell.Node, map.Selected);
        Assert.True(cell.IsSelected);
        Assert.True(map.MapColumns[1].IsActive);
        Assert.True(map.IsSceneSelected);
    }

    [Fact]
    public async Task Many_chapters_are_reported_as_horizontally_scrollable()
    {
        using var workspace = new TempWorkspace();
        using var provider = await CreateProjectAsync(workspace, chapters: 40, scenesPerChapter: 1);
        var map = Prepare(provider);

        // 40 глав: 39 * 250 + 236 = 9986 > 1600, карта прокручивается по горизонтали.
        Assert.False(map.MapFits);
        Assert.Equal(236d, map.MapColumns[0].Width);
        Assert.Equal(9986d, map.MapContentWidth);
    }

    [Fact]
    public async Task Map_shows_scene_words_and_key_scenes()
    {
        const string sceneText = "Сцена с четырьмя словами";
        using var workspace = new TempWorkspace();
        using var provider = await CreateProjectAsync(workspace, chapters: 1, scenesPerChapter: 1);
        var projects = provider.GetRequiredService<IProjectService>();
        var sceneId = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0].Id;

        var document = projects.GetSceneDocument(sceneId).Require();
        var updated = await projects.UpdateSceneDocumentAsync(new UpdateSceneDocumentRequest
        {
            SceneId = sceneId,
            BaseRevision = document.Revision,
            Document = SampleProjects.DocumentWithText(sceneText),
        });
        Assert.True(updated.IsSuccess, updated.Error?.TechnicalDetails);

        var marked = await projects.UpdateSceneAsync(new UpdateSceneRequest { SceneId = sceneId, IsKey = true });
        Assert.True(marked.IsSuccess, marked.Error?.TechnicalDetails);

        var map = Prepare(provider);

        var column = Assert.Single(map.MapColumns);
        var cell = Assert.Single(column.Scenes);
        var expected = WordCounting.Count(sceneText);
        Assert.Equal(expected, cell.Node.Words);
        Assert.Equal(expected, column.Node.Words);
        Assert.True(cell.IsKey);
        Assert.Equal("ключевых: 1", column.KeyLabel);
    }

    [Fact]
    public async Task Words_and_key_flag_survive_reopen()
    {
        const string sceneText = "Первая строка с текстом";
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Карта.scri");

        using (var provider = await CreateProjectAsync(workspace, chapters: 1, scenesPerChapter: 1))
        {
            var projects = provider.GetRequiredService<IProjectService>();
            var sceneId = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0].Id;
            var document = projects.GetSceneDocument(sceneId).Require();

            Assert.True((await projects.UpdateSceneDocumentAsync(new UpdateSceneDocumentRequest
            {
                SceneId = sceneId,
                BaseRevision = document.Revision,
                Document = SampleProjects.DocumentWithText(sceneText),
            })).IsSuccess);

            Assert.True((await projects.UpdateSceneAsync(new UpdateSceneRequest { SceneId = sceneId, IsKey = true })).IsSuccess);
            Assert.True((await projects.SaveAsync()).IsSuccess);
        }

        using var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var service = reopened.GetRequiredService<IProjectService>();
        var snapshot = await service.OpenAsync(path);
        Assert.True(snapshot.IsSuccess, snapshot.Error?.TechnicalDetails);

        var scene = snapshot.Require().Series[0].Books[0].Chapters[0].Scenes[0];
        Assert.True(scene.IsKey);
        // Объём не хранится в файле: он пересчитывается при открытии проекта.
        Assert.Equal(WordCounting.Count(sceneText), scene.Words);
        Assert.Equal(scene.Words, snapshot.Require().Series[0].Books[0].Chapters[0].Words);
    }

    [Fact]
    public async Task Scene_notes_and_lock_reach_the_details_panel_and_survive_reopen()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Карта.scri");
        using (var provider = await CreateProjectAsync(workspace, chapters: 1, scenesPerChapter: 2))
        {
            var projects = provider.GetRequiredService<IProjectService>();
            var sceneId = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[1].Id;
            Assert.True((await projects.UpdateSceneAsync(new UpdateSceneRequest
            {
                SceneId = sceneId,
                Notes = "Не забыть про артефакт.",
                IsLocked = true,
            })).IsSuccess);
            Assert.True((await projects.SaveAsync()).IsSuccess);

            var map = Prepare(provider);
            map.SelectNodeCommand.Execute(map.MapColumns[0].Scenes[1].Node);

            Assert.True(map.HasSceneDetails);
            Assert.Equal("1.2", map.SceneDetails!.NumberLabel);
            Assert.True(map.SceneDetails.HasNotes);
            Assert.Equal("Не забыть про артефакт.", map.SceneDetails.Notes);
            Assert.False(map.SceneDetails.IsKey);
            Assert.True(map.SceneDetails.IsLocked);
            Assert.True(map.MapColumns[0].Scenes[1].IsLocked);
        }

        using var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var service = reopened.GetRequiredService<IProjectService>();
        var snapshot = await service.OpenAsync(path);
        Assert.True(snapshot.IsSuccess, snapshot.Error?.TechnicalDetails);
        Assert.Equal("Не забыть про артефакт.", snapshot.Require().Series[0].Books[0].Chapters[0].Scenes[1].Notes);
        Assert.True(snapshot.Require().Series[0].Books[0].Chapters[0].Scenes[1].IsLocked);
    }

    [Fact]
    public async Task Map_scene_panel_links_and_unlinks_a_plot_line_with_characters()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Карта.scri");
        using var provider = await CreateProjectAsync(workspace, chapters: 1, scenesPerChapter: 2);
        var projects = provider.GetRequiredService<IProjectService>();
        var architecture = provider.GetRequiredService<IBookArchitectureService>();
        var sceneId = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0].Id;

        var character = (await architecture.SaveCharacterAsync(new CharacterDto
        {
            Name = "Идан",
            Role = CharacterRole.Protagonist,
        })).Require().Characters[0];

        var line = (await architecture.SavePlotLineAsync(new PlotLineDto
        {
            Name = "Главная линия",
            ColorHex = "#4A7DFF",
            Steps =
            [
                new PlotLineStepDto
                {
                    Title = "Поиск",
                    Participants = [new StepParticipantDto { CharacterId = character.Id }],
                },
            ],
        })).Require().PlotLines[0];
        var stepId = line.Steps[0].Id;

        var map = Prepare(provider);
        map.SelectNodeCommand.Execute(map.MapColumns[0].Scenes[0].Node);

        // Пустая сцена: линий нет, но выбрать линию уже можно.
        var details = map.SceneDetails!;
        Assert.False(details.HasLines);
        Assert.True(details.HasLineChoices);

        // Добавление линии с карты: сцена уходит на этап, линия и её персонаж появляются.
        details.SelectedLineChoice = details.LineChoices.First(choice => choice.Id == line.Id);
        details.SelectedStepChoice = details.StepChoices.First(choice => choice.Id == stepId);
        await details.AddLineCommand.ExecuteAsync(null);

        Assert.True(map.HasSceneDetails);
        Assert.True(map.SceneDetails!.HasLines);
        Assert.Equal("Главная линия", Assert.Single(map.SceneDetails.Lines).Name);
        Assert.True(map.SceneDetails.HasCharacters);
        Assert.Equal("Идан", Assert.Single(map.SceneDetails.Characters).Name);
        Assert.Contains(map.MapColumns[0].Scenes[0].Lines, marker => marker.Name == "Главная линия");

        Assert.True((await projects.SaveAsync()).IsSuccess);
        using (var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var service = reopened.GetRequiredService<IProjectService>();
            var snapshot = await service.OpenAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.TechnicalDetails);
            Assert.Contains(sceneId, snapshot.Require().PlotLines[0].Steps[0].SceneIds);

            // Снятие линии со сцены на карте переживает переоткрытие.
            var reopenedMap = Prepare(reopened);
            reopenedMap.SelectNodeCommand.Execute(
                reopenedMap.MapColumns[0].Scenes.First(cell => cell.Id == sceneId).Node);
            var reopenedDetails = reopenedMap.SceneDetails!;
            var entry = Assert.Single(reopenedDetails.Lines);
            Assert.True(reopenedDetails.HasCharacters);
            await reopenedDetails.RemoveLineCommand.ExecuteAsync(entry);

            Assert.False(reopenedMap.SceneDetails!.HasLines);
            Assert.False(reopenedMap.SceneDetails.HasCharacters);
            Assert.True((await service.SaveAsync()).IsSuccess);
        }

        using var final = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var finalService = final.GetRequiredService<IProjectService>();
        var finalSnapshot = await finalService.OpenAsync(path);
        Assert.True(finalSnapshot.IsSuccess, finalSnapshot.Error?.TechnicalDetails);
        Assert.DoesNotContain(sceneId, finalSnapshot.Require().PlotLines[0].Steps[0].SceneIds);
    }

    private static ArchitectureViewModel Prepare(ServiceProvider provider)
    {
        var map = provider.GetRequiredService<ArchitectureViewModel>();
        map.Refresh();
        map.MapViewportWidth = Viewport;
        return map;
    }

    private static async Task<ServiceProvider> CreateProjectAsync(
        TempWorkspace workspace,
        int chapters,
        int scenesPerChapter)
    {
        var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();
        var architecture = provider.GetRequiredService<IBookArchitectureService>();

        var created = await projects.CreateAsync(new CreateProjectRequest
        {
            FilePath = workspace.FilePath("Карта.scri"),
            Name = "Карта",
        });
        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);

        var seriesId = architecture.GetSeries()[0].Id;
        var book = await architecture.CreateBookAsync(new CreateBookRequest { SeriesId = seriesId, Title = "Книга" });
        var bookId = book.Require().Series[0].Books[0].Id;

        for (var chapterIndex = 0; chapterIndex < chapters; chapterIndex++)
        {
            var chapter = await architecture.CreateChapterAsync(new CreateChapterRequest
            {
                BookId = bookId,
                Title = $"Глава {chapterIndex + 1}",
            });
            var chapterId = chapter.Require().Series[0].Books[0].Chapters[chapterIndex].Id;

            for (var sceneIndex = 0; sceneIndex < scenesPerChapter; sceneIndex++)
            {
                var scene = await architecture.CreateSceneAsync(new CreateSceneRequest
                {
                    ChapterId = chapterId,
                    Title = $"Сцена {sceneIndex + 1}",
                });
                Assert.True(scene.IsSuccess, scene.Error?.TechnicalDetails);
            }
        }

        return provider;
    }
}
