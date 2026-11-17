namespace ScriSelfStudio.UiSmokeTests;

using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Presentation.Export;
using ScriSelfStudio.TestSupport;

public sealed class ExportPreviewTests
{
    [AvaloniaFact]
    public async Task Visible_export_options_update_preview_immediately_and_match_saved_file()
    {
        using var context = await SceneContext.CreateAsync();
        var provider = context.Provider;
        var editor = provider.GetRequiredService<EditorViewModel>();
        editor.Text = "Текст рукописи без служебных названий.";
        await editor.SaveCommand.ExecuteAsync(null);
        var export = provider.GetRequiredService<ExportViewModel>();
        export.SelectedFormat = export.Formats.Single(format => format.Key == "DOCX");
        export.IncludeTitlePage = false;
        export.TableOfContents = false;
        provider.GetRequiredService<ShellViewModel>().OpenExportCommand.Execute(null);
        var window = provider.GetRequiredService<MainWindow>();
        window.Width = 1440;
        window.Height = 900;
        window.Show();
        Flush();
        try
        {
            var headings = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "IncludeSceneHeadingsChoice");
            Assert.False(export.IncludeSceneHeadings);
            Assert.DoesNotContain(VisibleText(window), text => text.StartsWith("Сцена:"));
            foreach (var _ in Enumerable.Range(0, 2))
            {
                Click(window, headings);
                Assert.True(export.IncludeSceneHeadings);
                Assert.Contains(VisibleText(window), text => text.StartsWith("Сцена:"));
                Click(window, headings);
                Assert.False(export.IncludeSceneHeadings);
                Assert.DoesNotContain(VisibleText(window), text => text.StartsWith("Сцена:"));
            }

            var dialogs = Assert.IsType<StubFileDialogService>(provider.GetRequiredService<IFileDialogService>());
            dialogs.ExportPath = context.Workspace.FilePath("Без названий.docx");
            await export.ExportCommand.ExecuteAsync(null);
            Assert.False(export.HasError, export.ErrorMessage);
            using (var archive = ZipFile.OpenRead(dialogs.ExportPath))
            using (var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open()))
            {
                var xml = await reader.ReadToEndAsync();
                Assert.DoesNotContain("Сцена:", xml);
                Assert.Contains(editor.Text, xml);
            }

            var titlePage = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "IncludeTitlePageChoice");
            Click(window, titlePage);
            Assert.True(export.IncludeTitlePage);
            Assert.Equal(export.ProjectTitle, export.PreviewBlocks[0].Text);
            Click(window, titlePage);
            Assert.StartsWith("Глава 1.", export.PreviewBlocks[0].Text);

            export.SelectedFont = "Consolas";
            export.SelectedFontSize = "14";
            export.SelectedLineSpacing = "2.0";
            Flush();
            var body = window.GetVisualDescendants().OfType<ExportPreviewText>().Single(text => text.Block?.Text == editor.Text);
            Assert.Equal("Consolas", body.FontFamily.Name);
            Assert.Equal(14d * 4 / 3, body.FontSize, 3);
            Assert.Equal(body.FontSize * 2, body.LineHeight, 3);

            export.SelectedFormat = export.Formats.Single(format => format.Key == "TXT");
            dialogs.ExportPath = context.Workspace.FilePath("Без названий.txt");
            await export.ExportCommand.ExecuteAsync(null);
            Assert.Equal(await File.ReadAllTextAsync(dialogs.ExportPath), Assert.Single(export.PreviewBlocks).Text);
        }
        finally { window.Close(); }
    }

    private static string[] VisibleText(Window window) => window.GetVisualDescendants().OfType<ExportPreviewText>()
        .Where(text => text.IsEffectivelyVisible).Select(text => text.Block!.Text).ToArray();

    private static void Click(Window window, Control control)
    {
        control.BringIntoView();
        Flush();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush();
    }

    private static void Flush() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
}
