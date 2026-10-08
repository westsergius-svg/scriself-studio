namespace ScriSelfStudio.UiSmokeTests;

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
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation.Theme;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Рабочая оболочка (ТЗ п. 27): верхняя строка, левое дерево книги,
/// правая панель контекста сцены и навигация по сценам в редакторе.
/// </summary>
public sealed class WorkspaceShellTests
{
    [AvaloniaFact]
    public async Task Workspace_shows_tree_and_scene_context_when_scene_open()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Рукопись.scri");
        var dialogs = new StubFileDialogService { CreatePath = path };
        dialogs.PromptResponses.Enqueue("Книга 1");
        dialogs.PromptResponses.Enqueue("Глава 1");
        dialogs.PromptResponses.Enqueue("Сцена 1");
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
        architecture.Selected = architecture.Roots[0].Children[0].Children[0].Children[0];
        await architecture.OpenSceneCommand.ExecuteAsync(null);

        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var shell = provider.GetRequiredService<ShellViewModel>();
        Assert.True(shell.ShowWorkspace);
        Assert.True(shell.ShowScenePanel);
        Assert.Equal("Книга 1 › Глава 1 › Сцена 1", shell.BreadcrumbsText);
        Assert.Equal("Автосохранение: включено", shell.AutosaveText);

        // Лёвое дерево: список глав привязан к модели дерева книги.
        var tree = window.GetVisualDescendants()
            .OfType<ListBox>()
            .FirstOrDefault(listBox => listBox.DataContext is BookTreeViewModel);
        Assert.NotNull(tree);
        var bookTree = provider.GetRequiredService<BookTreeViewModel>();
        Assert.Single(bookTree.Books);
        Assert.Single(bookTree.Chapters);
        Assert.Single(bookTree.Chapters[0].Scenes);
        Assert.True(bookTree.Chapters[0].Scenes[0].IsCurrent);

        // Правая панель «Сцена» видна при открытой сцене.
        var headers = window.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Contains(headers, textBlock => textBlock.Text == "Сцена");
        var sceneContext = provider.GetRequiredService<SceneContextViewModel>();
        Assert.True(sceneContext.HasScene);
        Assert.Equal("Сцена 1", sceneContext.SceneTitle);

        window.Close();
    }

    [AvaloniaFact]
    public void Theme_switcher_lists_five_themes_and_applies_the_chosen_one()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root, new StubFileDialogService());

        var shell = provider.GetRequiredService<ShellViewModel>();

        // Пять тем макета: Studio, Paper, Graphite, Ocean, Forest.
        Assert.Equal(
            5,
            shell.Themes.Count);
        Assert.Equal(AppTheme.Studio, shell.Themes[0].Value);
        Assert.Equal(AppTheme.Forest, shell.Themes[4].Value);

        // Кнопка раскрывает меню, выбор темы применяется и закрывает меню.
        shell.ToggleThemeMenuCommand.Execute(null);
        Assert.True(shell.IsThemeMenuOpen);

        shell.SelectThemeCommand.Execute(shell.Themes[2]);
        Assert.False(shell.IsThemeMenuOpen);
        Assert.Equal("Graphite", shell.CurrentThemeName);
        Assert.Equal(AppTheme.Graphite, provider.GetRequiredService<IThemeService>().Current);
    }

    [AvaloniaFact]
    public async Task Editor_navigates_between_scenes_and_marks_the_scene()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var dialogs = new StubFileDialogService { CreatePath = path };
        dialogs.PromptResponses.Enqueue("Книга");
        dialogs.PromptResponses.Enqueue("Глава");
        dialogs.PromptResponses.Enqueue("Сцена 1");
        dialogs.PromptResponses.Enqueue("Сцена 2");
        dialogs.PromptResponses.Enqueue("Сцена 3");
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
        await architecture.CreateSceneCommand.ExecuteAsync(null);
        await architecture.CreateSceneCommand.ExecuteAsync(null);

        // Открываем среднюю сцену: навигация в обе стороны должна быть доступна.
        architecture.Selected = architecture.Roots[0].Children[0].Children[0].Children[1];
        await architecture.OpenSceneCommand.ExecuteAsync(null);

        var editor = provider.GetRequiredService<EditorViewModel>();
        Assert.Equal(2, editor.SceneNumber);
        Assert.Equal("СЦЕНА 2", editor.SceneMarkerText);
        Assert.Equal("Сцена 1", editor.PreviousSceneLabel);
        Assert.Equal("Сцена 3", editor.NextSceneLabel);
        Assert.True(editor.CanPreviousScene);
        Assert.True(editor.CanNextScene);

        await editor.NextSceneCommand.ExecuteAsync(null);
        Assert.Equal("Сцена 3", editor.SceneTitle);
        Assert.Equal("Сцена 2", editor.PreviousSceneLabel);
        Assert.False(editor.CanNextScene);

        await editor.PreviousSceneCommand.ExecuteAsync(null);
        Assert.Equal("Сцена 2", editor.SceneTitle);
        Assert.Equal("Сцена 1", editor.PreviousSceneLabel);
    }

    [AvaloniaFact]
    public async Task Tree_adds_chapter_and_scene_and_opening_scene_switches_to_editor()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, PromptResult = "Элемент" };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);

        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);
        launcher.OpenArchitectureCommand.Execute(null);

        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        architecture.Selected = architecture.Roots[0];
        await architecture.CreateBookCommand.ExecuteAsync(null);

        var bookTree = provider.GetRequiredService<BookTreeViewModel>();
        bookTree.Refresh();
        Assert.Single(bookTree.Books);
        Assert.Empty(bookTree.Chapters);

        await bookTree.AddChapterCommand.ExecuteAsync(null);
        Assert.Single(bookTree.Chapters);

        // Ношки дерева пересоздаются после каждого изменения структуры,
        // поэтому команды берём из свежих коллекций.
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)bookTree.Chapters[0].AddSceneCommand!).ExecuteAsync(null);
        Assert.Single(bookTree.Chapters[0].Scenes);
        var sceneNode = bookTree.Chapters[0].Scenes[0];
        Assert.NotNull(sceneNode.OpenCommand);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)sceneNode.OpenCommand!).ExecuteAsync(null);
        var editor = provider.GetRequiredService<EditorViewModel>();
        Assert.True(editor.IsSceneOpen);
        Assert.Equal(ScriSelfStudio.Contracts.Navigation.AppScreen.Editor,
            provider.GetRequiredService<ScriSelfStudio.Contracts.Navigation.INavigationService>().Current);
        Assert.True(bookTree.Chapters[0].Scenes[0].IsCurrent);
    }

    /// <summary>Создаёт проект из одной книги/главы/сцены и открывает сцену в редакторе.</summary>
    private static async Task CreateProjectWithSceneAsync(ServiceProvider provider)
    {
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
        architecture.Selected = architecture.Roots[0].Children[0].Children[0].Children[0];
        await architecture.OpenSceneCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public async Task Full_canvas_hides_side_panels_and_restores_them_on_toggle()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Полный холст.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, PromptResult = "Элемент" };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        await CreateProjectWithSceneAsync(provider);

        var shell = provider.GetRequiredService<ShellViewModel>();
        var editor = provider.GetRequiredService<EditorViewModel>();
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Обычный режим: обе панели видны, у холста стандартные отступы 24px.
        Assert.True(shell.ShowBookTree);
        Assert.True(shell.ShowScenePanel);
        Assert.False(editor.IsFullCanvas);
        Assert.Equal(new Avalonia.Thickness(24), editor.CanvasOuterMargin);

        // Включение «Полного холста»: панели исчезают, холст растягивается,
        // боковые отступы становятся по 20px (ТЗ: режим холста).
        shell.IsFullCanvas = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.ShowBookTree);
        Assert.False(shell.ShowScenePanel);
        Assert.True(editor.IsFullCanvas);
        Assert.Equal(20, editor.CanvasOuterMargin.Left);
        Assert.Equal(20, editor.CanvasOuterMargin.Right);
        Assert.Equal(0, editor.CanvasPaperMargin.Left);

        // Выключение возвращает панели и прежние отступы.
        shell.IsFullCanvas = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.ShowBookTree);
        Assert.True(shell.ShowScenePanel);
        Assert.False(editor.IsFullCanvas);
        Assert.Equal(new Avalonia.Thickness(24), editor.CanvasOuterMargin);

        window.Close();
    }

    [AvaloniaFact]
    public void Pane_widths_clamp_to_limits()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root, new StubFileDialogService());
        var shell = provider.GetRequiredService<ShellViewModel>();

        Assert.Equal(280, shell.BookTreeWidth);
        Assert.Equal(340, shell.SceneContextWidth);

        shell.SetBookTreeWidth(340);
        Assert.Equal(340, shell.BookTreeWidth);
        shell.SetBookTreeWidth(10000);
        Assert.Equal(ShellViewModel.MaxPaneWidth, shell.BookTreeWidth);
        shell.SetBookTreeWidth(-10000);
        Assert.Equal(ShellViewModel.MinPaneWidth, shell.BookTreeWidth);

        shell.SetSceneContextWidth(300);
        Assert.Equal(300, shell.SceneContextWidth);
        shell.SetSceneContextWidth(10000);
        Assert.Equal(ShellViewModel.MaxPaneWidth, shell.SceneContextWidth);
        shell.SetSceneContextWidth(-10000);
        Assert.Equal(ShellViewModel.MinPaneWidth, shell.SceneContextWidth);
    }

    [AvaloniaFact]
    public async Task Splitter_drag_resizes_both_panes()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Разделители.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, PromptResult = "Элемент" };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        await CreateProjectWithSceneAsync(provider);

        var shell = provider.GetRequiredService<ShellViewModel>();
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var splitters = window.GetVisualDescendants()
            .OfType<Avalonia.Controls.Primitives.Thumb>()
            .Where(thumb => thumb.Classes.Contains("paneSplitter"))
            .ToList();
        Assert.Equal(2, splitters.Count);

        // Левая панель: тянем разделитель вправо — панель расширяется ровно на сдвиг.
        var left = CenterInWindow(splitters[0], window);
        var bookBefore = shell.BookTreeWidth;
        window.MouseDown(left, MouseButton.Left);
        window.MouseMove(left + new Vector(30, 0));
        window.MouseMove(left + new Vector(60, 0));
        window.MouseUp(left + new Vector(60, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(bookBefore + 60, shell.BookTreeWidth, 1);

        // Правая панель: тянем разделитель влево — панель расширяется.
        var right = CenterInWindow(splitters[1], window);
        var sceneBefore = shell.SceneContextWidth;
        window.MouseDown(right, MouseButton.Left);
        window.MouseMove(right + new Vector(-25, 0));
        window.MouseMove(right + new Vector(-50, 0));
        window.MouseUp(right + new Vector(-50, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(sceneBefore + 50, shell.SceneContextWidth, 1);

        window.Close();
    }

    /// <summary>Центр разделителя в координатах окна — точка для headless-мыши.</summary>
    private static Point CenterInWindow(Visual control, Visual window)
    {
        var center = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);
        Assert.NotNull(center);
        return center!.Value;
    }
}
