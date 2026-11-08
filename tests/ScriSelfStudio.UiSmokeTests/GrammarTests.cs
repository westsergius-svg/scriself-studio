namespace ScriSelfStudio.UiSmokeTests;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation.Theme;
using ScriSelfStudio.TestSupport;

public sealed class GrammarTests
{
    [AvaloniaFact]
    public async Task Missing_punctuation_inserts_at_zero_length_range_and_undo_restores_text()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var preferences = provider.GetRequiredService<IAppSettingsService>();
        await preferences.UpdateAsync(p => p.GrammarCheckEnabled = true);
        var service = new StubGrammar { Response = _ => Task.FromResult(Result<IReadOnlyList<GrammarIssue>>.Success(
            [new(5, 0, "", "Пропущена запятая", "TEST_COMMA", [","])])) };
        var editor = new RichEditor { Document = Flow("Hello world") };
        using var behavior = new GrammarBehavior(editor, new GrammarViewModel(service, preferences));
        var window = new Window { Content = editor };
        window.Show();
        try
        {
            await behavior.CheckNowAsync();
            Assert.True(behavior.Apply(Assert.Single(behavior.CurrentIssues), ","));
            Assert.Equal("Hello, world", editor.GetPlainText());
            Assert.All(editor.Document!.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()), r => Assert.Equal(FontStyle.Italic, r.FontStyle));
            editor.Undo();
            Assert.Equal("Hello world", editor.GetPlainText());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Correction_preserves_formatting_review_and_native_undo_and_saves()
    {
        using var context = await SceneContext.CreateAsync();
        var preferences = context.Provider.GetRequiredService<IAppSettingsService>();
        await preferences.UpdateAsync(p => p.GrammarCheckEnabled = true);
        var original = SampleProjects.DocumentWithText("She go home.", TextFormat.Italic);
        original.Review.TrackChanges = true;
        var editor = new RichEditor { Document = RichDocumentAdapter.ToFlowDocument(original, 16) };
        var model = new GrammarViewModel(new StubGrammar(), preferences);
        using var behavior = new GrammarBehavior(editor, model);
        var window = new Window { Content = editor, Width = 700, Height = 400 };
        window.Show();
        try
        {
            await behavior.CheckNowAsync();
            var annotation = Assert.Single(behavior.CurrentIssues);
            Assert.Empty(RichDocumentAdapter.FromFlowDocument(editor.Document!, 16).Review.Changes);
            Assert.True(behavior.Apply(annotation, "goes")); Pump();
            var changed = RichDocumentAdapter.FromFlowDocument(editor.Document!, 16);
            Assert.Equal("She goes home.", changed.ToPlainText());
            Assert.All(changed.Blocks.SelectMany(b => b.Spans), s => Assert.Equal(TextFormat.Italic, s.Style.Flags));
            Assert.Single(changed.Review.Changes);
            editor.Undo(); Pump();
            Assert.Equal("She go home.", editor.GetPlainText());
            Assert.Empty(RichDocumentAdapter.FromFlowDocument(editor.Document!, 16).Review.Changes);
            editor.Redo(); Pump();
            var projects = context.Provider.GetRequiredService<IProjectService>();
            var current = projects.GetSceneDocument(context.SceneId).Require();
            Assert.True((await projects.UpdateSceneDocumentAsync(new() { SceneId = context.SceneId, BaseRevision = current.Revision,
                Document = RichDocumentAdapter.FromFlowDocument(editor.Document!, 16) })).IsSuccess);
            Assert.True((await projects.SaveAsync()).IsSuccess);
            var path = projects.CurrentFilePath!;
            await projects.CloseAsync(false);
            Assert.True((await projects.OpenAsync(path)).IsSuccess);
            Assert.Equal("She goes home.", projects.GetSceneDocument(context.SceneId).Require().Document.ToPlainText());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Out_of_order_response_and_old_menu_action_cannot_change_new_text()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var preferences = provider.GetRequiredService<IAppSettingsService>();
        await preferences.UpdateAsync(p => p.GrammarCheckEnabled = true);
        var delayed = new TaskCompletionSource<Result<IReadOnlyList<GrammarIssue>>>();
        var service = new StubGrammar { Response = _ => delayed.Task };
        var model = new GrammarViewModel(service, preferences);
        var editor = new RichEditor { Document = Flow("She go home.") };
        using var behavior = new GrammarBehavior(editor, model);
        var window = new Window { Content = editor };
        window.Show();
        try
        {
            var first = behavior.CheckNowAsync();
            editor.Document = Flow("New text."); Pump();
            service.Response = _ => Task.FromResult(Result<IReadOnlyList<GrammarIssue>>.Success([]));
            await behavior.CheckNowAsync();
            delayed.SetResult(Result<IReadOnlyList<GrammarIssue>>.Success([Issue()]));
            await first;
            Assert.Empty(behavior.CurrentIssues);
            editor.Document = Flow("She go home."); Pump();
            service.Response = null;
            await behavior.CheckNowAsync();
            var old = Assert.Single(behavior.CurrentIssues);
            editor.Document = Flow("A different scene.");
            Assert.False(behavior.Apply(old, "goes"));
            Assert.Equal("A different scene.", editor.GetPlainText());
            service.Response = _ => Task.FromResult(Result<IReadOnlyList<GrammarIssue>>.FromError(ErrorCode.EditorOperationFailed, "Проверка недоступна"));
            await behavior.CheckNowAsync();
            Assert.Equal("Проверка недоступна", model.Status);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Disabled_check_does_not_start_service_and_preferences_survive_reload()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root, new StubFileDialogService());
        var service = new StubGrammar();
        var preferences = provider.GetRequiredService<IAppSettingsService>();
        var model = new GrammarViewModel(service, preferences);
        var editor = new RichEditor { Document = Flow("She go home.") };
        using var behavior = new GrammarBehavior(editor, model);
        await behavior.CheckNowAsync();
        Assert.Equal(0, service.Calls);
        await provider.GetRequiredService<LauncherViewModel>().InitializeAsync();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Load();
        settings.GrammarCheckEnabled = true;
        await settings.PendingApply!;
        settings.SelectedGrammarLanguage = settings.GrammarLanguages.Single(l => l.Value == "en-US");
        await settings.PendingApply!;
        await preferences.LoadAsync();
        Assert.True(preferences.Current.GrammarCheckEnabled);
        Assert.Equal("en-US", preferences.Current.GrammarLanguage);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Studio)]
    [InlineData(AppTheme.Starter)]
    [InlineData(AppTheme.GreenStart)]
    [InlineData(AppTheme.Cabinet)]
    [InlineData(AppTheme.Jam)]
    public async Task Grammar_controls_and_status_are_visible_in_each_theme(AppTheme theme)
    {
        using var context = await SceneContext.CreateAsync();
        context.Provider.GetRequiredService<IThemeService>().Apply(theme);
        var model = context.Provider.GetRequiredService<EditorViewModel>();
        model.Text = "She goes home.";
        model.Grammar.IsEnabled = true;
        var view = new EditorView { DataContext = model };
        var window = new Window { Content = view, Width = 1100, Height = 760 };
        window.Show();
        try
        {
            Pump();
            var toggle = view.FindControl<CheckBox>("GrammarCheckToggle")!;
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.True(toggle.IsChecked);
            Assert.True(toggle.Bounds.Width > 50);
            Assert.Contains(view.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>(), t => t.Text == model.Grammar.Status && t.IsEffectivelyVisible);
        }
        finally { model.Grammar.IsEnabled = false; window.Close(); }
    }

    private static FlowDocument Flow(string text) => new() { Blocks = { new Paragraph { Inlines = { new Run { Text = text, FontStyle = FontStyle.Italic } } } } };
    private static GrammarIssue Issue() => new(4, 2, "go", "Проверьте согласование", "TEST_AGREEMENT", ["goes"]);
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }

    private sealed class StubGrammar : IGrammarService
    {
        public int Calls { get; private set; }
        public Func<string, Task<Result<IReadOnlyList<GrammarIssue>>>>? Response { get; set; }
        public Task<Result<IReadOnlyList<GrammarIssue>>> CheckAsync(string text, string language, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Response?.Invoke(text) ?? Task.FromResult(Result<IReadOnlyList<GrammarIssue>>.Success(text == "She go home." ? [Issue()] : []));
        }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
