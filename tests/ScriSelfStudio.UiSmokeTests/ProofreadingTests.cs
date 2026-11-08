namespace ScriSelfStudio.UiSmokeTests;

using Avalonia.Controls;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Projects;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.TestSupport;

public sealed class ProofreadingTests
{
    [AvaloniaFact]
    public async Task Right_click_offers_real_correction_and_synonym_and_preserves_word_style()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var paragraph = new Paragraph
        {
            Inlines =
            {
                new Run { Text = "Он сказал: " },
                new Run { Text = "превет", FontWeight = FontWeight.Bold, FontStyle = FontStyle.Italic, FontSize = 18 },
                new Run { Text = "." },
            },
        };
        var editor = new RichEditor { Document = new FlowDocument { Blocks = { paragraph } } };
        var window = new Window { Content = editor, Width = 700, Height = 400 };
        using var behavior = new ProofreadingBehavior(editor, provider.GetRequiredService<TextToolsViewModel>());
        ContextMenu? menu = null;
        editor.TextContextMenuOpening += (_, args) => menu = args.Menu;
        window.Show();
        editor.FocusDocumentEnd();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            // The first line starts at the control origin in continuous mode.
            window.MouseDown(new Point(103, 10), MouseButton.Right);
            window.MouseUp(new Point(103, 10), MouseButton.Right);
            Assert.NotNull(menu);
            var corrections = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Исправления");
            for (var i = 0; i < 100 && corrections.Items.OfType<MenuItem>().Any(item => item.Header as string == "Ищем варианты…"); i++)
                await Task.Delay(20);
            var correction = corrections.Items.OfType<MenuItem>().Single(item => item.Header as string == "привет");
            correction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Close();
            Assert.Equal("Он сказал: привет.", editor.GetPlainText());
            var corrected = paragraph.Inlines.OfType<Run>().Single(run => run.Text == "привет");
            Assert.Equal(FontWeight.Bold, corrected.FontWeight);
            Assert.Equal(FontStyle.Italic, corrected.FontStyle);
            Assert.Equal(18, corrected.FontSize);
            editor.Undo();
            Assert.Equal("Он сказал: превет.", editor.GetPlainText());

            editor.Document = new FlowDocument { Blocks = { new Paragraph { Inlines = { new Run { Text = "автомобиль" } } } } };
            editor.FocusDocumentEnd();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.MouseDown(new Point(15, 8), MouseButton.Right);
            window.MouseUp(new Point(15, 8), MouseButton.Right);
            var synonyms = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Синонимы · wiki-ru-wordnet");
            for (var i = 0; i < 100 && synonyms.Items.OfType<MenuItem>().Any(item => item.Header as string == "Ищем синонимы…"); i++)
                await Task.Delay(20);
            var synonym = synonyms.Items.OfType<MenuItem>().Single(item => item.Header as string == "машина");
            synonym.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("машина", editor.GetPlainText());
        }
        finally { menu?.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Real_editor_checks_loaded_scene_and_produces_preview()
    {
        using var context = await SceneContext.CreateAsync();
        var provider = context.Provider;
        var model = provider.GetRequiredService<EditorViewModel>();
        model.Text = "В комнате было тихо.\n\nОн сказал: превет.\n\nThe writer typed worlld.\n\nНайдём синоним для слова автомобиль.";
        var window = provider.GetRequiredService<MainWindow>();
        window.Width = 1200;
        window.Height = 800;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1300);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("— 2.", model.TextTools.Status, StringComparison.Ordinal);
            var rich = window.GetVisualDescendants().OfType<RichEditor>().Single();
            Assert.Contains("превет", rich.GetPlainText());
            var path = Environment.GetEnvironmentVariable("SCRISELF_RENDER_DIR");
            if (!string.IsNullOrWhiteSpace(path))
            {
                Directory.CreateDirectory(path);
                var saved = await provider.GetRequiredService<IProjectService>().SaveAsAsync(Path.Combine(path, "text-tools-demo.scri"));
                Assert.True(saved.IsSuccess);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = window.CaptureRenderedFrame();
                Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(path, "proofreading-editor.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Annotations_do_not_change_document_and_correction_is_undoable()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var editor = new RichEditor { Document = new FlowDocument { Blocks = { new Paragraph() } } };
        var window = new Window { Content = editor, Width = 700, Height = 400 };
        window.Show();
        editor.FocusDocumentEnd();
        editor.InsertText("превет и worlld");
        Dispatcher.UIThread.RunJobs();
        using var behavior = new ProofreadingBehavior(editor, provider.GetRequiredService<TextToolsViewModel>());
        try
        {
            var original = editor.ToJson();
            Assert.Equal("превет и worlld", editor.GetPlainText());
            await behavior.CheckNowAsync();
            Assert.True(behavior.CurrentSpans.Count == 2, provider.GetRequiredService<TextToolsViewModel>().Status);
            Assert.Equal(original, editor.ToJson());
            var first = behavior.CurrentSpans[0];
            Assert.True(editor.ReplaceProofreadingWord(first, "привет"));
            Dispatcher.UIThread.RunJobs();
            await behavior.CheckNowAsync();
            Assert.Single(behavior.CurrentSpans);
            Assert.Contains("привет", editor.GetPlainText());
            editor.Undo();
            Dispatcher.UIThread.RunJobs();
            await behavior.CheckNowAsync();
            Assert.Equal(2, behavior.CurrentSpans.Count);
            Assert.Contains("превет", editor.GetPlainText());
            Assert.False(editor.ReplaceProofreadingWord(first, "не должно замениться"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Underline_waits_for_word_boundary_and_obeys_toggle_and_dictionary()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var model = provider.GetRequiredService<TextToolsViewModel>();
        var editor = new RichEditor { Document = new FlowDocument { Blocks = { new Paragraph() } } };
        var window = new Window { Content = editor, Width = 700, Height = 400 };
        window.Show();
        editor.FocusDocumentEnd();
        using var behavior = new ProofreadingBehavior(editor, model);
        try
        {
            window.KeyTextInput("превет");
            Dispatcher.UIThread.RunJobs();
            await behavior.CheckNowAsync();
            Assert.Empty(behavior.CurrentSpans);
            window.KeyTextInput(" ");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("превет ", editor.GetPlainText());
            await behavior.CheckNowAsync();
            Assert.True(behavior.CurrentSpans.Count == 1, model.Status);
            model.IsEnabled = false;
            Assert.Empty(behavior.CurrentSpans);
            model.IsEnabled = true;
            await behavior.CheckNowAsync();
            Assert.Single(behavior.CurrentSpans);
            await model.SetPersonalWordAsync("превет", true);
            await behavior.CheckNowAsync();
            Assert.Empty(behavior.CurrentSpans);
            await model.SetPersonalWordAsync("превет", false);
            await behavior.CheckNowAsync();
            Assert.Single(behavior.CurrentSpans);
        }
        finally { window.Close(); }
    }
}
