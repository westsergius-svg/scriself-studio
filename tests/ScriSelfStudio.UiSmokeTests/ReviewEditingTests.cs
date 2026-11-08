namespace ScriSelfStudio.UiSmokeTests;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Presentation.Theme;

public sealed class ReviewEditingTests
{
    [AvaloniaFact]
    public async Task Automatic_capitalization_is_tracked_and_undo_keeps_the_lowercase_change()
    {
        using var context = await SceneContext.CreateAsync();
        var model = context.Provider.GetRequiredService<EditorViewModel>();
        await context.Provider.GetRequiredService<ScriSelfStudio.Contracts.Settings.IAppSettingsService>()
            .UpdateAsync(p => p.AutoCapitalizeSentences = true);
        model.Text = "Конец. ";
        var view = new EditorView { DataContext = model };
        var window = new Window { Content = view, Width = 1100, Height = 760 };
        window.Show();
        try
        {
            Pump();
            model.ReviewPanel.ToggleTrackingCommand.Execute(null);
            var editor = view.GetVisualDescendants().OfType<RichEditor>().Single();
            editor.FocusDocumentEnd();
            foreach (var c in "слово") window.KeyTextInput(c.ToString());
            window.KeyTextInput(" "); Pump();
            Assert.EndsWith("Конец. Слово ", editor.GetPlainText());
            Assert.Equal("Слово ", Assert.Single(model.ReviewPanel.Changes).Added);
            editor.Undo(); Pump();
            Assert.EndsWith("Конец. слово", editor.GetPlainText());
            Assert.Equal("слово", Assert.Single(model.ReviewPanel.Changes).Added);
            model.ReviewPanel.SelectedChange = model.ReviewPanel.Changes[0];
            model.ReviewPanel.RejectChangeCommand.Execute(null); Pump();
            Assert.EndsWith("Конец. ", editor.GetPlainText());
            Assert.Empty(model.ReviewPanel.Changes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Ribbon_keeps_each_scenes_review_and_tracks_committed_ime_and_spelling_replacement()
    {
        using var context = await SceneContext.CreateAsync();
        var projects = context.Provider.GetRequiredService<IProjectService>();
        var architecture = context.Provider.GetRequiredService<IBookArchitectureService>();
        var model = context.Provider.GetRequiredService<EditorViewModel>();
        model.Text = "Первая сцена";
        await model.FlushAsync();
        var chapter = projects.Current!.Series[0].Books[0].Chapters[0];
        await architecture.CreateSceneAsync(new() { ChapterId = chapter.Id, Title = "Вторая" });
        var secondId = projects.Current!.Series[0].Books[0].Chapters[0].Scenes.Last().Id;
        await projects.UpdateSceneDocumentAsync(new() { SceneId = secondId, BaseRevision = 0, Document = ScriSelfStudio.TestSupport.SampleProjects.DocumentWithText("превет") });
        await model.OpenSceneFromBookAsync(secondId);
        var view = new EditorView { DataContext = model };
        var window = new Window { Content = view, Width = 1100, Height = 850 };
        window.Show();
        try
        {
            Pump();
            var editor = view.GetVisualDescendants().OfType<RichEditor>().Single();
            var paragraph = editor.Document!.Blocks.OfType<Paragraph>().Last();
            editor.SelectHostRange(paragraph, 0, paragraph, 6);
            model.ReviewPanel.Draft = "Вторая сцена";
            model.ReviewPanel.AddCommentCommand.Execute(null);
            model.ReviewPanel.ToggleTrackingCommand.Execute(null);
            paragraph = editor.Document!.Blocks.OfType<Paragraph>().Last();
            Assert.True(editor.ReplaceProofreadingWord(new(paragraph, 0, "превет"), "привет")); Pump();
            editor.FocusDocumentEnd();
            window.KeyTextInput(" 你好😀"); Pump();
            await model.FlushAsync();
            var first = projects.GetSceneDocument(context.SceneId).Require().Document;
            var second = projects.GetSceneDocument(secondId).Require().Document;
            Assert.Empty(first.Review.Comments);
            Assert.Empty(first.Review.Changes);
            Assert.Single(second.Review.Comments);
            Assert.NotEmpty(second.Review.Changes);
            Assert.Contains("привет 你好😀", second.ToPlainText(), StringComparison.Ordinal);
            Assert.Equal("Первая сцена", first.ToPlainText());
            await architecture.MoveEntityAsync(new() { Kind = EntityKind.Scene, Id = secondId, NewOrder = 0 });
            Assert.Single(projects.GetSceneDocument(secondId).Require().Document.Review.Comments);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Comment_and_tracking_survive_native_undo_redo_fast_input_and_save()
    {
        using var context = await SceneContext.CreateAsync();
        var model = context.Provider.GetRequiredService<EditorViewModel>();
        model.Text = "Начало сцены";
        await model.FlushAsync();
        var view = new EditorView { DataContext = model };
        var window = new Window { Content = view, Width = 1100, Height = 850 };
        window.Show();
        try
        {
            Pump();
            var editor = view.GetVisualDescendants().OfType<RichEditor>().Single();
            var paragraph = editor.Document!.Blocks.OfType<Paragraph>().Last();
            var originalId = paragraph.StableId;
            editor.SelectHostRange(paragraph, 0, paragraph, 6);
            model.ReviewPanel.Draft = "Уточнить начало";
            model.ReviewPanel.AddCommentCommand.Execute(null);
            Assert.Single(model.ReviewPanel.Comments);
            editor.Undo(); Pump();
            Assert.Empty(model.ReviewPanel.Comments);
            editor.Redo(); Pump();
            Assert.Single(model.ReviewPanel.Comments);
            model.ReviewPanel.ToggleTrackingCommand.Execute(null);
            editor.FocusDocumentEnd();
            window.KeyTextInput(" и продолжение");
            // Undo before the deferred TextChanged event: metadata must be included in the snapshot.
            editor.Undo(); Pump();
            Assert.Empty(model.ReviewPanel.Changes);
            editor.Redo(); Pump();
            Assert.Single(model.ReviewPanel.Changes);
            Assert.Equal(originalId, editor.Document!.Blocks.OfType<Paragraph>().Last().StableId);
            await model.FlushAsync();
            var projects = context.Provider.GetRequiredService<IProjectService>();
            var path = projects.CurrentFilePath!;
            var saved = projects.GetSceneDocument(context.SceneId).Require().Document;
            Assert.Single(saved.Review.Comments);
            Assert.Single(saved.Review.Changes);
            model.ReviewPanel.SelectedChange = model.ReviewPanel.Changes[0];
            model.ReviewPanel.RejectChangeCommand.Execute(null); Pump();
            Assert.DoesNotContain("продолжение", editor.GetPlainText(), StringComparison.Ordinal);
            editor.Undo(); Pump();
            Assert.Contains("продолжение", editor.GetPlainText(), StringComparison.Ordinal);
            await model.FlushAsync();
            await projects.CloseAsync(false);
            Assert.True((await projects.OpenAsync(path)).IsSuccess);
            var reopened = projects.GetSceneDocument(context.SceneId).Require().Document;
            Assert.Single(reopened.Review.Comments);
            Assert.Single(reopened.Review.Changes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Paragraph_split_and_undo_preserve_comment_and_original_identity()
    {
        using var context = await SceneContext.CreateAsync();
        var model = context.Provider.GetRequiredService<EditorViewModel>();
        model.Text = "Первая вторая";
        var view = new EditorView { DataContext = model };
        var window = new Window { Content = view, Width = 1100, Height = 760 };
        window.Show();
        try
        {
            Pump();
            var editor = view.GetVisualDescendants().OfType<RichEditor>().Single();
            var paragraph = editor.Document!.Blocks.OfType<Paragraph>().Last();
            var id = paragraph.StableId;
            editor.SelectHostRange(paragraph, 0, paragraph, 13);
            model.ReviewPanel.Draft = "Весь фрагмент";
            model.ReviewPanel.AddCommentCommand.Execute(null); Pump();
            paragraph = editor.Document!.Blocks.OfType<Paragraph>().Last();
            editor.SelectHostRange(paragraph, 7, paragraph, 7);
            editor.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Pump();
            var parts = editor.Document!.Blocks.OfType<Paragraph>().Where(p => !RichEditor.GetParagraphText(p).StartsWith('\u200B')).ToArray();
            Assert.Equal(2, parts.Length);
            Assert.Equal(id, parts[0].StableId);
            Assert.NotEqual(id, parts[1].StableId);
            Assert.False(Assert.Single(model.ReviewPanel.Comments).Comment.IsOrphaned);
            editor.Undo(); Pump();
            Assert.Equal(13, Assert.Single(model.ReviewPanel.Comments).Comment.Length);
            Assert.Equal(id, editor.Document!.Blocks.OfType<Paragraph>().Last().StableId);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Studio)]
    [InlineData(AppTheme.Starter)]
    [InlineData(AppTheme.GreenStart)]
    [InlineData(AppTheme.Cabinet)]
    [InlineData(AppTheme.Jam)]
    public async Task Review_panel_is_usable_in_each_theme(AppTheme theme)
    {
        using var context = await SceneContext.CreateAsync();
        context.Provider.GetRequiredService<IThemeService>().Apply(theme);
        var model = context.Provider.GetRequiredService<EditorViewModel>();
        model.Text = "Она ждала у окна.";
        model.ReviewPanel.IsOpen = true;
        var view = new EditorView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 760 };
        window.Show();
        try
        {
            Pump();
            var panel = view.GetVisualDescendants().OfType<ReviewPanelView>().Single();
            Assert.True(panel.IsEffectivelyVisible);
            Assert.True(panel.Bounds.Height >= 180);
            Assert.NotNull(panel.FindControl<Button>("TrackChangesButton")!.Command);
            model.ReviewPanel.ToggleTrackingCommand.Execute(null);
            var editor = view.GetVisualDescendants().OfType<RichEditor>().Single();
            editor.FocusDocumentEnd();
            window.KeyTextInput(" Шёл дождь."); Pump();
            Assert.Single(model.ReviewPanel.Changes);
            var directory = Environment.GetEnvironmentVariable("SCRISELF_RENDER_DIR");
            if (!string.IsNullOrEmpty(directory) && theme == AppTheme.Studio)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = window.CaptureRenderedFrame();
                Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(directory, "review-panel.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
}
