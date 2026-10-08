namespace ScriSelfStudio.UiSmokeTests;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.TestSupport;

public sealed class EditorAndArchitectureFlowTests
{
    [AvaloniaFact]
    public async Task Mvp_flow_creates_structure_edits_text_and_survives_reopen()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Рукопись.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, ConfirmResult = true };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var projects = provider.GetRequiredService<IProjectService>();
        var navigation = provider.GetRequiredService<INavigationService>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        await launcher.CreateProjectCommand.ExecuteAsync(null);
        Assert.True(launcher.IsProjectOpen);

        launcher.OpenArchitectureCommand.Execute(null);
        Assert.Equal(AppScreen.Architecture, navigation.Current);

        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        Assert.Single(architecture.Roots);
        architecture.Selected = architecture.Roots[0];

        dialogs.PromptResponses.Enqueue("Книга первая");
        await architecture.CreateBookCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0];

        dialogs.PromptResponses.Enqueue("Глава 1");
        await architecture.CreateChapterCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0].Children[0];

        dialogs.PromptResponses.Enqueue("Сцена 1");
        await architecture.CreateSceneCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0].Children[0].Children[0];
        Assert.False(architecture.HasError);

        var sceneId = architecture.Selected.Id;
        await architecture.OpenSceneCommand.ExecuteAsync(null);
        Assert.Equal(AppScreen.Editor, navigation.Current);

        var editor = provider.GetRequiredService<EditorViewModel>();
        Assert.True(editor.IsSceneOpen);
        Assert.Equal("Сцена 1", editor.SceneTitle);

        editor.Text = "Первая строка.\nВторая строка с 🚀.";
        Assert.Equal("Первая строка.\nВторая строка с 🚀.", editor.Text);
        Assert.True(editor.IsDirty);

        // Лента сцен: правка активного блока уходит в документ движка.
        var block = Assert.Single(editor.Blocks);
        Assert.True(block.IsActive);
        Assert.Equal(editor.Text, block.Text);
        Assert.Equal("Первая строка.\nВторая строка с 🚀.", editor.Engine.PlainText);

        await editor.SaveCommand.ExecuteAsync(null);
        Assert.False(editor.IsDirty);
        Assert.False(editor.HasError);

        var saved = projects.GetSceneDocument(sceneId).Require();
        Assert.Equal("Первая строка.\nВторая строка с 🚀.", saved.Document.ToPlainText());
        Assert.Equal(1, saved.Revision);

        await editor.CloseSceneCommand.ExecuteAsync(null);
        Assert.False(editor.IsSceneOpen);
        Assert.True((await projects.CloseAsync(discardChanges: true)).IsSuccess);

        var reopened = await projects.OpenAsync(path);
        Assert.True(reopened.IsSuccess, reopened.Error?.TechnicalDetails);
        var reopenedScene = reopened.Require().Series[0].Books[0].Chapters[0].Scenes[0];
        Assert.Equal(sceneId, reopenedScene.Id);
        Assert.Equal("Сцена 1", reopenedScene.Title);
        Assert.True(reopenedScene.HasText);
        Assert.Equal(
            "Первая строка.\nВторая строка с 🚀.",
            projects.GetSceneDocument(sceneId).Require().Document.ToPlainText());
    }

    [AvaloniaFact]
    public async Task Architecture_screen_renames_moves_and_deletes_entities()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, ConfirmResult = true };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var projects = provider.GetRequiredService<IProjectService>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        architecture.Refresh();
        architecture.Selected = architecture.Roots[0];
        dialogs.PromptResponses.Enqueue("Книга A");
        await architecture.CreateBookCommand.ExecuteAsync(null);
        dialogs.PromptResponses.Enqueue("Книга B");
        await architecture.CreateBookCommand.ExecuteAsync(null);
        Assert.Equal(2, architecture.Roots[0].Children.Count);

        var second = architecture.Roots[0].Children[1];
        architecture.Selected = second;
        dialogs.PromptResponses.Enqueue("Книга Б");
        await architecture.RenameCommand.ExecuteAsync(null);
        Assert.Equal("Книга Б", architecture.Roots[0].Children[1].Title);

        architecture.Selected = architecture.Roots[0].Children[1];
        await architecture.MoveUpCommand.ExecuteAsync(null);
        Assert.Equal("Книга Б", architecture.Roots[0].Children[0].Title);

        architecture.Selected = architecture.Roots[0].Children[1];
        await architecture.DeleteCommand.ExecuteAsync(null);
        Assert.Single(architecture.Roots[0].Children);
        Assert.Equal("Книга Б", architecture.Roots[0].Children[0].Title);
        Assert.True(dialogs.Confirmations.Count >= 1);
    }

    [AvaloniaFact]
    public async Task Editor_view_has_no_hollow_buttons()
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

        var architectureView = window.GetVisualDescendants().OfType<ArchitectureView>().FirstOrDefault();
        var editorView = window.GetVisualDescendants().OfType<EditorView>().FirstOrDefault();
        Assert.Null(architectureView);
        Assert.NotNull(editorView);

        var buttons = editorView!.GetVisualDescendants().OfType<Button>().ToList();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.NotNull(button.Command));

        var editor = provider.GetRequiredService<EditorViewModel>();
        Assert.True(editor.IsSceneOpen);
        // Лента сцен: активный блок правится обычным текстовым полем.
        var activeBlock = Assert.Single(editor.Blocks);
        Assert.True(activeBlock.IsActive);

        var textBox = editorView!.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        Assert.NotNull(textBox);
        Assert.Equal(editor.Text, textBox!.Text);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Editor_plain_text_round_trips_through_the_document()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, PromptResult = "Элемент" };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var projects = provider.GetRequiredService<IProjectService>();
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

        var sceneId = architecture.Selected.Id;
        await architecture.OpenSceneCommand.ExecuteAsync(null);

        var editor = provider.GetRequiredService<EditorViewModel>();
        editor.Text = "один";
        Assert.Equal("один", editor.Engine.PlainText);
        Assert.True(editor.IsDirty);

        editor.Text = "один два";
        Assert.Equal("один два", editor.Engine.PlainText);

        // Собственного движка ввода нет: история отмены движка от текста не растёт,
        // отмену ведёт сам элемент ввода (ТЗ п. 11, 22).
        Assert.False(editor.Engine.CanUndo);

        await editor.SaveCommand.ExecuteAsync(null);
        Assert.False(editor.IsDirty);
        Assert.False(editor.HasError);
        Assert.Equal("один два", projects.GetSceneDocument(sceneId).Require().Document.ToPlainText());
    }
}