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
using AvaloniaRichEditor.Controls;

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
        dialogs.PromptResponses.Enqueue("Пробуждение");
        dialogs.PromptResponses.Enqueue("Начало");
        dialogs.PromptResponses.Enqueue("Дорога");
        dialogs.PromptResponses.Enqueue("Тени прошлого");
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
        architecture.Selected = architecture.Roots[0];
        await architecture.CreateBookCommand.ExecuteAsync(null);

        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var shell = provider.GetRequiredService<ShellViewModel>();
        Assert.True(shell.ShowWorkspace);
        Assert.True(shell.ShowScenePanel);
        Assert.Equal("Пробуждение › Начало › Дорога", shell.BreadcrumbsText);
        Assert.Equal("Автосохранение: включено", shell.AutosaveText);

        // Левое дерево показывает всю иерархию серий, книг, глав и сцен.
        var tree = window.GetVisualDescendants()
            .OfType<ItemsControl>()
            .FirstOrDefault(itemsControl => itemsControl.DataContext is BookTreeViewModel);
        Assert.NotNull(tree);
        var bookTree = provider.GetRequiredService<BookTreeViewModel>();
        Assert.Equal(2, bookTree.Books.Count);
        Assert.Single(bookTree.Series);
        Assert.Equal(2, bookTree.Series[0].Books.Count);
        Assert.True(bookTree.Series[0].IsExpanded);
        Assert.Equal("Рукопись", bookTree.Series[0].DisplayTitle);
        Assert.True(bookTree.Series[0].Books[0].IsExpanded);
        Assert.False(bookTree.Series[0].Books[1].IsExpanded);
        Assert.Equal("Книга 1. Пробуждение", bookTree.Series[0].Books[0].DisplayTitle);
        Assert.Equal("Глава 1. Начало", bookTree.Series[0].Books[0].Chapters[0].DisplayTitle);
        Assert.Equal("Сцена 1. Дорога", bookTree.Series[0].Books[0].Chapters[0].Scenes[0].DisplayTitle);
        var treeRows = window.GetVisualDescendants().OfType<Button>().ToArray();
        var seriesX = treeRows.Single(button => ReferenceEquals(button.DataContext, bookTree.Series[0]))
            .TranslatePoint(new Point(0, 0), window)!.Value.X;
        var bookX = treeRows.Single(button => ReferenceEquals(button.DataContext, bookTree.Series[0].Books[0]))
            .TranslatePoint(new Point(0, 0), window)!.Value.X;
        var chapter = bookTree.Series[0].Books[0].Chapters[0];
        var chapterX = treeRows.Single(button =>
                ReferenceEquals(button.DataContext, chapter) && button.Classes.Contains("chapterNode"))
            .TranslatePoint(new Point(0, 0), window)!.Value.X;
        var sceneX = treeRows.Single(button => ReferenceEquals(button.DataContext, bookTree.Series[0].Books[0].Chapters[0].Scenes[0]))
            .TranslatePoint(new Point(0, 0), window)!.Value.X;
        Assert.True(bookX - seriesX >= 12 && chapterX - bookX >= 12 && sceneX - chapterX >= 12,
            $"Строки дерева должны сдвигаться вправо по вложенности: {seriesX}, {bookX}, {chapterX}, {sceneX}.");
        var chapterAdd = treeRows.Single(button =>
            ReferenceEquals(button.DataContext, chapter) && button.Classes.Contains("chapterAdd"));
        Assert.Equal(0, chapterAdd.Opacity);
        var chapterAddCenter = chapterAdd.TranslatePoint(
            new Point(chapterAdd.Bounds.Width / 2, chapterAdd.Bounds.Height / 2),
            window);
        Assert.NotNull(chapterAddCenter);
        window.MouseMove(chapterAddCenter!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, chapterAdd.Opacity);
        window.MouseMove(new Point(0, 0));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, chapterAdd.Opacity);
        Assert.Single(bookTree.Chapters);
        Assert.Single(bookTree.Chapters[0].Scenes);
        Assert.True(bookTree.Chapters[0].Scenes[0].IsCurrent);

        var firstBook = bookTree.Series[0].Books[0];
        firstBook.ToggleCommand!.Execute(null);
        Assert.False(firstBook.IsExpanded);
        Assert.Equal(firstBook.Id, bookTree.SelectedBook!.Id);
        firstBook.ToggleCommand.Execute(null);
        Assert.True(firstBook.IsExpanded);

        bookTree.Series[0].ToggleExpandedCommand.Execute(null);
        bookTree.Refresh();
        Assert.False(bookTree.Series[0].IsExpanded);

        // Правая панель «Сцена» видна при открытой сцене.
        var headers = window.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Contains(headers, textBlock => textBlock.Text == "Сцена");
        var sceneContext = provider.GetRequiredService<SceneContextViewModel>();
        Assert.True(sceneContext.HasScene);
        Assert.Equal("Дорога", sceneContext.SceneTitle);

        shell.OpenCharactersCommand.Execute(null);
        Assert.Equal(ScriSelfStudio.Contracts.Navigation.AppScreen.Characters, shell.CurrentScreen);
        Assert.True(shell.IsCharactersActive);
        shell.OpenLinesCommand.Execute(null);
        Assert.Equal(ScriSelfStudio.Contracts.Navigation.AppScreen.Timeline, shell.CurrentScreen);
        Assert.True(shell.IsLinesActive);
        shell.OpenMapCommand.Execute(null);
        Assert.Equal(ScriSelfStudio.Contracts.Navigation.AppScreen.Architecture, shell.CurrentScreen);
        Assert.True(shell.IsMapActive);

        window.Close();
    }

    [AvaloniaFact]
    public void Theme_selection_is_temporarily_limited_to_the_standard_theme()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root, new StubFileDialogService());

        var shell = provider.GetRequiredService<ShellViewModel>();

        // Выбор тем временно отключён: осталась только стандартная тема.
        Assert.Single(shell.Themes);
        Assert.Equal(AppTheme.Starter, shell.Themes[0].Value);

        // Кнопка раскрывает меню, выбор темы применяется и закрывает меню.
        shell.ToggleThemeMenuCommand.Execute(null);
        Assert.True(shell.IsThemeMenuOpen);

        shell.SelectThemeCommand.Execute(shell.Themes[0]);
        Assert.False(shell.IsThemeMenuOpen);
        Assert.Equal("Стандартная", shell.CurrentThemeName);
        Assert.Equal(AppTheme.Starter, provider.GetRequiredService<IThemeService>().Current);
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

    [AvaloniaFact]
    public async Task Tree_scene_switch_keeps_text_of_the_previous_scene()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Проект.scri") };
        dialogs.PromptResponses.Enqueue("Книга");
        dialogs.PromptResponses.Enqueue("Глава");
        dialogs.PromptResponses.Enqueue("Сцена 1");
        dialogs.PromptResponses.Enqueue("Сцена 2");
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

        var chapter = architecture.Roots[0].Children[0].Children[0];
        var firstSceneId = chapter.Children[0].Id;
        var secondSceneId = chapter.Children[1].Id;
        architecture.Selected = chapter.Children[0];
        await architecture.OpenSceneCommand.ExecuteAsync(null);

        var editor = provider.GetRequiredService<EditorViewModel>();
        editor.Text = "Текст первой сцены";
        Assert.True(editor.IsDirty);

        var sceneContext = provider.GetRequiredService<SceneContextViewModel>();
        sceneContext.Refresh();
        sceneContext.Summary = "Кратко о первой сцене";
        sceneContext.Notes = "Заметка первой сцены";

        var bookTree = provider.GetRequiredService<BookTreeViewModel>();
        bookTree.Refresh();
        var scenes = bookTree.Chapters[0].Scenes;
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)scenes[1].OpenCommand!).ExecuteAsync(null);

        Assert.Equal(secondSceneId, editor.Engine.SceneId);
        Assert.Equal(
            "Текст первой сцены",
            provider.GetRequiredService<ScriSelfStudio.Contracts.Projects.IProjectService>()
                .GetSceneDocument(firstSceneId).Require().Document.ToPlainText());
        var firstScene = provider.GetRequiredService<ScriSelfStudio.Contracts.Projects.IProjectService>()
            .Current!.Series.SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Scenes)
            .Single(scene => scene.Id == firstSceneId);
        Assert.Equal("Кратко о первой сцене", firstScene.Description);
        Assert.Equal("Заметка первой сцены", firstScene.Notes);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)scenes[0].OpenCommand!).ExecuteAsync(null);
        Assert.Equal(firstSceneId, editor.Engine.SceneId);
        Assert.Equal("Текст первой сцены", editor.Text);
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
    public async Task Escape_exits_full_canvas_mode()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Холст и Esc.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, PromptResult = "Элемент" };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        await CreateProjectWithSceneAsync(provider);

        var shell = provider.GetRequiredService<ShellViewModel>();
        var editor = provider.GetRequiredService<EditorViewModel>();
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Вне режима холста команда выхода недоступна, поэтому Esc не перехватывается.
        Assert.False(editor.ExitFullCanvasCommand.CanExecute(null));

        shell.IsFullCanvas = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.ExitFullCanvasCommand.CanExecute(null));
        Assert.False(shell.ShowBookTree);

        // Esc из окна (не из редактора) обязан выключить режим и вернуть панели.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(editor.IsFullCanvas);
        Assert.True(shell.ShowBookTree);
        Assert.True(shell.ShowScenePanel);
        Assert.Equal(new Avalonia.Thickness(24), editor.CanvasOuterMargin);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Canvas_mode_lays_the_ribbon_out_as_word_pages()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Листы A4.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, PromptResult = "Элемент" };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        await CreateProjectWithSceneAsync(provider);

        var shell = provider.GetRequiredService<ShellViewModel>();
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var editor = window.GetVisualDescendants().OfType<RichEditor>().First();

        // Обычный режим: сплошной поток по ширине панели, без листов.
        Assert.Equal(RichEditorPageSize.Continuous, editor.PageSize);
        Assert.False(editor.ShowPageBoundaries);

        // Режим «Холст»: лента показывается листами Word формата A4 (210x297 мм).
        shell.IsFullCanvas = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(RichEditorPageSize.A4, editor.PageSize);
        Assert.Equal(RichEditorPageOrientation.Portrait, editor.PageOrientation);
        Assert.True(editor.ShowPageBoundaries);

        var paper = editor.GetPaperPixelSize();
        Assert.Equal(Math.Round(210d / 25.4d * 96d), Math.Round(paper.Width));
        Assert.Equal(Math.Round(297d / 25.4d * 96d), Math.Round(paper.Height));

        // Выход из режима возвращает сплошной поток.
        shell.IsFullCanvas = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(RichEditorPageSize.Continuous, editor.PageSize);
        Assert.False(editor.ShowPageBoundaries);

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
