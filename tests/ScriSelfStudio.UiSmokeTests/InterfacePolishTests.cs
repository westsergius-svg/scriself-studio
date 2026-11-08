namespace ScriSelfStudio.UiSmokeTests;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation.Export;
using ScriSelfStudio.Presentation.Localization;
using ScriSelfStudio.Presentation.Theme;
using ScriSelfStudio.StoryUI;
using ScriSelfStudio.TestSupport;

/// <summary>Пользовательские сценарии изменяемых панелей, навигации и перевода.</summary>
public sealed class InterfacePolishTests
{
    [AvaloniaTheory]
    [InlineData(AppTheme.Starter, 800)]
    [InlineData(AppTheme.Starter, 1152)]
    [InlineData(AppTheme.Starter, 1600)]
    [InlineData(AppTheme.GreenStart, 800)]
    [InlineData(AppTheme.GreenStart, 1152)]
    [InlineData(AppTheme.GreenStart, 1600)]
    [InlineData(AppTheme.Cabinet, 800)]
    [InlineData(AppTheme.Cabinet, 1152)]
    [InlineData(AppTheme.Cabinet, 1600)]
    [InlineData(AppTheme.Jam, 800)]
    public async Task Every_workspace_pane_can_be_dragged_in_both_directions(AppTheme theme, int width)
    {
        using var context = await SceneContext.CreateAsync();
        await ApplyThemeAsync(context.Provider, theme);
        await SeedStoryAsync(context);
        var navigation = context.Provider.GetRequiredService<INavigationService>();
        var window = context.Provider.GetRequiredService<MainWindow>();
        window.Width = width;
        window.Height = 900;
        window.Show();
        Flush();
        try
        {
            (AppScreen Screen, string Grid)[] workspaces =
            [
                (AppScreen.Characters, "CharactersWorkspace"),
                (AppScreen.Timeline, "TimelineWorkspace"),
                (AppScreen.Architecture, "MapWorkspace"),
                (AppScreen.Export, "ExportWorkspace"),
                (AppScreen.Settings, "SettingsWorkspace"),
                (AppScreen.Launcher, "LauncherWorkspace"),
            ];
            foreach (var (screen, name) in workspaces)
            {
                Assert.True(navigation.NavigateTo(screen).IsSuccess);
                Flush();
                var grid = Find<Grid>(window, name);
                Assert.True(grid.IsEffectivelyVisible, name);
                var splitters = grid.Children.OfType<GridSplitter>().ToArray();
                Assert.NotEmpty(splitters);
                Capture(window, $"panes-{theme}-{width}-{screen}");
                foreach (var splitter in splitters)
                {
                    AssertInsideWindow(window, splitter);
                    var index = Grid.GetColumn(splitter);
                    var previous = grid.ColumnDefinitions[index - 1];
                    var next = grid.ColumnDefinitions[index + 1];
                    var before = previous.ActualWidth;
                    var nextSlack = next.ActualWidth - next.MinWidth;
                    var previousSlack = previous.ActualWidth - previous.MinWidth;
                    var delta = nextSlack >= previousSlack ? Math.Min(35, nextSlack) : -Math.Min(35, previousSlack);
                    Assert.True(Math.Abs(delta) >= 8, $"{theme}/{width}/{name}: no room to resize {splitter.Name}");
                    Drag(window, splitter, delta);
                    Assert.InRange(Math.Abs(previous.ActualWidth - before - delta), 0, 1);
                    Drag(window, splitter, -delta);
                    Assert.InRange(Math.Abs(previous.ActualWidth - before), 0, 1);
                    AssertInsideWindow(window, splitter);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Starter, 800)]
    [InlineData(AppTheme.Starter, 1152)]
    [InlineData(AppTheme.Starter, 1600)]
    [InlineData(AppTheme.GreenStart, 800)]
    [InlineData(AppTheme.Cabinet, 800)]
    [InlineData(AppTheme.Cabinet, 1152)]
    [InlineData(AppTheme.Jam, 800)]
    public async Task Arc_stage_cross_remains_visible_and_deletes_only_its_stage(AppTheme theme, int width)
    {
        using var context = await SceneContext.CreateAsync();
        await ApplyThemeAsync(context.Provider, theme);
        var characterId = await SeedStoryAsync(context);
        var navigation = context.Provider.GetRequiredService<INavigationService>();
        navigation.NavigateTo(AppScreen.Characters);
        var characters = context.Provider.GetRequiredService<CharactersViewModel>();
        characters.Selected = characters.Characters.Single(character => character.Id == characterId);
        var target = characters.Draft.ArcStages.Last();
        var retained = characters.Draft.ArcStages.First();
        var window = context.Provider.GetRequiredService<MainWindow>();
        window.Width = width;
        window.Height = 768;
        window.Show();
        Flush();
        try
        {
            var button = window.GetVisualDescendants().OfType<Button>()
                .Single(control => ReferenceEquals(control.CommandParameter, target));
            button.BringIntoView();
            Flush();
            AssertInsideWindow(window, button);
            var viewport = button.GetVisualAncestors().OfType<ScrollViewer>().First();
            var position = button.TranslatePoint(default, viewport)!.Value;
            Assert.True(position.X >= 0 && position.X + button.Bounds.Width <= viewport.Bounds.Width,
                $"Delete cross clipped at {theme}/{width}: {position.X} + {button.Bounds.Width} > {viewport.Bounds.Width}");
            Assert.True(button.Bounds.Width >= 32 && button.Bounds.Height >= 32);
            Assert.IsType<PathIcon>(button.Content);
            Capture(window, $"delete-{theme}-{width}");
            Click(window, button);
            Assert.DoesNotContain(characters.Draft.ArcStages, stage => stage.Id == target.Id);
            Assert.Contains(characters.Draft.ArcStages, stage => stage.Id == retained.Id);
            await characters.SaveCharacterCommand.ExecuteAsync(null);
            var saved = context.Provider.GetRequiredService<IBookArchitectureService>().GetCharacters()
                .Single(character => character.Id == characterId);
            Assert.Single(saved.ArcStages);
            Assert.Equal(retained.Id, saved.ArcStages[0].Id);
            Flush();
            var lastCross = window.GetVisualDescendants().OfType<Button>()
                .Single(control => control.CommandParameter is ArcStageDraft);
            Click(window, lastCross);
            await characters.SaveCharacterCommand.ExecuteAsync(null);
            characters.Refresh();
            Assert.Empty(characters.Draft.ArcStages);
            Assert.Empty(context.Provider.GetRequiredService<IBookArchitectureService>().GetCharacters().Single().ArcStages);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Starter)]
    [InlineData(AppTheme.GreenStart)]
    [InlineData(AppTheme.Cabinet)]
    public async Task Map_expanders_show_all_assigned_steps_and_arc_stages_and_survive_save(AppTheme theme)
    {
        using var context = await SceneContext.CreateAsync();
        await ApplyThemeAsync(context.Provider, theme);
        await SeedStoryAsync(context);
        context.Provider.GetRequiredService<INavigationService>().NavigateTo(AppScreen.Architecture);
        var architecture = context.Provider.GetRequiredService<ArchitectureViewModel>();
        architecture.Refresh();
        architecture.Selected = architecture.Roots[0].Children[0].Children[0];
        var window = context.Provider.GetRequiredService<MainWindow>();
        window.Width = 1152;
        window.Height = 900;
        window.Show();
        Flush();
        try
        {
            var sceneButton = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.IsEffectivelyVisible
                    && button.CommandParameter is ArchitectureNodeViewModel node && node.Id == context.SceneId);
            Click(window, sceneButton);
            Assert.True(architecture.ShowSceneDetails);
            Assert.Equal(context.SceneId, architecture.SceneDetails!.SceneId);
            var line = Expander(window, "sceneLineDescription");
            Assert.False(line.IsExpanded);
            var descriptionInput = Find<Border>(window, "MapScenePanel").GetVisualDescendants().OfType<TextBox>().First();
            descriptionInput.BringIntoView();
            Flush();
            Assert.True(descriptionInput.Focus());
            window.KeyTextInput("Описание после редактирования");
            Click(window, line.GetVisualDescendants().OfType<ToggleButton>().First());
            line = Expander(window, "sceneLineDescription");
            Assert.True(line.IsExpanded);
            Assert.True(architecture.SceneDetails!.Lines.Single().IsExpanded);
            AssertVisibleText(line, "Описание линии для проверки");
            AssertVisibleText(line, "Описание первого шага");
            AssertVisibleText(line, "Описание второго шага");

            var character = Expander(window, "sceneCharacterDescription");
            Click(window, character.GetVisualDescendants().OfType<ToggleButton>().First());
            Assert.True(character.IsExpanded);
            AssertVisibleText(character, "Описание начального этапа");
            AssertVisibleText(character, "Описание финального этапа");
            Assert.DoesNotContain(character.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Описание первого шага");
            Assert.DoesNotContain(character.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Описание второго шага");
            Capture(window, $"map-descriptions-{theme}");

            // Сохранение описания сцены обновляет снимок; раскрытые секции остаются открытыми.
            architecture.SceneDetails.Description = "Изменённое описание сцены";
            await architecture.SceneDetails.SaveDescriptionCommand.ExecuteAsync(null);
            architecture.Refresh();
            Flush();
            Assert.True(Expander(window, "sceneLineDescription").IsExpanded);
            Assert.True(Expander(window, "sceneCharacterDescription").IsExpanded);
            AssertVisibleText(Expander(window, "sceneCharacterDescription"), "Описание финального этапа");
            var grid = Find<Grid>(window, "MapWorkspace");
            var panelWidth = grid.ColumnDefinitions[2].ActualWidth;
            architecture.IsCompact = true;
            Flush();
            Assert.False(Find<Border>(window, "MapScenePanel").IsVisible);
            Assert.Equal(0, grid.ColumnDefinitions[2].ActualWidth);
            architecture.IsCompact = false;
            Flush();
            Assert.InRange(Math.Abs(grid.ColumnDefinitions[2].ActualWidth - panelWidth), 0, 1);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Starter, AppNavigationMode.Menu)]
    [InlineData(AppTheme.GreenStart, AppNavigationMode.Menu)]
    [InlineData(AppTheme.Cabinet, AppNavigationMode.Tabs)]
    [InlineData(AppTheme.Jam, AppNavigationMode.Tabs)]
    public async Task Navigation_choice_is_independent_of_theme_and_survives_restart(AppTheme theme, AppNavigationMode mode)
    {
        using var context = await SceneContext.CreateAsync();
        await ApplyThemeAsync(context.Provider, theme);
        var settings = context.Provider.GetRequiredService<SettingsViewModel>();
        var navigation = context.Provider.GetRequiredService<INavigationService>();
        navigation.NavigateTo(AppScreen.Settings);
        settings.SelectedSection = settings.Sections.Single(section => section.Section == SettingsSection.Appearance);
        var window = context.Provider.GetRequiredService<MainWindow>();
        window.Show();
        Flush();
        try
        {
            var choice = Find<ComboBox>(window, "NavigationChoice");
            choice.SelectedItem = settings.NavigationOptions.Single(option => option.Value == mode);
            await settings.PendingApply!;
            Flush();
            var shell = context.Provider.GetRequiredService<ShellViewModel>();
            Assert.Equal(mode == AppNavigationMode.Menu, shell.UseDropdownNavigation);
            var alternateTheme = theme is AppTheme.Starter or AppTheme.GreenStart ? AppTheme.Cabinet : AppTheme.GreenStart;
            settings.SelectedTheme = settings.Themes.Single(option => option.Value == alternateTheme);
            await settings.PendingApply!;
            Flush();
            Assert.Equal(mode, settings.SelectedNavigation.Value);
            Assert.Equal(mode == AppNavigationMode.Menu, shell.UseDropdownNavigation);
            Assert.Equal(alternateTheme == AppTheme.Cabinet, shell.IsCompactLayout);
            navigation.NavigateTo(AppScreen.Characters);
            Flush();
            Assert.Equal(mode == AppNavigationMode.Tabs, shell.ShowClassicHeader);
            Assert.Equal(mode == AppNavigationMode.Menu, shell.ShowCabinetChrome);
            if (mode == AppNavigationMode.Menu)
            {
                var menuButton = Find<Button>(window, "WorkspaceMenuButton");
                Click(window, menuButton);
                var menu = Assert.IsType<MenuFlyout>(menuButton.Flyout);
                Assert.True(menu.IsOpen);
                Assert.Equal(7, menu.Items.OfType<MenuItem>().Count());
                menu.Hide();
            }
            settings.SelectedTheme = settings.Themes.Single(option => option.Value == theme);
            await settings.PendingApply!;
            Flush();
            Assert.Equal(mode == AppNavigationMode.Menu, shell.UseDropdownNavigation);
            Capture(window, $"navigation-{theme}-{mode}");
        }
        finally
        {
            window.Close();
        }

        using var restarted = ServiceProviderFactory.Create(context.Workspace.Root, new StubFileDialogService());
        await restarted.GetRequiredService<LauncherViewModel>().InitializeAsync();
        Assert.Equal(theme, restarted.GetRequiredService<IThemeService>().Current);
        Assert.Equal(mode, restarted.GetRequiredService<IAppSettingsService>().Current.NavigationMode);
        Assert.Equal(mode == AppNavigationMode.Menu, restarted.GetRequiredService<ShellViewModel>().UseDropdownNavigation);
    }

    [AvaloniaFact]
    public async Task Simplified_chinese_updates_controls_preserves_export_choices_and_is_saved()
    {
        using var context = await SceneContext.CreateAsync();
        await SeedStoryAsync(context);
        var export = context.Provider.GetRequiredService<ExportViewModel>();
        export.SelectedFormat = export.Formats.Single(format => format.Key == "PDF");
        export.SelectedDocumentStyle = export.DocumentStyles[2];
        export.SelectedFont = "Georgia";
        export.SelectedFontSize = "16";
        export.SelectedLineSpacing = "2.0";
        var settings = context.Provider.GetRequiredService<SettingsViewModel>();
        settings.Load();
        settings.SelectedTheme = settings.Themes.Single(option => option.Value == AppTheme.GreenStart);
        await settings.PendingApply!;
        var window = context.Provider.GetRequiredService<MainWindow>();
        var navigation = context.Provider.GetRequiredService<INavigationService>();
        navigation.NavigateTo(AppScreen.Settings);
        window.Width = 1152;
        window.Height = 900;
        window.Show();
        Flush();
        try
        {
            var language = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => ReferenceEquals(control.ItemsSource, settings.Languages));
            language.SelectedItem = settings.Languages.Single(option => option.Value == "zh-CN");
            await settings.PendingApply!;
            Flush();
            Assert.True(LocalizationManager.Instance.IsChinese);
            AssertVisibleText(window, "设置");
            Assert.True(language.IsEffectivelyVisible);
            Assert.Equal(AppTheme.GreenStart, settings.SelectedTheme.Value);
            Assert.Equal("绿色起点", settings.SelectedTheme.Title);
            Assert.Equal(new[] { "标准", "绿色起点", "果酱", "书房", "湿沥青" }, settings.Themes.Select(option => option.Title));
            Assert.Equal("主角", StoryLabels.Roles.Single(role => role.Value == CharacterRole.Protagonist).Label);
            Assert.Equal("PDF", export.SelectedFormat.Key);
            Assert.Equal(export.DocumentStyles[2], export.SelectedDocumentStyle);
            Assert.Equal("Georgia", export.SelectedFont);
            Assert.Equal("16", export.SelectedFontSize);
            Assert.Equal("2.0", export.SelectedLineSpacing);
            Assert.Contains(export.Formats, option => option.Description.Contains('电'));
            Assert.Equal("整本书", export.Ranges[0].Title);
            Capture(window, "chinese-settings");
            foreach (var screen in new[] { AppScreen.Characters, AppScreen.Timeline, AppScreen.Architecture, AppScreen.Export })
            {
                navigation.NavigateTo(screen);
                if (screen == AppScreen.Characters)
                {
                    var characters = context.Provider.GetRequiredService<CharactersViewModel>();
                    characters.Selected = characters.Characters.Single();
                }
                Flush();
                Capture(window, $"chinese-{screen}");
            }
            Assert.Equal("Анна — 用户内容", context.Provider.GetRequiredService<IBookArchitectureService>().GetCharacters().Single().Name);
            // Новый процесс начинает с русского языка, а не с глобального состояния предыдущего окна.
            context.Provider.GetRequiredService<ICultureService>().Apply("ru-RU");
            Assert.False(LocalizationManager.Instance.IsChinese);
            using var restarted = ServiceProviderFactory.Create(context.Workspace.Root, new StubFileDialogService());
            await restarted.GetRequiredService<LauncherViewModel>().InitializeAsync();
            Assert.Equal("zh-CN", restarted.GetRequiredService<IAppSettingsService>().Current.Language);
            Assert.Equal(AppTheme.GreenStart, restarted.GetRequiredService<IThemeService>().Current);
            Assert.True(LocalizationManager.Instance.IsChinese);
        }
        finally
        {
            settings.SelectedLanguage = settings.Languages.Single(option => option.Value == "ru-RU");
            await settings.PendingApply!;
            window.Close();
        }
    }

    [Fact]
    public void Green_start_uses_the_cabinet_palette_while_standard_keeps_its_blue_accent()
    {
        Assert.Equal(ThemePalette.Cabinet.Brushes.OrderBy(pair => pair.Key), ThemePalette.GreenStart.Brushes.OrderBy(pair => pair.Key));
        Assert.Equal("#38634F", ThemePalette.GreenStart.Brushes["AccentBrush"]);
        Assert.Equal("#3B8AE5", ThemePalette.Starter.Brushes["AccentBrush"]);
    }

    private static async Task ApplyThemeAsync(IServiceProvider provider, AppTheme theme)
    {
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Load();
        settings.SelectedTheme = settings.Themes.Single(option => option.Value == theme);
        if (settings.PendingApply is { } apply) await apply;
    }

    private static async Task<Guid> SeedStoryAsync(SceneContext context)
    {
        var books = context.Provider.GetRequiredService<IBookArchitectureService>();
        var snapshot = (await books.SaveCharacterAsync(new CharacterDto
        {
            Name = "Анна — 用户内容",
            Role = CharacterRole.Protagonist,
            ArcStages =
            [
                new() { Title = "Начальное состояние", Description = "Описание начального этапа", Kind = CharacterArcStageKind.InitialState },
                new() { Title = "Финальное состояние", Description = "Описание финального этапа", Kind = CharacterArcStageKind.FinalState },
            ],
        })).Require();
        var character = snapshot.Characters.Single();
        snapshot = (await books.SavePlotLineAsync(new PlotLineDto
        {
            Name = "Линия Анны",
            Description = "Описание линии для проверки",
            ColorHex = "#38634F",
            Steps =
            [
                new() { Title = "Первый шаг", Description = "Описание первого шага", Participants = [new() { CharacterId = character.Id, CharacterArcStageId = character.ArcStages[0].Id }] },
                new() { Title = "Второй шаг", Description = "Описание второго шага", Participants = [new() { CharacterId = character.Id, CharacterArcStageId = character.ArcStages[1].Id }] },
            ],
        })).Require();
        var steps = snapshot.PlotLines.Single().Steps;
        var assigned = await context.Provider.GetRequiredService<IProjectService>().UpdateSceneAsync(new UpdateSceneRequest
        {
            SceneId = context.SceneId,
            PlotLineStepIds = [.. steps.Select(step => step.Id)],
        });
        Assert.True(assigned.IsSuccess, assigned.Error?.UserMessage);
        return character.Id;
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static Expander Expander(Window window, string className) =>
        window.GetVisualDescendants().OfType<Expander>().Single(control => control.Classes.Contains(className));

    private static void AssertVisibleText(Control control, string expected) =>
        Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == expected && text.IsEffectivelyVisible);

    private static void AssertInsideWindow(Window window, Control control)
    {
        var position = control.TranslatePoint(default, window)!.Value;
        Assert.True(position.X >= 0 && position.X + control.Bounds.Width <= window.ClientSize.Width + 1,
            $"{control.Name ?? control.GetType().Name} outside window: {position.X} + {control.Bounds.Width} > {window.ClientSize.Width}");
    }

    private static void Drag(Window window, Control control, double dx)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(dx / 2, 0));
        window.MouseMove(point + new Vector(dx, 0));
        window.MouseUp(point + new Vector(dx, 0), MouseButton.Left);
        Flush();
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

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("SCRISELF_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        Flush();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }
}
