namespace ScriSelfStudio.UiSmokeTests;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Core.Model;
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

        // Роль — выпадающий список: выбор должен сразу попадать в черновик.
        var roleCombo = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(combo => ReferenceEquals(combo.ItemsSource, characters.Roles));
        var mentor = characters.Roles.Single(role => role.Value == CharacterRole.Mentor);
        roleCombo.SelectedItem = mentor;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(mentor, characters.Draft.Role);

        characters.Draft.Name = "Анна";
        characters.Draft.Age = "25 лет";
        characters.Draft.FalseBelief = "Деньги решают всё";
        characters.Draft.TagsText = "маг, предатель";
        characters.Draft.Status = characters.Statuses.Single(status => status.Value == CharacterStatus.Dead);
        characters.AddArcStageCommand.Execute(null);
        characters.Draft.ArcStages[^1].Title = "Своя ступень";
        await characters.SaveCharacterCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(characters.HasError, characters.ErrorMessage);
        var saved = Assert.Single(characters.Characters);
        Assert.Equal("Анна", saved.Name);
        Assert.Equal(8, saved.ArcStageCount);
        var characterList = window.GetVisualDescendants().OfType<ListBox>()
            .Single(list => ReferenceEquals(list.ItemsSource, characters.Characters));
        var renderedCharacterText = characterList.GetVisualDescendants().OfType<TextBlock>()
            .Select(textBlock => textBlock.Text)
            .ToArray();
        Assert.Contains("Анна", renderedCharacterText);
        Assert.Contains("Мёртв", renderedCharacterText);
        Assert.Contains("Наставник", renderedCharacterText);
        Assert.DoesNotContain(renderedCharacterText, text => text?.StartsWith("CharacterListItem", StringComparison.Ordinal) == true);

        var card = provider.GetRequiredService<IBookArchitectureService>().GetCharacters()[0];
        Assert.Equal("25 лет", card.Age);
        Assert.Equal(CharacterRole.Mentor, card.Role);
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
    public async Task Character_draft_is_saved_when_leaving_the_screen()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Рукопись.scri") };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var navigation = provider.GetRequiredService<INavigationService>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        launcher.OpenCharactersCommand.Execute(null);
        var characters = provider.GetRequiredService<CharactersViewModel>();
        characters.Draft.Name = "Анна";
        await characters.SaveCharacterCommand.ExecuteAsync(null);

        characters.Draft.Description = "Описание, набранное без кнопки сохранения.";
        navigation.NavigateTo(AppScreen.Launcher);

        Assert.False(characters.HasError, characters.ErrorMessage);
        var card = Assert.Single(provider.GetRequiredService<IBookArchitectureService>().GetCharacters());
        Assert.Equal("Описание, набранное без кнопки сохранения.", card.Description);
    }

    [AvaloniaFact]
    public async Task Character_draft_is_saved_when_switching_to_another_card()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Рукопись.scri") };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        launcher.OpenCharactersCommand.Execute(null);
        var characters = provider.GetRequiredService<CharactersViewModel>();
        characters.Draft.Name = "Анна";
        await characters.SaveCharacterCommand.ExecuteAsync(null);
        var annaId = characters.Characters.Single().Id;

        characters.NewCharacterCommand.Execute(null);
        characters.Draft.Name = "Идан";
        await characters.SaveCharacterCommand.ExecuteAsync(null);

        characters.Selected = characters.Characters.Single(item => item.Id == annaId);
        characters.Draft.Description = "Описание Анны без кнопки сохранения.";
        characters.Selected = characters.Characters.Single(item => item.Name == "Идан");

        var anna = provider.GetRequiredService<IBookArchitectureService>()
            .GetCharacters().Single(card => card.Id == annaId);
        Assert.Equal("Описание Анны без кнопки сохранения.", anna.Description);
    }

    [AvaloniaFact]
    public async Task Timeline_draft_is_saved_when_switching_to_another_line()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Рукопись.scri") };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        launcher.OpenTimelineCommand.Execute(null);
        var timeline = provider.GetRequiredService<TimelineViewModel>();
        timeline.NewLineCommand.Execute(null);
        timeline.Draft.Name = "Линия А";
        await timeline.SaveLineCommand.ExecuteAsync(null);
        var firstId = timeline.PlotLines.Single().Id;

        timeline.NewLineCommand.Execute(null);
        timeline.Draft.Name = "Линия Б";
        await timeline.SaveLineCommand.ExecuteAsync(null);

        timeline.SelectedLine = timeline.PlotLines.Single(line => line.Id == firstId);
        timeline.Draft.Goal = "Цель без кнопки сохранения";
        timeline.SelectedLine = timeline.PlotLines.Single(line => line.Name == "Линия Б");

        var first = provider.GetRequiredService<IBookArchitectureService>()
            .GetPlotLines().Single(line => line.Id == firstId);
        Assert.Equal("Цель без кнопки сохранения", first.Goal);
    }

    [AvaloniaFact]
    public async Task Timeline_step_tabs_survive_field_commit_and_participant_changes()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Рукопись.scri") };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        launcher.OpenCharactersCommand.Execute(null);
        var characters = provider.GetRequiredService<CharactersViewModel>();
        characters.Draft.Name = "Анна";
        await characters.SaveCharacterCommand.ExecuteAsync(null);

        launcher.OpenTimelineCommand.Execute(null);
        var timeline = provider.GetRequiredService<TimelineViewModel>();
        await timeline.NewLineCommand.ExecuteAsync(null);
        timeline.Draft.Name = "Главная линия";
        await timeline.SaveLineCommand.ExecuteAsync(null);
        timeline.AddStepCommand.Execute(null);
        var originalStep = timeline.SelectedStep!;
        var originalTitle = originalStep.Title;

        var window = provider.GetRequiredService<MainWindow>();
        window.Width = 1152;
        window.Height = 768;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var tabs = window.GetVisualDescendants().OfType<TabControl>()
            .Single(control => control.Classes.Contains("stepTabs"));
        var titleInput = tabs.GetVisualDescendants().OfType<TextBox>()
            .Single(input => input.Classes.Contains("fieldBox") && !input.AcceptsReturn);
        Assert.True(titleInput.Focus());
        titleInput.CaretIndex = titleInput.Text!.Length;
        window.KeyTextInput(" изменено");

        var arcTab = (TabItem)tabs.Items[2]!;
        var point = arcTab.TranslatePoint(new Point(arcTab.Bounds.Width / 2, arcTab.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        if (timeline.CommitLineCommand.ExecutionTask is { } commit)
        {
            await commit;
        }
        Dispatcher.UIThread.RunJobs();

        var savedStep = Assert.Single(provider.GetRequiredService<IBookArchitectureService>().GetPlotLines().Single().Steps);
        Assert.Equal(originalTitle + " изменено", savedStep.Title);
        Assert.Equal(2, timeline.SelectedStep!.SelectedTabIndex);
        tabs = window.GetVisualDescendants().OfType<TabControl>()
            .Single(control => control.Classes.Contains("stepTabs"));
        Assert.Equal(2, tabs.SelectedIndex);

        timeline.AddParticipantCommand.Execute(null);
        timeline.SelectedStep.Participants.Single().Character = Assert.Single(timeline.CharacterChoices);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, timeline.SelectedStep.SelectedTabIndex);
        var participant = Assert.Single(timeline.SelectedStep.Participants);
        participant.ArcStage = participant.AvailableSteps[0];
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, timeline.SelectedStep.SelectedTabIndex);

        await timeline.SaveLineCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        tabs = window.GetVisualDescendants().OfType<TabControl>()
            .Single(control => control.Classes.Contains("stepTabs"));
        Assert.Equal(2, tabs.SelectedIndex);
        Assert.Equal("Анна", timeline.SelectedStep!.Participants.Single().Character!.Name);
        Assert.NotNull(timeline.SelectedStep.Participants.Single().ArcStage);

        timeline.ToggleHeaderEditingCommand.Execute(null);
        Assert.True(timeline.IsHeaderEditing);
        await timeline.CommitLineCommand.ExecuteAsync(null);
        Assert.False(timeline.IsHeaderEditing);
        Assert.False(timeline.HasUnsavedDraft);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Scene_description_is_saved_when_switching_to_another_scene()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService
        {
            CreatePath = workspace.FilePath("Рукопись.scri"),
            PromptResult = "Сцена",
        };
        dialogs.PromptResponses.Enqueue("Книга");
        dialogs.PromptResponses.Enqueue("Глава");
        dialogs.PromptResponses.Enqueue("Сцена 1");
        dialogs.PromptResponses.Enqueue("Сцена 2");
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);
        launcher.OpenArchitectureCommand.Execute(null);

        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        architecture.Selected = architecture.Roots[0];
        await architecture.CreateBookCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0];
        await architecture.CreateChapterCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0].Children[0];
        await architecture.CreateSceneCommand.ExecuteAsync(null);
        await architecture.CreateSceneCommand.ExecuteAsync(null);

        var chapter = architecture.Roots[0].Children[0].Children[0];
        var firstSceneId = chapter.Children[0].Id;
        architecture.Selected = chapter.Children[0];
        architecture.SceneDetails!.Description = "Описание сцены без кнопки сохранения.";
        architecture.Selected = chapter.Children[1];

        var scene = provider.GetRequiredService<ScriSelfStudio.Contracts.Projects.IProjectService>()
            .Current!.Series.SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Scenes)
            .Single(item => item.Id == firstSceneId);
        Assert.Equal("Описание сцены без кнопки сохранения.", scene.Description);
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

    [AvaloniaFact]
    public async Task Characters_screen_right_panel_reads_relations_lines_and_scenes()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Рукопись.scri") };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var sceneId = await CreateSceneAsync(provider);
        var architecture = provider.GetRequiredService<IBookArchitectureService>();

        var withIdan = (await architecture.SaveCharacterAsync(new CharacterDto { Name = "Идан" })).Require();
        var idanId = withIdan.Characters.Single(card => card.Name == "Идан").Id;
        var withAnna = (await architecture.SaveCharacterAsync(new CharacterDto
        {
            Name = "Анна",
            Relations = [new CharacterRelationDto { CharacterId = idanId, Type = CharacterRelationType.Friend }],
        })).Require();
        var annaId = withAnna.Characters.Single(card => card.Name == "Анна").Id;

        await architecture.SavePlotLineAsync(new PlotLineDto
        {
            Name = "Главная линия",
            Status = PlotLineStatus.InProgress,
            Steps =
            [
                new PlotLineStepDto
                {
                    Title = "Поиск",
                    Participants = [new StepParticipantDto { CharacterId = annaId }],
                    SceneIds = [sceneId],
                },
            ],
        });

        launcher.OpenCharactersCommand.Execute(null);
        var characters = provider.GetRequiredService<CharactersViewModel>();
        characters.Selected = characters.Characters.Single(item => item.Id == annaId);

        // Правая панель — не заглушка: связи, линии и сцены берутся из проекта.
        var relation = Assert.Single(characters.RelatedCharacters);
        Assert.Equal("Идан", relation.Name);
        Assert.Equal("Друг", relation.RelationLabel);

        var line = Assert.Single(characters.RelatedLines);
        Assert.Equal("Главная линия", line.Name);
        Assert.Equal("В работе", line.StatusLabel);
        Assert.True(characters.HasRelatedLines);

        Assert.Equal(1, characters.SceneCount);
        Assert.Equal("1 сцена", characters.SceneCountLabel);

        // Правка связей в карточке сразу отражается в панели.
        characters.RemoveRelationCommand.Execute(characters.Draft.Relations[0]);
        Assert.Empty(characters.RelatedCharacters);
        Assert.False(characters.HasRelatedCharacters);
    }
    [AvaloniaFact]
    public async Task Shell_flush_saves_pending_text_and_scene_context()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Рукопись.scri") };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var sceneId = await CreateSceneAsync(provider);
        var editor = provider.GetRequiredService<ScriSelfStudio.EditorUI.EditorViewModel>();
        await editor.Engine.OpenSceneAsync(sceneId);
        editor.Text = "Текст перед закрытием приложения";

        var panel = provider.GetRequiredService<SceneContextViewModel>();
        panel.Refresh();
        panel.Summary = "Описание перед закрытием приложения";

        var shell = provider.GetRequiredService<ShellViewModel>();
        Assert.True(shell.HasPendingEdits);

        await shell.FlushPendingEditsAsync();

        Assert.False(editor.IsDirty);
        Assert.False(panel.HasPendingChanges);
        var scene = provider.GetRequiredService<ScriSelfStudio.Contracts.Projects.IProjectService>()
            .Current!.Series.SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Scenes)
            .Single(item => item.Id == sceneId);
        Assert.Equal("Описание перед закрытием приложения", scene.Description);
        Assert.False(shell.HasPendingEdits);
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
