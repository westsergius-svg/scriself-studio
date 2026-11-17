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
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Presentation.Export;
using ScriSelfStudio.Presentation.Theme;
using ScriSelfStudio.TestSupport;

public sealed class CompactExportTests
{
    [AvaloniaTheory]
    [InlineData(AppTheme.Studio)]
    [InlineData(AppTheme.Starter)]
    [InlineData(AppTheme.GreenStart)]
    [InlineData(AppTheme.Cabinet)]
    [InlineData(AppTheme.Jam)]
    public async Task Minimum_window_scrolls_to_markdown_exports_by_mouse_and_adapts_its_preview(AppTheme theme)
    {
        using var context = await SceneContext.CreateAsync();
        var provider = context.Provider;
        await provider.GetRequiredService<IAppSettingsService>().UpdateAsync(preferences => preferences.Theme = theme);
        provider.GetRequiredService<IThemeService>().Apply(theme);
        var editor = provider.GetRequiredService<EditorViewModel>();
        editor.Text = "Проверка компактного экспорта.";
        await editor.SaveCommand.ExecuteAsync(null);
        provider.GetRequiredService<ShellViewModel>().OpenExportCommand.Execute(null);
        var export = provider.GetRequiredService<ExportViewModel>();
        var window = provider.GetRequiredService<MainWindow>();
        window.Width = 800;
        window.Height = 480;
        window.Show();
        Flush();
        try
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ExportView>());
            var formats = view.FindControl<ListBox>("ExportFormats")!;
            var scroll = Assert.Single(formats.GetVisualDescendants().OfType<ScrollViewer>());
            Capture(window, theme + "-initial");
            Assert.InRange(formats.Bounds.Height, 50, window.Bounds.Height);
            Assert.True(scroll.Viewport.Height <= formats.Bounds.Height + 1);
            Assert.True(formats.ContainerFromIndex(formats.SelectedIndex)!.Focus());
            window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
            Flush();
            Assert.Equal("Markdown", export.SelectedFormat.Key);
            formats.SelectedIndex = 0;
            Flush();
            formats.ScrollIntoView(export.Formats[^1]);
            Flush();
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1 || scroll.Offset.Y > 0);
            var last = Assert.IsType<ListBoxItem>(formats.ContainerFromIndex(export.Formats.Count - 1));
            AssertInside(window, last);
            Click(window, last);
            Assert.Equal("Markdown", export.SelectedFormat.Key);

            var previewPane = view.FindControl<Border>("ExportPreviewPane")!;
            var compactPreview = view.FindControl<Expander>("ExportCompactPreview")!;
            var workspace = view.FindControl<Grid>("ExportWorkspace")!;
            Assert.False(previewPane.IsEffectivelyVisible);
            Assert.True(compactPreview.IsEffectivelyVisible);
            Assert.Equal(0, workspace.ColumnDefinitions[4].ActualWidth);
            var settingsScroll = view.FindControl<ScrollViewer>("ExportSettingsScroll")!;
            settingsScroll.Offset = new Vector(0, settingsScroll.Extent.Height);
            Flush();
            var toggle = Assert.Single(compactPreview.GetVisualDescendants().OfType<ToggleButton>());
            AssertInside(window, toggle);
            Click(window, toggle);
            Assert.True(compactPreview.IsExpanded);
            Assert.Contains(compactPreview.GetVisualDescendants().OfType<ExportPreviewText>(), text => text.Block?.Text.Contains(editor.Text, StringComparison.Ordinal) == true);
            Capture(window, theme + "-preview");
            Click(window, toggle);
            settingsScroll.Offset = new Vector(0, settingsScroll.Extent.Height);
            Flush();
            var write = view.FindControl<Button>("ExportWriteButton")!;
            AssertInside(window, write);
            var dialogs = Assert.IsType<StubFileDialogService>(provider.GetRequiredService<IFileDialogService>());
            dialogs.ExportPath = context.Workspace.FilePath("compact.md");
            Capture(window, theme + "-formats");
            Click(window, write);
            if (export.ExportCommand.ExecutionTask is { } execution) { await execution; }
            Assert.False(export.HasError, export.ErrorMessage);
            Assert.True(File.Exists(dialogs.ExportPath));
            Assert.Contains(editor.Text, await File.ReadAllTextAsync(dialogs.ExportPath));

            window.Width = 1440;
            window.Height = 900;
            Flush();
            Assert.True(previewPane.IsEffectivelyVisible);
            Assert.False(compactPreview.IsEffectivelyVisible);
            Assert.True(view.FindControl<GridSplitter>("ExportRightSplitter")!.IsEffectivelyVisible);
            Assert.True(workspace.ColumnDefinitions[4].ActualWidth >= 140);
            Assert.Equal("Markdown", export.SelectedFormat.Key);
            Capture(window, theme + "-wide");
        }
        finally { window.Close(); }
    }

    private static void AssertInside(Window window, Control control)
    {
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.InRange(point.X, 0, window.Bounds.Width - control.Bounds.Width + 1);
        Assert.InRange(point.Y, 0, window.Bounds.Height - control.Bounds.Height + 1);
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

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("SCRISELF_AUDIT_UI_OUTPUT_DIR");
        if (string.IsNullOrEmpty(directory)) { return; }
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(directory, "export-" + name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
