namespace ScriSelfStudio.UiSmokeTests;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation.Help;
using ScriSelfStudio.TestSupport;

public sealed class FirstUseRegressionTests
{
    [Fact]
    public void Help_search_matches_word_prefixes_without_matching_inside_unrelated_words()
    {
        var scene = new HelpTopic("scene", "Сцена и её заголовок", "Книга", "", []);
        Assert.False(scene.Matches("цена"));
        Assert.True(scene.Matches("сцен"));
        Assert.True(scene.Matches("ЕЁ"));
        var help = new HelpViewModel { SearchQuery = "цена" };
        Assert.DoesNotContain(help.FilteredTopics, topic => topic.Id == "troubleshooting");
        help.SearchQuery = "заголовки сцен";
        Assert.Contains(help.FilteredTopics, topic => topic.Id == "export");
    }

    [AvaloniaFact]
    public async Task First_book_reveals_a_visible_clickable_chapter_action_without_reopening_the_view()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Первая.scri"), PromptResult = "Книга" };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        var window = provider.GetRequiredService<MainWindow>();
        window.Width = 1440;
        window.Height = 900;
        window.Show();
        try
        {
            await launcher.CreateProjectCommand.ExecuteAsync(null);
            Flush();
            var architecture = provider.GetRequiredService<ArchitectureViewModel>();
            await architecture.CreateBookCommand.ExecuteAsync(null);
            Flush();
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("addChapterColumn"));
            Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, 80), window)!.Value;
            var hit = window.InputHitTest(point) as Visual;
            Assert.True(ReferenceEquals(hit, button) || hit!.GetVisualAncestors().Contains(button));
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            if (architecture.CreateChapterInBookCommand.ExecutionTask is { } creation) await creation;
            Flush();
            Assert.Single(architecture.MapColumns);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Empty_scene_uses_project_font_for_first_input_and_preserves_undo()
    {
        using var context = await SceneContext.CreateAsync();
        var viewModel = context.Provider.GetRequiredService<EditorViewModel>();
        var window = context.Provider.GetRequiredService<MainWindow>();
        window.Show();
        try
        {
            Flush();
            var rich = window.GetVisualDescendants().OfType<RichEditor>().Single();
            var body = rich.Document!.Blocks.OfType<Paragraph>().Last();
            Assert.All(body.Inlines.OfType<Run>(), run => Assert.Equal(rich.DefaultFontSize, run.FontSize));
            rich.FocusDocumentEnd();
            window.KeyTextInput("Первое предложение.");
            Flush();
            Assert.Equal("Первое предложение.", viewModel.Text);
            Assert.Equal(rich.DefaultFontSize, rich.GetCaretFormat().FontSize);
            rich.Undo();
            Flush();
            Assert.Equal(string.Empty, viewModel.Text);
            rich.Redo();
            Flush();
            Assert.Equal("Первое предложение.", viewModel.Text);
            await viewModel.SaveCommand.ExecuteAsync(null);
            Assert.False(viewModel.IsDirty);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Compact_window_keeps_search_separate_and_context_actions_inside_the_viewport()
    {
        using var context = await SceneContext.CreateAsync();
        var provider = context.Provider;
        var window = provider.GetRequiredService<MainWindow>();
        window.Width = 1024;
        window.Height = 768;
        window.Show();
        try
        {
            Flush();
            var actions = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "SceneContextActions");
            AssertFullyVisible(actions, window);
            provider.GetRequiredService<ShellViewModel>().OpenLauncherCommand.Execute(null);
            Flush();
            var launcher = window.GetVisualDescendants().OfType<LauncherView>().Single();
            var search = launcher.FindControl<TextBox>("ProjectSearchBox")!;
            var files = launcher.FindControl<WrapPanel>("ProjectFileActions")!;
            AssertFullyVisible(search, window);
            var searchOrigin = search.TranslatePoint(default, window)!.Value;
            var filesOrigin = files.TranslatePoint(default, window)!.Value;
            Assert.True(searchOrigin.Y + search.Bounds.Height <= filesOrigin.Y);
            AssertFullyVisible(launcher.FindControl<StackPanel>("SelectedProjectActions")!, window);
            AssertFullyVisible(launcher.FindControl<Button>("LanguageSettingsButton")!, window);
        }
        finally { window.Close(); }
    }

    private static void AssertFullyVisible(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible);
        var origin = control.TranslatePoint(default, window)!.Value;
        Assert.True(origin.X >= 0 && origin.Y >= 0);
        Assert.True(origin.X + control.Bounds.Width <= window.Bounds.Width + 1);
        Assert.True(origin.Y + control.Bounds.Height <= window.Bounds.Height + 1);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
