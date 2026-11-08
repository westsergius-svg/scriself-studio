namespace ScriSelfStudio.UiSmokeTests;

using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRichEditor.Controls;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Presentation.Theme;
using ScriSelfStudio.TestSupport;

public sealed class SceneVersionsTests
{
    [AvaloniaFact]
    public async Task Rich_editor_preserves_undo_on_viewing_and_starts_fresh_after_restore()
    {
        var dialog = new DialogStub();
        using var context = await SceneContext.CreateAsync(dialog);
        var projects = context.Provider.GetRequiredService<IProjectService>();
        var model = context.Provider.GetRequiredService<EditorViewModel>();
        model.Text = "Первый вариант";
        await model.FlushAsync();
        var scene = projects.GetSceneDocument(context.SceneId).Require();
        var version = projects.CreateSceneVersion(context.SceneId, scene.Revision, "Начало").Require();
        var view = new EditorView { DataContext = model };
        var window = new Window { Content = view, Width = 1100, Height = 760 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var editor = view.GetVisualDescendants().OfType<RichEditor>().Single();
            editor.FocusDocumentEnd();
            window.KeyTextInput(" после правки");
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.CanUndo);
            await model.OpenVersionsCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.CanUndo);
            editor.Undo();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("после правки", editor.GetPlainText(), StringComparison.Ordinal);
            editor.Redo();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("после правки", editor.GetPlainText(), StringComparison.Ordinal);
            dialog.Action = id =>
            {
                Assert.True(projects.RestoreSceneVersion(id, version.Id, projects.GetSceneDocument(id).Require().Revision).IsSuccess);
                return Task.CompletedTask;
            };
            await model.OpenVersionsCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Первый вариант", editor.GetPlainText(), StringComparison.Ordinal);
            Assert.DoesNotContain("после правки", editor.GetPlainText(), StringComparison.Ordinal);
            Assert.False(editor.CanUndo);
            Assert.False(editor.CanRedo);
            editor.FocusDocumentEnd();
            window.KeyTextInput(" новая правка");
            editor.Undo();
            Dispatcher.UIThread.RunJobs();
            await model.FlushAsync();
            Assert.Equal("Первый вариант", projects.GetSceneDocument(context.SceneId).Require().Document.ToPlainText());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Failed_save_keeps_version_in_memory_and_retry_does_not_duplicate_it()
    {
        using var workspace = new TempWorkspace();
        var project = SampleProjects.CreateBookWithScenes();
        var store = new InMemoryProjectStore();
        var path = workspace.FilePath("Ошибка записи.scri");
        await store.SaveAsync(new() { Project = project, FilePath = path, CreateBackup = false });
        var service = new ScriSelfStudio.Persistence.Projects.ProjectService(store,
            new ScriSelfStudio.Persistence.FileSystem.FileProjectRecoveryStore(), new ScriSelfStudio.Core.Events.EventBus());
        Assert.True((await service.OpenAsync(path)).IsSuccess);
        using var model = new SceneVersionsViewModel(service, project.EnumerateScenes().First().Id);
        model.NewVersionName = "Важный вариант";
        store.FailOnSave = true;
        await model.CreateCommand.ExecuteAsync(null);
        Assert.True(model.HasUnsavedChanges);
        Assert.False(model.CanChange);
        Assert.Single(model.Versions);
        Assert.Contains("только в памяти", model.Status, StringComparison.Ordinal);
        store.FailOnSave = false;
        await model.SaveChangesCommand.ExecuteAsync(null);
        Assert.False(model.HasUnsavedChanges);
        var saved = (await store.LoadAsync(path)).Require().Project.EnumerateScenes().First();
        Assert.Single(saved.Versions);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Studio)]
    [InlineData(AppTheme.Starter)]
    [InlineData(AppTheme.GreenStart)]
    [InlineData(AppTheme.Cabinet)]
    [InlineData(AppTheme.Jam)]
    public async Task History_window_creates_compares_and_restores_in_all_themes(AppTheme theme)
    {
        using var context = await SceneContext.CreateAsync();
        context.Provider.GetRequiredService<IThemeService>().Apply(theme);
        var projects = context.Provider.GetRequiredService<IProjectService>();
        var current = projects.GetSceneDocument(context.SceneId).Require();
        await projects.UpdateSceneDocumentAsync(new() { SceneId = context.SceneId, BaseRevision = current.Revision,
            Document = SampleProjects.DocumentWithText("Она ждала у окна.\nЗа стеклом шёл дождь.", TextFormat.Italic) });
        using var model = new SceneVersionsViewModel(projects, context.SceneId);
        var window = new SceneVersionsWindow { DataContext = model, Width = 760, Height = 540 };
        window.Show();
        try
        {
            window.FindControl<TextBox>("VersionNameBox")!.Text = "Первый вариант";
            Click(window, "CreateVersionButton");
            await model.CreateCommand.ExecutionTask!;
            await model.PendingComparison;
            var saved = Assert.Single(model.Versions);
            Assert.False(model.HasUnsavedChanges);
            Assert.Equal("Текст и оформление совпадают.", model.ComparisonSummary);
            Assert.True((await projects.UpdateSceneDocumentAsync(new() { SceneId = context.SceneId,
                BaseRevision = projects.GetSceneDocument(context.SceneId).Require().Revision,
                Document = SampleProjects.DocumentWithText("Она стояла у окна.\nЗа стеклом уже светало.") })).IsSuccess);

            using var comparison = new SceneVersionsViewModel(projects, context.SceneId);
            window.DataContext = comparison;
            await comparison.PendingComparison;
            Assert.Contains(comparison.Difference.Parts, p => p.Kind == SceneDiffKind.Removed && p.Text.Contains("ждала", StringComparison.Ordinal));
            Assert.Contains(comparison.Difference.Parts, p => p.Kind == SceneDiffKind.Added && p.Text.Contains("стояла", StringComparison.Ordinal));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var renderDir = Environment.GetEnvironmentVariable("SCRISELF_RENDER_DIR");
            if (theme == AppTheme.Studio && !string.IsNullOrEmpty(renderDir))
            {
                Directory.CreateDirectory(renderDir);
                using var bitmap = window.CaptureRenderedFrame();
                Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(renderDir, "scene-versions.png"), PngBitmapEncoderOptions.Default);
                Assert.True((await projects.SaveAsAsync(Path.Combine(renderDir, "scene-versions-demo.scri"))).IsSuccess);
            }
            Click(window, "RestoreVersionButton");
            Assert.True(comparison.ShowConfirmation);
            Click(window, "ConfirmVersionActionButton");
            await comparison.ConfirmCommand.ExecutionTask!;
            await comparison.PendingComparison;
            Assert.Contains("восстановлена", comparison.Status, StringComparison.Ordinal);
            Assert.Equal("Текст и оформление совпадают.", comparison.ComparisonSummary);
            Assert.Equal(2, comparison.Versions.Count);
            var restored = projects.GetSceneDocument(context.SceneId).Require().Document;
            Assert.Contains("ждала", restored.ToPlainText(), StringComparison.Ordinal);
            Assert.Equal(TextFormat.Italic, restored.Blocks[0].Spans[0].Style.Flags);
            Assert.Equal(saved.Info.Id, comparison.SelectedVersion!.Info.Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Editor_keeps_undo_on_viewing_and_reloads_restored_text_before_autosave()
    {
        var dialog = new DialogStub();
        using var context = await SceneContext.CreateAsync(dialog);
        var projects = context.Provider.GetRequiredService<IProjectService>();
        var editor = context.Provider.GetRequiredService<EditorViewModel>();
        editor.Text = "Сохранённый текст";
        await editor.FlushAsync();
        var original = projects.GetSceneDocument(context.SceneId).Require();
        var version = projects.CreateSceneVersion(context.SceneId, original.Revision, "До переписывания").Require();
        context.Engine.ReplaceRange(new(new(0), new(context.Engine.PlainText.Length)), "Новый рабочий текст");
        Assert.True(editor.CanUndo);
        await editor.OpenVersionsCommand.ExecuteAsync(null);
        Assert.True(editor.CanUndo);
        dialog.Action = id =>
        {
            Assert.Equal(context.SceneId, id);
            var current = projects.GetSceneDocument(id).Require();
            Assert.Equal("Новый рабочий текст", current.Document.ToPlainText());
            Assert.True(projects.RestoreSceneVersion(id, version.Id, current.Revision).IsSuccess);
            return Task.CompletedTask;
        };
        await editor.OpenVersionsCommand.ExecuteAsync(null);
        Assert.Equal("Сохранённый текст", editor.Text);
        await editor.FlushAsync();
        Assert.Equal("Сохранённый текст", projects.GetSceneDocument(context.SceneId).Require().Document.ToPlainText());
        var safety = projects.GetSceneVersions(context.SceneId).Require().Single(v => v.IsSafetySnapshot);
        Assert.Equal("Новый рабочий текст", projects.GetSceneVersion(context.SceneId, safety.Id).Require().Document.ToPlainText());
    }

    private static void Click(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var button = window.FindControl<Button>(name)!;
        var point = button.TranslatePoint(new Avalonia.Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class DialogStub : ISceneVersionsDialogService
    {
        public Func<Guid, Task>? Action { get; set; }
        public Task ShowAsync(Guid sceneId) => Action?.Invoke(sceneId) ?? Task.CompletedTask;
    }
}
