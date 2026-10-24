namespace ScriSelfStudio.UiSmokeTests;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.TestSupport;
using RichTextBlock = ScriSelfStudio.Core.Text.TextBlock;

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
    public async Task Scene_ribbon_keeps_scene_documents_separate_and_saves_rich_formatting()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { CreatePath = workspace.FilePath("Лента.scri") };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var projects = provider.GetRequiredService<IProjectService>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);
        launcher.OpenArchitectureCommand.Execute(null);

        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        architecture.Selected = architecture.Roots[0];
        dialogs.PromptResponses.Enqueue("Книга");
        await architecture.CreateBookCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0];
        dialogs.PromptResponses.Enqueue("Глава");
        await architecture.CreateChapterCommand.ExecuteAsync(null);
        var chapterNode = architecture.Roots[0].Children[0].Children[0];

        architecture.Selected = chapterNode;
        dialogs.PromptResponses.Enqueue("Первая встреча");
        await architecture.CreateSceneCommand.ExecuteAsync(null);
        chapterNode = architecture.Roots[0].Children[0].Children[0];
        var firstSceneId = chapterNode.Children[0].Id;
        architecture.Selected = chapterNode;
        dialogs.PromptResponses.Enqueue("Вторая встреча");
        await architecture.CreateSceneCommand.ExecuteAsync(null);
        chapterNode = architecture.Roots[0].Children[0].Children[0];
        var secondSceneId = chapterNode.Children[1].Id;
        architecture.Selected = chapterNode;
        dialogs.PromptResponses.Enqueue("Третья встреча");
        await architecture.CreateSceneCommand.ExecuteAsync(null);
        chapterNode = architecture.Roots[0].Children[0].Children[0];
        var thirdSceneId = chapterNode.Children[2].Id;

        architecture.Selected = chapterNode.Children[0];
        await architecture.OpenSceneCommand.ExecuteAsync(null);
        var editor = provider.GetRequiredService<EditorViewModel>();
        Assert.Equal("Сцена Первая встреча", editor.CurrentCaretSceneTitle);
        editor.SetRibbonCaretLine(3);
        Assert.Equal("Сцена Вторая встреча", editor.CurrentCaretSceneTitle);
        editor.SetRibbonCaretLine(29);
        Assert.Equal("Сцена Третья встреча", editor.CurrentCaretSceneTitle);

        var ribbon = editor.RibbonDocument;
        Assert.Equal(5, ribbon.Blocks.Count);
        Assert.Equal(BlockKind.Separator, ribbon.Blocks[1].Kind);
        ribbon.Blocks[0].Spans.Add(new TextSpan
        {
            Text = "Форматированная первая сцена",
            Style = new SpanStyle { Flags = TextFormat.Bold },
        });
        ribbon.Blocks[2].Spans.Add(new TextSpan { Text = "Длинная вторая сцена" });
        ribbon.Blocks[2].Alignment = TextAlignment.Center;
        for (var paragraphIndex = 0; paragraphIndex < 24; paragraphIndex++)
        {
            ribbon.Blocks.Insert(3 + paragraphIndex, new RichTextBlock
            {
                Spans = { new TextSpan { Text = $"Продолжение длинной сцены, абзац {paragraphIndex + 1}." } },
            });
        }
        Assert.True(editor.SynchronizeRibbonDocument(ribbon));
        Assert.True(editor.IsDirty);

        var window = provider.GetRequiredService<ScriSelfStudio.App.Views.MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var sceneChips = window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("sceneChip"))
            .ToList();
        Assert.Equal(editor.Blocks.Count, sceneChips.Count);
        Assert.All(sceneChips, chip => Assert.NotNull(chip.Command));
        var richEditor = window.GetVisualDescendants().OfType<RichEditor>().First();
        Assert.NotNull(richEditor.Document);
        var flowBlocks = richEditor.Document!.Blocks.ToList();
        var dividerIndexes = flowBlocks
            .Select((block, index) => (block, index))
            .Where(item => item.block is DividerBlock)
            .Select(item => item.index)
            .ToList();
        Assert.Equal(2, dividerIndexes.Count);
        // Каждая сцена начинается видимым заголовком; в модель он не попадает.
        Assert.Equal(
            editor.Blocks.Count,
            flowBlocks.OfType<Paragraph>().Count(paragraph => paragraph.Inlines
                .OfType<Run>()
                .Any(run => run.Text?.StartsWith('\u200B') == true)));
        // Каждая следующая сцена начинается с новой страницы: 46 строк Word.
        // Интервал стоит перед разделителем, чтобы линия была над заголовком сцены.
        Assert.All(dividerIndexes, index => Assert.Equal(
            46 * richEditor.DefaultFontSize * 1.5,
            flowBlocks[index].MarginTop));
        // Разделитель сцены несёт собственный отступ, поэтому лента не добавляет свой:
        // иначе разрыв между сценами удваивался (было 56, стало 0).
        Assert.Equal(0, flowBlocks[dividerIndexes[0] - 1].MarginBottom);
        Assert.Equal(0, flowBlocks[dividerIndexes[1] - 1].MarginBottom);

        await editor.SaveCommand.ExecuteAsync(null);

        var firstDocument = projects.GetSceneDocument(firstSceneId).Require().Document;
        var secondDocument = projects.GetSceneDocument(secondSceneId).Require().Document;
        var thirdDocument = projects.GetSceneDocument(thirdSceneId).Require().Document;
        Assert.Equal("Форматированная первая сцена", firstDocument.ToPlainText());
        Assert.Equal(TextFormat.Bold, firstDocument.Blocks[0].Spans[0].Style.Flags);
        Assert.Contains("Продолжение длинной сцены, абзац 24.", secondDocument.ToPlainText());
        Assert.Equal(TextAlignment.Center, secondDocument.Blocks[0].Alignment);
        Assert.Equal(string.Empty, thirdDocument.ToPlainText());
        Assert.Equal(1, projects.GetSceneDocument(firstSceneId).Require().Revision);
        Assert.Equal(1, projects.GetSceneDocument(secondSceneId).Require().Revision);
        Assert.Equal(0, projects.GetSceneDocument(thirdSceneId).Require().Revision);
        Assert.False(editor.IsDirty);
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

        var editor = provider.GetRequiredService<EditorViewModel>();
        Assert.True(editor.IsSceneOpen);
        // The active scene block is backed by the WYSIWYG rich-text editor.
        var activeBlock = Assert.Single(editor.Blocks);
        Assert.True(activeBlock.IsActive);

        var richEditor = editorView!.GetVisualDescendants().OfType<RichEditor>().FirstOrDefault();
        Assert.NotNull(richEditor);
        Assert.Equal(editor.Text, WithoutSceneHeaders(richEditor!.GetPlainText()));
        Assert.Contains(editorView.GetVisualDescendants().OfType<RichEditorToolbar>(),
            toolbar => ReferenceEquals(toolbar.Target, richEditor));

        richEditor.LoadHtml("<p>Кириллица, пробел и 🚀.<br />Вторая строка.</p>");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Кириллица, пробел и 🚀.\nВторая строка.",
            editor.Text.Replace("\r\n", "\n"));
        Assert.Equal(editor.Text, editor.Engine.PlainText);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Rich_editor_block_formatting_is_rendered_and_persisted()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Форматирование абзаца.scri");
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
        var sceneId = architecture.Roots[0].Children[0].Children[0].Children[0].Id;
        architecture.Selected = architecture.Roots[0].Children[0].Children[0].Children[0];
        await architecture.OpenSceneCommand.ExecuteAsync(null);

        var editor = provider.GetRequiredService<EditorViewModel>();
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var richEditor = Assert.Single(window.GetVisualDescendants().OfType<RichEditor>());

        richEditor.LoadHtml("<p>Выравнивание и список</p>");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        richEditor.SetTextAlignment(Avalonia.Media.TextAlignment.Center);
        richEditor.ToggleBullet();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var document = editor.Engine.GetDocumentSnapshot().Require().Document;
        var formattedBlock = Assert.Single(
            document.Blocks,
            block => string.Concat(block.Spans.Select(span => span.Text)) == "Выравнивание и список");
        Assert.Equal(TextAlignment.Center, formattedBlock.Alignment);
        Assert.Equal(BlockKind.BulletListItem, formattedBlock.Kind);
        Assert.Equal("Выравнивание и список", editor.Engine.PlainText);

        await editor.SaveCommand.ExecuteAsync(null);
        Assert.False(editor.IsDirty);
        var saved = projects.GetSceneDocument(sceneId).Require().Document;
        Assert.Equal(TextAlignment.Center, saved.Blocks[0].Alignment);
        Assert.Equal(BlockKind.BulletListItem, saved.Blocks[0].Kind);

        await editor.CloseSceneCommand.ExecuteAsync(null);
        Assert.True((await projects.CloseAsync(discardChanges: true)).IsSuccess);
        Assert.True((await projects.OpenAsync(path)).IsSuccess);
        var reopened = projects.GetSceneDocument(sceneId).Require().Document;
        Assert.Equal(TextAlignment.Center, reopened.Blocks[0].Alignment);
        Assert.Equal(BlockKind.BulletListItem, reopened.Blocks[0].Kind);
        Assert.Equal("Выравнивание и список", reopened.ToPlainText());

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

        // Text input history belongs to AvalonEdit rather than the document engine.
        Assert.False(editor.Engine.CanUndo);

        await editor.SaveCommand.ExecuteAsync(null);
        Assert.False(editor.IsDirty);
        Assert.False(editor.HasError);
        Assert.Equal("один два", projects.GetSceneDocument(sceneId).Require().Document.ToPlainText());
    }

    [AvaloniaFact]
    public async Task Editor_formatting_applies_to_selection_and_plain_text_edits_preserve_it()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Форматирование.scri");
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
        var sceneId = architecture.Selected.Id;
        await architecture.OpenSceneCommand.ExecuteAsync(null);

        var editor = provider.GetRequiredService<EditorViewModel>();
        editor.Text = "Слова 🚀 здесь";
        editor.SetEditorSelection(0, "Слова".Length);
        editor.ToggleBoldCommand.Execute(null);

        var styled = editor.Engine.GetDocumentSnapshot().Require().Document;
        Assert.Equal(TextFormat.Bold, styled.Blocks[0].Spans[0].Style.Flags);
        Assert.Equal("Слова 🚀 здесь", styled.ToPlainText());
        Assert.True(editor.IsBold);

        editor.Text = "X" + editor.Text;
        var afterEdit = editor.Engine.GetDocumentSnapshot().Require().Document;
        Assert.Equal("XСлова 🚀 здесь", afterEdit.ToPlainText());
        Assert.StartsWith("XСлова", afterEdit.Blocks[0].Spans[0].Text);
        Assert.True(afterEdit.Blocks[0].Spans[0].Style.Flags.HasFlag(TextFormat.Bold));

        editor.SetEditorSelection(1, 6);
        editor.SelectedFormattingFontFamily = "Georgia";
        editor.ApplySelectedFontFamily();
        editor.SelectedFormattingFontSize = "20";
        editor.ApplySelectedFontSize();
        await editor.SaveCommand.ExecuteAsync(null);

        var saved = provider.GetRequiredService<IProjectService>().GetSceneDocument(sceneId).Require().Document;
        Assert.Equal("XСлова 🚀 здесь", saved.ToPlainText());
        Assert.Contains(saved.Blocks[0].Spans, span =>
            span.Text == "Слова"
            && span.Style.Flags.HasFlag(TextFormat.Bold)
            && span.Style.FontFamily == "Georgia"
            && span.Style.FontSize == 20d);
        Assert.NotEmpty(editor.PreviewBlocks);
    }

    private static string WithoutSceneHeaders(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n").Split('\n')
            .Where(line => !line.StartsWith('\u200B')));
}
