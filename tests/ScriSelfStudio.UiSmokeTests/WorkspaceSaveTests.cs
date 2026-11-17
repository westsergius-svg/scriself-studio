namespace ScriSelfStudio.UiSmokeTests;

using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Updates;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Persistence.Sqlite;
using ScriSelfStudio.StoryUI;
using ScriSelfStudio.TestSupport;

public sealed class WorkspaceSaveTests
{
    [AvaloniaFact]
    public async Task Clean_workspace_does_not_require_another_save()
    {
        using var context = await Context.CreateAsync();
        Assert.False(context.Shell.HasPendingEdits);
        Assert.True((await context.Shell.FlushPendingEditsAsync()).IsSuccess);
    }

    [AvaloniaFact]
    public async Task Save_includes_editor_context_and_both_fields_of_the_architecture_panel()
    {
        using var context = await Context.CreateAsync();
        var editor = context.Provider.GetRequiredService<EditorViewModel>();
        editor.Text = "Changed manuscript";
        var panel = context.Provider.GetRequiredService<SceneContextViewModel>();
        panel.Summary = "Focused summary";
        panel.Notes = "Focused notes";
        var map = context.Provider.GetRequiredService<ArchitectureViewModel>();
        map.Refresh();
        map.Selected = Descendants(map.Roots).Single(node => node.Id == context.SecondScene);
        map.SceneDetails!.Description = "Architecture summary";
        map.SceneDetails.Notes = "Architecture notes";

        await context.Shell.SaveCommand.ExecuteAsync(null);
        Assert.False(context.Shell.HasPendingEdits);
        var disk = await context.ReadAsync();
        Assert.Equal("Changed manuscript", disk.FindScene(context.FirstScene)!.Document.ToPlainText());
        Assert.Equal("Focused summary", disk.FindScene(context.FirstScene)!.Description);
        Assert.Equal("Focused notes", disk.FindScene(context.FirstScene)!.Metadata.Notes);
        Assert.Equal("Architecture summary", disk.FindScene(context.SecondScene)!.Description);
        Assert.Equal("Architecture notes", disk.FindScene(context.SecondScene)!.Metadata.Notes);
    }

    [AvaloniaFact]
    public async Task Creating_another_project_saves_scene_and_story_drafts_first()
    {
        using var context = await Context.CreateAsync();
        context.Provider.GetRequiredService<SceneContextViewModel>().Summary = "Summary before switching";
        var characters = context.Provider.GetRequiredService<CharactersViewModel>();
        characters.Draft.Name = "New character";
        characters.Draft.Description = "Character draft";
        var lines = context.Provider.GetRequiredService<TimelineViewModel>();
        lines.Draft.Name = "New line";
        lines.Draft.Goal = "Line draft";
        context.Dialogs.CreatePath = context.Workspace.FilePath("second.scri");

        await context.Launcher.CreateProjectCommand.ExecuteAsync(null);
        Assert.False(context.Launcher.HasError, context.Launcher.ErrorMessage
            + $"; character draft: {characters.HasUnsavedDraft}; line draft: {lines.HasUnsavedDraft}");
        Assert.Equal(context.Dialogs.CreatePath, context.Projects.CurrentFilePath);
        var disk = await context.ReadAsync();
        Assert.Equal("Summary before switching", disk.FindScene(context.FirstScene)!.Description);
        Assert.Equal("Character draft", Assert.Single(disk.Characters).Description);
        Assert.Equal("Line draft", Assert.Single(disk.PlotLines).Goal);
    }

    [AvaloniaFact]
    public async Task Failed_save_cancels_project_switch_and_retry_preserves_the_draft()
    {
        var store = new ControlledStore();
        using var context = await Context.CreateAsync(store);
        context.Provider.GetRequiredService<SceneContextViewModel>().Summary = "Keep this draft";
        context.Dialogs.CreatePath = context.Workspace.FilePath("second.scri");
        store.FailWrites = true;
        await context.Launcher.CreateProjectCommand.ExecuteAsync(null);
        Assert.Equal(context.Path, context.Projects.CurrentFilePath);
        Assert.True(context.Shell.HasPendingEdits);
        Assert.NotEmpty(context.Dialogs.Errors);
        Assert.NotEqual("Keep this draft", (await context.ReadAsync()).FindScene(context.FirstScene)!.Description);

        store.FailWrites = false;
        await context.Launcher.CreateProjectCommand.ExecuteAsync(null);
        Assert.Equal(context.Dialogs.CreatePath, context.Projects.CurrentFilePath);
        Assert.Equal("Keep this draft", (await context.ReadAsync()).FindScene(context.FirstScene)!.Description);
    }

    [AvaloniaFact]
    public async Task Unnamed_character_draft_blocks_switch_instead_of_being_discarded()
    {
        using var context = await Context.CreateAsync();
        var characters = context.Provider.GetRequiredService<CharactersViewModel>();
        characters.Draft.Description = "An unnamed character's work";
        context.Dialogs.CreatePath = context.Workspace.FilePath("second.scri");
        await context.Launcher.CreateProjectCommand.ExecuteAsync(null);
        Assert.Equal(context.Path, context.Projects.CurrentFilePath);
        Assert.Equal("An unnamed character's work", characters.Draft.Description);
        Assert.True(context.Shell.HasPendingEdits);
    }

    [AvaloniaFact]
    public async Task Update_installation_saves_the_entire_workspace_before_launch()
    {
        var updater = new CapturingUpdater();
        using var context = await Context.CreateAsync(updater: updater);
        context.Provider.GetRequiredService<SceneContextViewModel>().Summary = "Summary before update";
        context.Projects.Mutate(project => { project.Description = "Project before update"; return Result.Success; });
        updater.HasPendingEdits = () => context.Shell.HasPendingEdits;
        await OfferUpdateAsync(context.Launcher);
        Assert.True(updater.Launched);
        Assert.False(updater.PendingAtLaunch);
        var disk = await context.ReadAsync();
        Assert.Equal("Project before update", disk.Description);
        Assert.Equal("Summary before update", disk.FindScene(context.FirstScene)!.Description);
    }

    [AvaloniaFact]
    public async Task Failed_save_prevents_installer_launch()
    {
        var store = new ControlledStore();
        var updater = new CapturingUpdater();
        using var context = await Context.CreateAsync(store, updater);
        context.Projects.Mutate(project => { project.Description = "Keep open"; return Result.Success; });
        store.FailWrites = true;
        await OfferUpdateAsync(context.Launcher);
        Assert.False(updater.Launched);
        Assert.True(context.Shell.HasPendingEdits);
        Assert.Equal("Keep open", context.Projects.Current!.Description);
    }

    [AvaloniaFact]
    public async Task Failed_installer_launch_keeps_the_current_book_open_and_its_drafts_saved()
    {
        var updater = new CapturingUpdater { FailLaunch = true };
        using var context = await Context.CreateAsync(updater: updater);
        updater.HasPendingEdits = () => context.Shell.HasPendingEdits;
        context.Provider.GetRequiredService<SceneContextViewModel>().Summary = "Safe after failed launch";
        await OfferUpdateAsync(context.Launcher);
        Assert.False(updater.Launched);
        Assert.False(updater.PendingAtLaunch);
        Assert.Equal(context.Path, context.Projects.CurrentFilePath);
        Assert.NotNull(context.Projects.Current);
        Assert.False(context.Shell.HasPendingEdits);
        Assert.Equal("Safe after failed launch", (await context.ReadAsync()).FindScene(context.FirstScene)!.Description);
        Assert.NotEmpty(context.Dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Closing_the_window_saves_dirty_project_metadata_without_editor_drafts()
    {
        using var context = await Context.CreateAsync();
        context.Projects.Mutate(project => { project.Description = "Save on close"; return Result.Success; });
        var window = context.Provider.GetRequiredService<MainWindow>();
        var app = PrepareApp(context.Provider);
        AttachClosingGuard(app, window);
        window.Show();
        window.Close();
        await WaitUntilAsync(() => !window.IsVisible);
        Assert.Equal("Save on close", (await context.ReadAsync()).Description);
    }

    [AvaloniaFact]
    public async Task Failed_window_close_stays_open_and_a_second_close_can_retry()
    {
        var store = new ControlledStore();
        using var context = await Context.CreateAsync(store);
        context.Projects.Mutate(project => { project.Description = "Draft after failure"; return Result.Success; });
        var window = context.Provider.GetRequiredService<MainWindow>();
        var app = PrepareApp(context.Provider);
        AttachClosingGuard(app, window);
        window.Show();
        store.FailWrites = true;
        window.Close();
        await WaitUntilAsync(() => !ShutdownInProgress(app));
        Assert.True(window.IsVisible);
        Assert.True(context.Shell.HasPendingEdits);
        Assert.NotEmpty(context.Dialogs.Errors);
        store.FailWrites = false;
        window.Close();
        await WaitUntilAsync(() => !window.IsVisible);
        Assert.Equal("Draft after failure", (await context.ReadAsync()).Description);
    }

    [AvaloniaFact]
    public async Task Unexpected_save_exception_does_not_complete_shutdown()
    {
        var store = new ControlledStore();
        using var context = await Context.CreateAsync(store);
        context.Projects.Mutate(project => { project.Description = "Keep after exception"; return Result.Success; });
        var app = PrepareApp(context.Provider);
        var desktop = DispatchProxy.Create<IClassicDesktopStyleApplicationLifetime, LifetimeProxy>();
        var proxy = (LifetimeProxy)(object)desktop;
        proxy.Window = context.Provider.GetRequiredService<MainWindow>();
        store.ThrowWrites = true;
        var args = new ShutdownRequestedEventArgs();
        typeof(ScriSelfStudio.App.App).GetMethod("OnShutdownRequested", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [desktop, args]);
        Assert.True(args.Cancel);
        await WaitUntilAsync(() => !ShutdownInProgress(app));
        Assert.Equal(0, proxy.Shutdowns);
        Assert.True(context.Shell.HasPendingEdits);
        Assert.NotEmpty(context.Dialogs.Errors);
        store.ThrowWrites = false;
        var retry = new ShutdownRequestedEventArgs();
        typeof(ScriSelfStudio.App.App).GetMethod("OnShutdownRequested", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [desktop, retry]);
        Assert.True(retry.Cancel);
        await WaitUntilAsync(() => proxy.Shutdowns == 1);
        Assert.False(context.Shell.HasPendingEdits);
        Assert.Equal("Keep after exception", (await context.ReadAsync()).Description);
    }

    private static ScriSelfStudio.App.App PrepareApp(ServiceProvider provider)
    {
        var app = new ScriSelfStudio.App.App();
        typeof(ScriSelfStudio.App.App).GetField("_provider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, provider);
        return app;
    }

    private static void AttachClosingGuard(ScriSelfStudio.App.App app, Window window) =>
        window.Closing += (EventHandler<WindowClosingEventArgs>)Delegate.CreateDelegate(typeof(EventHandler<WindowClosingEventArgs>),
            app, typeof(ScriSelfStudio.App.App).GetMethod("OnMainWindowClosing", BindingFlags.Instance | BindingFlags.NonPublic)!);

    private static bool ShutdownInProgress(ScriSelfStudio.App.App app) =>
        (bool)typeof(ScriSelfStudio.App.App).GetField("_shutdownInProgress", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate() && timer.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        Assert.True(predicate(), "The workspace operation did not finish.");
    }

    private static Task OfferUpdateAsync(LauncherViewModel launcher) =>
        (Task)typeof(LauncherViewModel).GetMethod("OfferUpdateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(launcher, [new UpdateInfo([1, 2, 1], "1.2.1", "test-setup.exe", "https://example.test/test-setup.exe", "", "manifest")
            { Sha256 = new string('0', 64), Signature = "test-proof" }, CancellationToken.None])!;

    private static IEnumerable<ArchitectureNodeViewModel> Descendants(IEnumerable<ArchitectureNodeViewModel> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));

    public class LifetimeProxy : DispatchProxy
    {
        public Window? Window { get; set; }
        public int Shutdowns { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "get_MainWindow") return Window;
            if (targetMethod.Name == "Shutdown") Shutdowns++;
            return targetMethod.ReturnType == typeof(bool) ? false : null;
        }
    }

    private sealed class ControlledStore : IProjectStore
    {
        private readonly SqliteProjectStore _inner = new();
        public bool FailWrites { get; set; }
        public bool ThrowWrites { get; set; }
        public Task<Result<StoreProbeResult>> ProbeAsync(string filePath, CancellationToken cancellationToken = default) => _inner.ProbeAsync(filePath, cancellationToken);
        public Task<Result<ProjectLoadResult>> LoadAsync(string filePath, CancellationToken cancellationToken = default) => _inner.LoadAsync(filePath, cancellationToken);
        public Task<Result> SaveAsync(ProjectSaveRequest request, CancellationToken cancellationToken = default) => ThrowWrites
            ? throw new IOException("Controlled write failure")
            : FailWrites ? Task.FromResult(Result.FromError(ErrorCode.StorageWriteFailed, "Controlled failed save"))
            : _inner.SaveAsync(request, cancellationToken);
    }

    private sealed class CapturingUpdater : IUpdateService
    {
        public string CurrentVersion => "1.2.0";
        public string ChannelUrl => "https://example.test/";
        public bool Launched { get; private set; }
        public bool PendingAtLaunch { get; private set; }
        public bool FailLaunch { get; set; }
        public Func<bool> HasPendingEdits { get; set; } = () => false;
        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) => Task.FromResult(UpdateCheckResult.Empty());
        public bool IsNewer(UpdateInfo info) => true;
        public Task<string> DownloadAsync(UpdateInfo latest, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult("never-executed-test-setup.exe");
        public void LaunchInstaller(string installerPath)
        {
            PendingAtLaunch = HasPendingEdits();
            if (FailLaunch) { throw new InvalidOperationException("Controlled process start failure"); }
            Launched = true;
        }
    }

    private sealed class Context : IDisposable
    {
        public required TempWorkspace Workspace { get; init; }
        public required ServiceProvider Provider { get; init; }
        public required StubFileDialogService Dialogs { get; init; }
        public required string Path { get; init; }
        public required Guid FirstScene { get; init; }
        public required Guid SecondScene { get; init; }
        public IProjectService Projects => Provider.GetRequiredService<IProjectService>();
        public ShellViewModel Shell => Provider.GetRequiredService<ShellViewModel>();
        public LauncherViewModel Launcher => Provider.GetRequiredService<LauncherViewModel>();
        public Task<Project> ReadAsync() => ReadCoreAsync(Path);

        public static async Task<Context> CreateAsync(IProjectStore? store = null, IUpdateService? updater = null)
        {
            var workspace = new TempWorkspace();
            var path = workspace.FilePath("first.scri");
            var dialogs = new StubFileDialogService { CreatePath = path, ConfirmResult = true };
            var provider = ServiceProviderFactory.Create(workspace.Root, dialogs, updater, store: store);
            var launcher = provider.GetRequiredService<LauncherViewModel>();
            await launcher.InitializeAsync();
            var projects = provider.GetRequiredService<IProjectService>();
            Assert.True((await projects.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Workspace" })).IsSuccess);
            Guid first = default, second = default;
            projects.Mutate(project =>
            {
                var book = ProjectFactory.CreateBook(project, project.Series[0], "Book");
                var chapter = ProjectFactory.CreateChapter(book, "Chapter");
                var scene = ProjectFactory.CreateScene(chapter, "First");
                scene.Document = SampleProjects.DocumentWithText("Original manuscript");
                first = scene.Id;
                chapter.Scenes.Add(scene);
                var other = ProjectFactory.CreateScene(chapter, "Second");
                second = other.Id;
                chapter.Scenes.Add(other);
                book.Chapters.Add(chapter);
                project.Series[0].Books.Add(book);
                return Result.Success;
            });
            Assert.True((await projects.SaveAsync()).IsSuccess);
            Assert.True((await provider.GetRequiredService<IEditorEngine>().OpenSceneAsync(first)).IsSuccess);
            provider.GetRequiredService<INavigationService>().NavigateTo(AppScreen.Editor);
            return new Context { Workspace = workspace, Provider = provider, Dialogs = dialogs, Path = path, FirstScene = first, SecondScene = second };
        }

        private static async Task<Project> ReadCoreAsync(string path) => (await new SqliteProjectStore().LoadAsync(path)).Require().Project;
        public void Dispose() { Provider.Dispose(); Workspace.Dispose(); }
    }
}
