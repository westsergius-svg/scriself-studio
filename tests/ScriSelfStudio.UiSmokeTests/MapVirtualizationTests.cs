namespace ScriSelfStudio.UiSmokeTests;

using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
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
using ScriSelfStudio.ArchitectureUI.Map;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;

public sealed class MapVirtualizationTests
{
    [AvaloniaTheory]
    [InlineData(1, 500)]
    [InlineData(25, 20)]
    public async Task Large_book_realizes_only_nearby_cards_and_keeps_selection_after_scrolling(int chapters, int scenesPerChapter)
    {
        using var context = await SceneContext.CreateAsync();
        var projects = context.Provider.GetRequiredService<IProjectService>();
        Assert.True(projects.Mutate(project =>
        {
            var book = project.Series[0].Books[0];
            var initialChapter = book.Chapters[0];
            var initialScene = initialChapter.Scenes[0];
            book.Chapters.Clear();
            for (var chapterIndex = 0; chapterIndex < chapters; chapterIndex++)
            {
                var chapter = chapterIndex == 0 ? initialChapter : new Chapter { BookId = book.Id, Order = chapterIndex };
                chapter.Title = $"Глава {chapterIndex + 1}";
                chapter.Scenes.Clear();
                for (var sceneIndex = 0; sceneIndex < scenesPerChapter; sceneIndex++)
                {
                    var scene = chapterIndex == 0 && sceneIndex == 0 ? initialScene : new Scene
                    {
                        ChapterId = chapter.Id, Order = sceneIndex,
                    };
                    scene.Title = $"Сцена {chapterIndex + 1}.{sceneIndex + 1}";
                    scene.Description = "Описание синтетической сцены для проверки карты.";
                    chapter.Scenes.Add(scene);
                }
                book.Chapters.Add(chapter);
            }
            return Result.Success;
        }).IsSuccess);
        Assert.True((await projects.SaveAsync()).IsSuccess);
        var architecture = context.Provider.GetRequiredService<ArchitectureViewModel>();
        architecture.Refresh();
        Assert.True(context.Provider.GetRequiredService<INavigationService>().NavigateTo(AppScreen.Architecture).IsSuccess);
        var window = context.Provider.GetRequiredService<MainWindow>();
        window.Width = 1200;
        window.Height = 720;
        var watch = Stopwatch.StartNew();
        window.Show();
        Flush();
        watch.Stop();
        try
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ArchitectureView>());
            var columns = view.FindControl<ItemsControl>("MapColumnItems")!;
            var realizedColumns = view.GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("mapColumn"));
            var realizedCards = Cards(view).Length;
            Assert.Equal(500, architecture.MapColumns.Sum(column => column.Scenes.Count));
            Assert.InRange(realizedColumns, 1, 9);
            Assert.InRange(realizedCards, 1, 100);
            Assert.IsType<VirtualizingStackPanel>(columns.ItemsPanelRoot);
            Record(window, projects.CurrentFilePath!, chapters, scenesPerChapter, realizedColumns, realizedCards, watch.Elapsed.TotalMilliseconds);

            var lastColumn = architecture.MapColumns[^1];
            columns.ScrollIntoView(lastColumn);
            Flush();
            var columnBorder = Assert.Single(view.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("mapColumn") && ReferenceEquals(border.DataContext, lastColumn));
            var list = Assert.Single(columnBorder.GetVisualDescendants().OfType<ListBox>(), control => control.Classes.Contains("mapScenes"));
            list.SelectedIndex = 0;
            Flush();
            Assert.True(list.ContainerFromIndex(0)!.Focus());
            window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
            Flush();
            var lastScene = lastColumn.Scenes[^1];
            Assert.Equal(lastScene.Id, architecture.Selected!.Id);
            Assert.True(lastScene.IsSelected);
            Assert.Same(lastScene, lastColumn.SelectedScene);
            Assert.Single(architecture.MapColumns.SelectMany(column => column.Scenes), scene => scene.IsSelected);
            var lastCard = Assert.Single(Cards(view), button => button.DataContext is BookMapSceneViewModel scene && scene.Id == lastScene.Id);
            Click(window, lastCard);
            Assert.Equal(lastScene.Id, architecture.SceneDetails!.SceneId);
            Assert.InRange(Cards(view).Length, 1, 100);

            columns.ScrollIntoView(architecture.MapColumns[0]);
            Flush();
            Assert.Equal(lastScene.Id, architecture.Selected!.Id);
            columns.ScrollIntoView(lastColumn);
            Flush();
            columnBorder = Assert.Single(view.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("mapColumn") && ReferenceEquals(border.DataContext, lastColumn));
            list = Assert.Single(columnBorder.GetVisualDescendants().OfType<ListBox>(), control => control.Classes.Contains("mapScenes"));
            Assert.Same(lastScene, list.SelectedItem);
            list.ScrollIntoView(lastScene);
            Flush();
            Assert.Contains(Cards(view), button => button.Classes.Contains("selected") && button.DataContext is BookMapSceneViewModel scene && scene.Id == lastScene.Id);
            await architecture.SceneDetails.OpenCommand.ExecuteAsync(null);
            Assert.Equal(lastScene.Id, context.Provider.GetRequiredService<ScriSelfStudio.Contracts.Editor.IEditorEngine>().SceneId);
        }
        finally { window.Close(); }
    }

    private static Button[] Cards(Control root) => root.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("mapCell")).ToArray();

    private static void Record(Window window, string projectPath, int chapters, int scenes, int columns, int cards, double milliseconds)
    {
        var directory = Environment.GetEnvironmentVariable("SCRISELF_AUDIT_UI_OUTPUT_DIR");
        if (string.IsNullOrEmpty(directory)) { return; }
        Directory.CreateDirectory(directory);
        var name = $"map-{chapters}x{scenes}";
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new
        {
            Mode = "Avalonia headless / Skia, Release", Chapters = chapters, ScenesPerChapter = scenes,
            RealizedColumns = columns, RealizedSceneCards = cards, Visuals = window.GetVisualDescendants().Count(),
            InitialLayoutMilliseconds = milliseconds,
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.Copy(projectPath, Path.Combine(directory, name + ".scri"), overwrite: true);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static void Click(Window window, Control control)
    {
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
}
