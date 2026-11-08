namespace ScriSelfStudio.UiSmokeTests;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRichEditor.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.StoryUI;
using ScriSelfStudio.Presentation.Theme;
using Avalonia.Media.Imaging;
using ScriSelfStudio.TestSupport;

public sealed class UserFeedbackTests
{
    [AvaloniaFact]
    public async Task Repeated_settings_changes_keep_controls_and_section_available()
    {
        using var context = await SceneContext.CreateAsync();
        var settings = context.Provider.GetRequiredService<SettingsViewModel>();
        var shell = context.Provider.GetRequiredService<ShellViewModel>();
        shell.OpenSettingsCommand.Execute(null);
        settings.SelectedSection = settings.Sections.Single(section => section.Section == SettingsSection.Appearance);
        var window = context.Provider.GetRequiredService<MainWindow>();
        window.Show();
        Flush();
        try
        {
            var section = settings.SelectedSection;
            var themes = settings.Themes;
            var combo = window.GetVisualDescendants().OfType<ComboBox>().Single(item => item.Name == "ThemeChoice");
            foreach (var index in new[] { 1, 3, 2, 0, 1 })
            {
                combo.SelectedIndex = index;
                await settings.PendingApply!;
                Flush();
                Assert.Same(section, settings.SelectedSection);
                Assert.Same(themes, settings.Themes);
                Assert.True(combo.IsEffectivelyVisible && combo.IsEffectivelyEnabled);
                Assert.Equal(themes[index], combo.SelectedItem);
            }
            settings.SelectedNavigation = settings.NavigationOptions.Single(option => option.Value == AppNavigationMode.Menu);
            await settings.PendingApply!;
            Flush();
            Assert.True(shell.UseDropdownNavigation);
            settings.SelectedNavigation = settings.NavigationOptions.Single(option => option.Value == AppNavigationMode.Tabs);
            await settings.PendingApply!;
            Assert.False(shell.UseDropdownNavigation);
            settings.SelectedSection = settings.Sections.Single(section => section.Section == SettingsSection.Saving);
            foreach (var value in new[] { false, true, false, true })
            {
                settings.AutoSaveEnabled = value;
                await settings.PendingApply!;
                Assert.Equal(value, context.Provider.GetRequiredService<IProjectService>().Current!.Settings.AutoSaveEnabled);
                Assert.True(settings.IsSavingSection);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Scene_order_removal_and_character_profiles_survive_refresh_and_save()
    {
        using var context = await SceneContext.CreateAsync();
        var provider = context.Provider;
        var service = provider.GetRequiredService<IBookArchitectureService>();
        var projects = provider.GetRequiredService<IProjectService>();
        var characters = new List<CharacterDto>();
        foreach (var name in new[] { "Анна", "Борис" })
        {
            await service.SaveCharacterAsync(new CharacterDto
            {
                Name = name, Appearance = "Серые глаза", Temperament = "Спокойный", SpeechHabits = "Разберёмся",
                ArcStages = [new() { Title = "Выбор", Description = "Признаёт свою ошибку" }, new() { Title = "Решение", Description = "Просит помощи" }],
            });
            characters.Add(service.GetCharacters().Last());
        }
        foreach (var name in new[] { "Расследование", "Дружба" })
        {
            await service.SavePlotLineAsync(new PlotLineDto
            {
                Name = name,
                Steps = [new() { Title = "Открытие", Description = "Находит письмо",
                    Participants = [.. characters.Select(character => new StepParticipantDto
                    { CharacterId = character.Id, CharacterArcStageId = character.ArcStages[name == "Дружба" ? 1 : 0].Id })] }],
            });
        }
        var first = service.GetPlotLines()[0].Steps[0].Id;
        var second = service.GetPlotLines()[1].Steps[0].Id;
        await projects.UpdateSceneAsync(new UpdateSceneRequest { SceneId = context.SceneId, PlotLineStepIds = [second, first] });
        var panel = provider.GetRequiredService<SceneContextViewModel>();
        panel.Refresh();
        Assert.Equal(new[] { second, first }, panel.AssignedSteps.Select(step => step.StepId));
        foreach (var character in panel.Characters)
        {
            character.ToggleDescriptionCommand.Execute(null);
            character.IsProfileExpanded = true;
            Assert.Equal(2, character.ArcStages.Count);
            Assert.Contains(character.Profile, field => field.Value == "Разберёмся");
            Assert.Contains(character.Profile, field => field.Value == "Серые глаза");
        }
        panel.Refresh();
        Assert.All(panel.Characters, character => Assert.True(character.IsDescriptionExpanded && character.IsProfileExpanded));

        var timeline = provider.GetRequiredService<TimelineViewModel>();
        timeline.Refresh();
        await timeline.MoveLineUpCommand.ExecuteAsync(service.GetPlotLines()[1].Id);
        panel.Refresh();
        Assert.Equal(new[] { second, first }, panel.AssignedSteps.Select(step => step.StepId));
        await panel.MoveStepDownCommand.ExecuteAsync(panel.AssignedSteps[0]);
        Assert.Equal(new[] { first, second }, panel.AssignedSteps.Select(step => step.StepId));
        Assert.Equal("Дружба", service.GetPlotLines()[0].Name);
        Assert.True((await projects.SaveAsync()).IsSuccess);
        var path = projects.CurrentFilePath!;
        await context.Engine.CloseAsync();
        await projects.CloseAsync(true);
        await projects.OpenAsync(path);
        await context.Engine.OpenSceneAsync(context.SceneId);
        panel.Refresh();
        Assert.Equal(new[] { first, second }, panel.AssignedSteps.Select(step => step.StepId));

        var map = provider.GetRequiredService<ArchitectureViewModel>();
        map.Refresh();
        map.Selected = map.Roots[0].Children[0].Children[0].Children[0];
        Assert.Equal("Расследование", map.SceneDetails!.Lines[0].Name);
        Assert.False(map.SceneDetails.Lines[0].MoveUpCommand!.CanExecute(null));
        await map.SceneDetails.Lines[0].MoveDownCommand!.ExecuteAsync(null);
        Assert.Equal("Дружба", map.SceneDetails!.Lines[0].Name);
        await map.SceneDetails.Lines[0].MoveDownCommand!.ExecuteAsync(null);
        Assert.Equal("Расследование", map.SceneDetails!.Lines[0].Name);
        Assert.All(map.SceneDetails.Characters, character =>
        {
            Assert.Equal(2, character.Stages.Count);
            Assert.DoesNotContain(character.Stages, stage => stage.Description == "Находит письмо");
        });
        await map.SceneDetails.Lines[0].Steps[0].RemoveCommand!.ExecuteAsync(null);
        panel.Refresh();
        Assert.Equal(second, Assert.Single(panel.AssignedSteps).StepId);
        await panel.AssignedSteps[0].RemoveCommand.ExecuteAsync(null);
        Assert.Empty(panel.AssignedSteps);
        Assert.NotNull(projects.GetSceneDocument(context.SceneId).Require());
        Assert.Equal(2, service.GetPlotLines().Count);
    }

    [AvaloniaFact]
    public async Task Visible_map_switch_and_chapter_context_menu_act_on_clicked_chapter()
    {
        using var context = await SceneContext.CreateAsync();
        var provider = context.Provider;
        var service = provider.GetRequiredService<IBookArchitectureService>();
        var map = provider.GetRequiredService<ArchitectureViewModel>();
        await service.CreateChapterAsync(new CreateChapterRequest { BookId = service.GetBooks()[0].Id, Title = "Другая глава" });
        provider.GetRequiredService<LauncherViewModel>().OpenArchitectureCommand.Execute(null);
        map.Refresh();
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Flush();
        try
        {
            for (var index = 0; index < 3; index++)
            {
                Click(window, window.FindControl<ToggleButton>("ListModeButton") ?? window.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "ListModeButton"));
                Assert.True(map.IsListMode);
                Click(window, window.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "MapModeButton"));
                Assert.True(map.IsMapMode);
            }
            var targetId = map.MapColumns[1].Node.Id;
            map.Selected = map.MapColumns[0].Node;
            var header = window.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("columnHead")).ElementAt(1);
            var menu = header.ContextMenu!;
            menu.Open(header);
            Flush();
            var move = menu.Items.OfType<MenuItem>().First();
            Assert.NotNull(move.Command);
            await ((IAsyncRelayCommand)move.Command!).ExecuteAsync(move.CommandParameter);
            menu.Close();
            Assert.Equal(targetId, map.MapColumns[0].Node.Id);
            ((StubFileDialogService)provider.GetRequiredService<IFileDialogService>()).ConfirmResult = true;
            map.Selected = map.MapColumns[1].Node;
            await map.DeleteNodeCommand.ExecuteAsync(map.MapColumns[0].Node);
            Assert.DoesNotContain(service.GetBooks()[0].Chapters, chapter => chapter.Id == targetId);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Typing_dialogue_prefix_produces_em_dash_and_preserves_undo_and_storage()
    {
        using var context = await SceneContext.CreateAsync();
        var window = context.Provider.GetRequiredService<MainWindow>();
        window.Show();
        Flush();
        try
        {
            var rich = window.GetVisualDescendants().OfType<RichEditor>().Single();
            Assert.True(rich.Focus());
            window.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
            window.KeyTextInput("-");
            window.KeyTextInput(" ");
            window.KeyTextInput("Привет");
            Flush();
            var engine = context.Engine;
            Assert.Equal("— Привет", engine.PlainText);
            Assert.All(engine.GetDocumentSnapshot().Require().Document.Blocks, block => Assert.Equal(BlockKind.Paragraph, block.Kind));
            rich.Undo();
            Flush();
            Assert.DoesNotContain("Привет", engine.PlainText, StringComparison.Ordinal);
            rich.Redo();
            Flush();
            Assert.Equal("— Привет", engine.PlainText);
            var editor = context.Provider.GetRequiredService<EditorViewModel>();
            await editor.SaveCommand.ExecuteAsync(null);
            var projects = context.Provider.GetRequiredService<IProjectService>();
            var path = projects.CurrentFilePath!;
            await engine.CloseAsync();
            await projects.CloseAsync(true);
            await projects.OpenAsync(path);
            Assert.Equal("— Привет", projects.GetSceneDocument(context.SceneId).Require().Document.ToPlainText());
        }
        finally { window.Close(); }
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Starter)]
    [InlineData(AppTheme.GreenStart)]
    [InlineData(AppTheme.Jam)]
    [InlineData(AppTheme.Cabinet)]
    public async Task Feedback_controls_render_in_all_themes_and_compact_windows(AppTheme theme)
    {
        using var context = await SceneContext.CreateAsync();
        var provider = context.Provider;
        var service = provider.GetRequiredService<IBookArchitectureService>();
        var projects = provider.GetRequiredService<IProjectService>();
        await provider.GetRequiredService<IAppSettingsService>().UpdateAsync(settings => settings.Theme = theme);
        provider.GetRequiredService<IThemeService>().Apply(theme);
        provider.GetRequiredService<ShellViewModel>().IsScenePanelRequested = true;
        foreach (var name in new[] { "Анна", "Борис" })
        {
            await service.SaveCharacterAsync(new CharacterDto { Name = name,
                Appearance = "Серые глаза, короткие тёмные волосы.", Temperament = "Спокойный, но упрямый.", SpeechHabits = "Сначала разберёмся.",
                ArcStages = [new() { Title = "Выбор", Description = "Признаёт свою ошибку и решает помочь другу." }] });
        }
        await service.SavePlotLineAsync(new PlotLineDto { Name = "Тайна старого письма", Steps =
            [new() { Title = "Неожиданная встреча", Description = "Герои находят письмо и обсуждают, кому можно доверять.",
                Participants = [.. service.GetCharacters().Select(character => new StepParticipantDto
                { CharacterId = character.Id, CharacterArcStageId = character.ArcStages[0].Id })] }] });
        await projects.UpdateSceneAsync(new UpdateSceneRequest { SceneId = context.SceneId,
            Description = "Встреча в доме у реки", PlotLineStepIds = [service.GetPlotLines()[0].Steps[0].Id] });
        var panel = provider.GetRequiredService<SceneContextViewModel>();
        panel.Refresh();
        foreach (var character in panel.Characters)
        {
            character.IsDescriptionExpanded = true;
            character.IsProfileExpanded = true;
        }
        var window = provider.GetRequiredService<MainWindow>();
        window.Width = 1280;
        window.Height = 900;
        window.Show();
        try
        {
            Capture(window, $"feedback-editor-{theme}");
            Assert.All(panel.Characters, character => Assert.True(character.IsDescriptionExpanded));
            provider.GetRequiredService<ShellViewModel>().OpenMapCommand.Execute(null);
            var map = provider.GetRequiredService<ArchitectureViewModel>();
            map.Refresh();
            map.Selected = map.Roots[0].Children[0].Children[0].Children[0];
            map.SceneDetails!.Lines[0].IsExpanded = true;
            Capture(window, $"feedback-map-{theme}");
            window.Width = 940;
            window.Height = 650;
            Capture(window, $"feedback-map-compact-{theme}");
            var switchButton = window.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "ListModeButton");
            Assert.True(switchButton.IsEffectivelyVisible);
            Click(window, switchButton);
            Assert.True(map.IsListMode);
            Click(window, window.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "MapModeButton"));
            Assert.True(map.IsMapMode);
            var directory = Environment.GetEnvironmentVariable("SCRISELF_RENDER_DIR");
            if (theme == AppTheme.Starter && !string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                Assert.True((await projects.SaveAsAsync(Path.Combine(directory, "feedback-demo.scri"))).IsSuccess);
            }
        }
        finally { window.Close(); }
    }

    private static void Capture(Window window, string name)
    {
        Flush();
        var directory = Environment.GetEnvironmentVariable("SCRISELF_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }

    private static void Click(Window window, Control control)
    {
        control.BringIntoView();
        Flush();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush();
    }
}
