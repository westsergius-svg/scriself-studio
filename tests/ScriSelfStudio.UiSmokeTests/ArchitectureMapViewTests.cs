using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.TestSupport;

namespace ScriSelfStudio.UiSmokeTests;

/// <summary>Карта книги в окне Архитектора и переключение на список (ТЗ п. 27).</summary>
public sealed class ArchitectureMapViewTests
{
    [AvaloniaFact]
    public async Task Architecture_window_renders_the_book_map_and_switches_to_the_list()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService
        {
            CreatePath = workspace.FilePath("Карта.scri"),
            PromptResult = "Элемент",
        };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);
        launcher.OpenArchitectureCommand.Execute(null);

        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        architecture.Selected = architecture.Roots[0];
        await architecture.CreateBookCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0];
        await architecture.CreateChapterCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0].Children[0];
        await architecture.CreateSceneCommand.ExecuteAsync(null);

        var window = provider.GetRequiredService<MainWindow>();
        window.Width = 1440;
        window.Height = 768;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var shell = provider.GetRequiredService<ScriSelfStudio.App.Shell.ShellViewModel>();
        Assert.False(shell.ShowBookTree);
        Assert.False(shell.ShowScenePanel);
        Assert.False(shell.ShowStatusBar);

        Assert.True(architecture.IsMapMode);
        var column = Assert.Single(architecture.MapColumns);
        Assert.Single(column.Scenes);

        var view = window.GetVisualDescendants().OfType<ArchitectureView>().FirstOrDefault();
        Assert.NotNull(view);
        Assert.Single(Columns(view!));
        Assert.True(Columns(view!)[0].IsEffectivelyVisible);

        var actionPanel = view!.FindControl<Border>("MapActionsPanel");
        var mapBoard = view.FindControl<Border>("MapBoard");
        var overview = view.FindControl<Border>("MapOverview");
        Assert.NotNull(actionPanel);
        Assert.NotNull(mapBoard);
        Assert.NotNull(overview);
        var actionOrigin = actionPanel!.TranslatePoint(default, view);
        var boardOrigin = mapBoard!.TranslatePoint(default, view);
        var overviewOrigin = overview!.TranslatePoint(default, view);
        Assert.NotNull(actionOrigin);
        Assert.NotNull(boardOrigin);
        Assert.NotNull(overviewOrigin);
        Assert.True(boardOrigin!.Value.Y >= actionOrigin!.Value.Y + actionPanel.Bounds.Height,
            "Карта не должна заходить на панель действий.");
        Assert.True(boardOrigin.Value.Y + mapBoard.Bounds.Height <= overviewOrigin!.Value.Y,
            "Доска карты не должна перекрывать быстрый обзор.");

        // Список — второй вид того же экрана: карта скрывается, дерево появляется.
        architecture.ShowListCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(architecture.IsListMode);
        // Скрытые элементы остаются в дереве, поэтому проверяется видимость, а не наличие.
        Assert.All(Columns(view!), border => Assert.False(border.IsEffectivelyVisible));
        var tree = view!.GetVisualDescendants().OfType<TreeView>().FirstOrDefault();
        Assert.NotNull(tree);
        Assert.True(tree!.IsEffectivelyVisible);

        shell.OpenSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.ShowBookTree);
        Assert.False(shell.ShowScenePanel);
        Assert.False(shell.ShowStatusBar);
        Assert.False(shell.IsFullCanvasAvailable);

        window.Close();
    }

    private static List<Border> Columns(ArchitectureView view) => view.GetVisualDescendants()
        .OfType<Border>()
        .Where(border => border.Classes.Contains("mapColumn"))
        .ToList();

    /// <summary>
    /// Книга создаётся без предварительного выбора серии: берётся первая серия
    /// проекта, новая книга сразу выделяется и попадает в переключатель книг.
    /// </summary>
    [AvaloniaFact]
    public async Task Creating_a_book_without_selection_targets_the_first_series()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService
        {
            CreatePath = workspace.FilePath("Карта.scri"),
            PromptResult = "Книга",
        };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        Assert.True(architecture.CanCreateBook);
        Assert.True(architecture.ShowMapEmpty);

        await architecture.CreateBookCommand.ExecuteAsync(null);

        Assert.False(architecture.HasError, architecture.ErrorMessage);
        var book = Assert.Single(architecture.Books);
        Assert.Equal(EntityKind.Book, book.Kind);
        Assert.Same(book, architecture.SelectedBook);
        Assert.Same(book, architecture.Selected);
    }

    [AvaloniaFact]
    public async Task Chapter_add_button_is_available_for_an_empty_book_and_creates_a_chapter()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService
        {
            CreatePath = workspace.FilePath("Карта.scri"),
            PromptResult = "Глава",
        };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        architecture.Selected = architecture.Roots[0];
        await architecture.CreateBookCommand.ExecuteAsync(null);

        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var view = Assert.Single(window.GetVisualDescendants().OfType<ArchitectureView>());
        var overview = view.FindControl<Border>("MapOverview");
        Assert.NotNull(overview);
        Assert.True(overview!.IsEffectivelyVisible);

        var chapterAdd = Assert.Single(
            view.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("addChapterColumn"));
        Assert.True(chapterAdd.IsEnabled);
        Assert.Same(architecture.CreateChapterInBookCommand, chapterAdd.Command);

        await architecture.CreateChapterInBookCommand.ExecuteAsync(null);

        Assert.False(architecture.HasError, architecture.ErrorMessage);
        Assert.Single(architecture.MapColumns);
        Assert.Single(architecture.ChapterOverview);
        window.Close();
    }

    /// <summary>
    /// Пустая карта предлагает следующий недостающий элемент: сначала книгу,
    /// затем главу. После главы на карте появляется столбец.
    /// </summary>
    [AvaloniaFact]
    public async Task Empty_map_action_creates_the_next_missing_item()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService
        {
            CreatePath = workspace.FilePath("Карта.scri"),
            PromptResult = "Элемент",
        };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        Assert.True(architecture.ShowMapEmpty);
        Assert.Equal("Создать книгу", architecture.MapEmptyActionLabel);

        await architecture.MapEmptyActionCommand.ExecuteAsync(null);
        Assert.Empty(architecture.MapColumns);
        Assert.False(architecture.ShowMapEmpty);
        Assert.True(architecture.CanCreateChapterInBook);

        await architecture.CreateChapterInBookCommand.ExecuteAsync(null);
        Assert.False(architecture.HasError, architecture.ErrorMessage);
        Assert.True(architecture.HasMapColumns);
        Assert.False(architecture.ShowMapEmpty);
        Assert.Single(architecture.MapColumns);
        Assert.Equal(EntityKind.Chapter, architecture.Selected!.Kind);
    }
}
