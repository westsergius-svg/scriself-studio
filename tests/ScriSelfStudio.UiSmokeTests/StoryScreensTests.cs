namespace ScriSelfStudio.UiSmokeTests;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.StoryUI;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Экраны персонажей и сюжетных линий: карточка, арка и этапы (ТЗ п. 27; ADR 010).
/// </summary>
public sealed class StoryScreensTests
{
    [AvaloniaFact]
    public async Task Characters_screen_saves_a_card_and_its_arc()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Рукопись.scri") };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var navigation = provider.GetRequiredService<INavigationService>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        launcher.OpenCharactersCommand.Execute(null);
        Assert.Equal(AppScreen.Characters, navigation.Current);

        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(window.GetVisualDescendants().OfType<CharactersView>());

        var characters = provider.GetRequiredService<CharactersViewModel>();
        Assert.True(characters.Draft.IsNew);
        Assert.Equal(7, characters.Draft.ArcStages.Count);

        characters.Draft.Name = "Анна";
        characters.Draft.Age = "25 лет";
        characters.Draft.FalseBelief = "Деньги решают всё";
        characters.Draft.TagsText = "маг, предатель";
        characters.AddArcStageCommand.Execute(null);
        characters.Draft.ArcStages[^1].Title = "Своя ступень";
        await characters.SaveCharacterCommand.ExecuteAsync(null);

        Assert.False(characters.HasError, characters.ErrorMessage);
        var saved = Assert.Single(characters.Characters);
        Assert.Equal("Анна", saved.Name);
        Assert.Equal(8, saved.ArcStageCount);

        var card = provider.GetRequiredService<IBookArchitectureService>().GetCharacters()[0];
        Assert.Equal("25 лет", card.Age);
        Assert.Equal(2, card.Tags.Count);
        Assert.Equal("Своя ступень", card.ArcStages[^1].Title);

        // Повторный выбор карточки из списка поднимает её поля и участие в сюжете.
        characters.Selected = null;
        characters.Selected = characters.Characters[0];
        Assert.Equal("Анна", characters.Draft.Name);
        Assert.Equal(8, characters.Draft.ArcStages.Count);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Timeline_screen_links_character_arc_step_and_scene()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Рукопись.scri") };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var sceneId = await CreateSceneAsync(provider);
        var architecture = provider.GetRequiredService<IBookArchitectureService>();
        var character = (await architecture.SaveCharacterAsync(
            new ScriSelfStudio.Contracts.Dtos.CharacterDto { Name = "Анна" })).Require().Characters[0];

        launcher.OpenTimelineCommand.Execute(null);
        var timeline = provider.GetRequiredService<TimelineViewModel>();
        Assert.True(timeline.HasScenes);

        timeline.NewLineCommand.Execute(null);
        timeline.Draft.Name = "Линия Анны";
        timeline.Draft.ColorHex = "#2FA96B";
        timeline.AddStepCommand.Execute(null);
        Assert.NotNull(timeline.SelectedStep);
        timeline.SelectedStep!.Title = "Знакомство";
        timeline.AddParticipantCommand.Execute(null);

        var participant = Assert.Single(timeline.SelectedStep.Participants);
        participant.Character = participant.Characters.First(choice => choice.Id == character.Id);
        Assert.NotEmpty(participant.AvailableSteps);
        participant.ArcStage = participant.AvailableSteps[0];
        await timeline.SaveLineCommand.ExecuteAsync(null);

        Assert.False(timeline.HasError, timeline.ErrorMessage);
        var line = Assert.Single(timeline.PlotLines);
        Assert.Equal("Линия Анны", line.Name);
        Assert.Equal(1, line.StepCount);
        Assert.Equal(StepIcons.ByNumber(1).Title, timeline.Draft.Steps[0].SelectedIcon?.Title);

        var saved = Assert.Single(architecture.GetPlotLines());
        var step = Assert.Single(saved.Steps);
        Assert.Equal("Знакомство", step.Title);
        var savedParticipant = Assert.Single(step.Participants);
        Assert.Equal(character.Id, savedParticipant.CharacterId);
        Assert.NotNull(savedParticipant.CharacterArcStageId);

        // Связь «сцена ↔ этап» задаётся со стороны сцены (ADR 012): линия узнаёт
        // о сцене от сцены, а на экране линий сцена только показывается.
        var projects = provider.GetRequiredService<ScriSelfStudio.Contracts.Projects.IProjectService>();
        var assigned = await projects.UpdateSceneAsync(new ScriSelfStudio.Contracts.Projects.UpdateSceneRequest
        {
            SceneId = sceneId,
            PlotLineStepIds = [step.Id],
        });
        Assert.True(assigned.IsSuccess, assigned.Error?.UserMessage);
        Assert.Equal(sceneId, Assert.Single(architecture.GetPlotLines()[0].Steps[0].SceneIds));

        // Экран линий показывает назначенную сцену, не позволяя её изменить.
        timeline.Refresh();
        var shown = Assert.Single(timeline.Draft.Steps);
        Assert.True(shown.HasScenes);
        Assert.Single(shown.Scenes);

        // Снятие привязки тоже идёт со стороны сцены.
        var cleared = await projects.UpdateSceneAsync(new ScriSelfStudio.Contracts.Projects.UpdateSceneRequest
        {
            SceneId = sceneId,
            PlotLineStepIds = [],
        });
        Assert.True(cleared.IsSuccess, cleared.Error?.UserMessage);
        Assert.Empty(architecture.GetPlotLines()[0].Steps[0].SceneIds);

        // Правка шага на экране линий не должна стирать привязку сцен.
        var reAssign = await projects.UpdateSceneAsync(new ScriSelfStudio.Contracts.Projects.UpdateSceneRequest
        {
            SceneId = sceneId,
            PlotLineStepIds = [step.Id],
        });
        Assert.True(reAssign.IsSuccess, reAssign.Error?.UserMessage);
        timeline.Refresh();
        timeline.Draft.Steps[0].Title = "Знакомство и встреча";
        await timeline.SaveLineCommand.ExecuteAsync(null);
        Assert.False(timeline.HasError, timeline.ErrorMessage);
        Assert.Equal(sceneId, Assert.Single(architecture.GetPlotLines()[0].Steps[0].SceneIds));
    }

    [AvaloniaFact]
    public async Task Timeline_new_line_saves_on_blur_and_survives_reopen()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Рукопись.scri");
        var dialogs = new StubFileDialogService { CreatePath = path };
        using (var provider = ServiceProviderFactory.Create(workspace.Root, dialogs))
        {
            var launcher = provider.GetRequiredService<LauncherViewModel>();
            await launcher.InitializeAsync();
            await launcher.CreateProjectCommand.ExecuteAsync(null);

            launcher.OpenTimelineCommand.Execute(null);
            var timeline = provider.GetRequiredService<TimelineViewModel>();

            // Новую линию нельзя выделить: до сохранения это только черновик, поэтому
            // проверяем, что потеря фокуса поля сохраняет её, а не молча теряет.
            timeline.NewLineCommand.Execute(null);
            timeline.Draft.Name = "Главная линия";
            timeline.Draft.Goal = "Найти источник магии";
            await timeline.CommitLineCommand.ExecuteAsync(null);

            Assert.False(timeline.HasError, timeline.ErrorMessage);
            var line = Assert.Single(provider.GetRequiredService<IBookArchitectureService>().GetPlotLines());
            Assert.Equal("Найти источник магии", line.Goal);

            // Проект сохраняется с любого экрана: кнопка оболочки и Ctrl+S делят одну команду.
            var shell = provider.GetRequiredService<ShellViewModel>();
            Assert.True(shell.SaveCommand.CanExecute(null));
            await shell.SaveCommand.ExecuteAsync(null);
        }

        using var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = reopened.GetRequiredService<ScriSelfStudio.Contracts.Projects.IProjectService>();
        var snapshot = await projects.OpenAsync(path);
        Assert.True(snapshot.IsSuccess, snapshot.Error?.TechnicalDetails);
        var saved = Assert.Single(snapshot.Require().PlotLines);
        Assert.Equal("Главная линия", saved.Name);
        Assert.Equal("Найти источник магии", saved.Goal);
    }

    [AvaloniaFact]
    public async Task Scene_panel_assigns_and_removes_a_plot_line_step()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Рукопись.scri") };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var sceneId = await CreateSceneAsync(provider);
        var architecture = provider.GetRequiredService<IBookArchitectureService>();
        var line = (await architecture.SavePlotLineAsync(new ScriSelfStudio.Contracts.Dtos.PlotLineDto
        {
            Name = "Главная сюжетная линия",
            Description = "Поиск источника магии",
            Goal = "Найти источник магии",
            Status = ScriSelfStudio.Core.Model.PlotLineStatus.InProgress,
            Steps =
            [
                new ScriSelfStudio.Contracts.Dtos.PlotLineStepDto { Title = "Начало", Icon = "start" },
                new ScriSelfStudio.Contracts.Dtos.PlotLineStepDto { Title = "Поиск", Icon = "search" },
            ],
        })).Require().PlotLines[0];

        // Панель контекста сцены — точка входа для назначения этапа (ADR 012).
        var editor = provider.GetRequiredService<ScriSelfStudio.EditorUI.EditorViewModel>();
        await editor.Engine.OpenSceneAsync(sceneId);
        var panel = provider.GetRequiredService<ScriSelfStudio.App.Shell.SceneContextViewModel>();
        panel.Refresh();

        Assert.True(panel.HasScene);
        Assert.True(panel.HasLineOptions);
        Assert.Equal("Главная сюжетная линия", panel.SelectedLineOption!.Name);
        Assert.Equal(2, panel.StepOptions.Count);
        Assert.Empty(panel.AssignedSteps);

        panel.SelectedStepOption = panel.StepOptions.First(option => option.Title == "Поиск");
        await panel.AddStepCommand.ExecuteAsync(null);

        Assert.False(panel.HasError, panel.ErrorMessage);
        var assignedStep = Assert.Single(panel.AssignedSteps);
        Assert.Equal("Поиск", assignedStep.StepTitle);
        Assert.Equal(
            sceneId,
            Assert.Single(architecture.GetPlotLines()[0].Steps.First(step => step.Title == "Поиск").SceneIds));

        await panel.RemoveStepCommand.ExecuteAsync(assignedStep);

        Assert.Empty(panel.AssignedSteps);
        Assert.Empty(architecture.GetPlotLines()[0].Steps.First(step => step.Title == "Поиск").SceneIds);
    }

    [AvaloniaFact]
    public async Task New_plot_line_cycles_color_and_names_steps()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Рукопись.scri") };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        launcher.OpenTimelineCommand.Execute(null);
        var timeline = provider.GetRequiredService<TimelineViewModel>();

        // Цвет новой линии берётся из палитры по кругу: в макете линии книги
        // различаются цветом точки (красный, фиолетовый, бирюзовый, золотой).
        timeline.NewLineCommand.Execute(null);
        Assert.Equal(StoryLabels.LineSwatches[0], timeline.Draft.ColorHex);
        Assert.True(timeline.Swatches[0].IsSelected);

        // Шаги получают осмысленные имена по номеру: «Начало», «Призыв», «Поиск».
        timeline.Draft.Name = "Главная линия";
        timeline.AddStepCommand.Execute(null);
        Assert.Equal("Начало", timeline.SelectedStep!.Title);
        timeline.AddStepCommand.Execute(null);
        Assert.Equal("Призыв", timeline.SelectedStep!.Title);
        timeline.AddStepCommand.Execute(null);
        Assert.Equal("Поиск", timeline.SelectedStep!.Title);

        await timeline.SaveLineCommand.ExecuteAsync(null);
        Assert.False(timeline.HasError, timeline.ErrorMessage);

        // Следующая линия получает следующий цвет палитры.
        timeline.NewLineCommand.Execute(null);
        Assert.Equal(StoryLabels.LineSwatches[1], timeline.Draft.ColorHex);
        timeline.Draft.Name = "Вторая линия";
        await timeline.SaveLineCommand.ExecuteAsync(null);

        var lines = provider.GetRequiredService<IBookArchitectureService>().GetPlotLines();
        Assert.Equal(2, lines.Count);
        Assert.Equal(StoryLabels.LineSwatches[0], lines[0].ColorHex);
        Assert.Equal(StoryLabels.LineSwatches[1], lines[1].ColorHex);
        Assert.Equal(["Начало", "Призыв", "Поиск"], lines[0].Steps.Select(step => step.Title));
    }

    private static async Task<Guid> CreateSceneAsync(IServiceProvider provider)
    {
        var architecture = provider.GetRequiredService<IBookArchitectureService>();
        var projects = provider.GetRequiredService<ScriSelfStudio.Contracts.Projects.IProjectService>();
        var seriesId = projects.Current!.Series[0].Id;
        var book = (await architecture.CreateBookAsync(new CreateBookRequest { SeriesId = seriesId, Title = "Книга" }))
            .Require().Series[0].Books[0];
        var chapter = (await architecture.CreateChapterAsync(
            new CreateChapterRequest { BookId = book.Id, Title = "Глава" })).Require().Series[0].Books[0].Chapters[0];
        var scene = (await architecture.CreateSceneAsync(
            new CreateSceneRequest { ChapterId = chapter.Id, Title = "Сцена" })).Require().Series[0].Books[0].Chapters[0].Scenes[0];
        return scene.Id;
    }
}
