namespace ScriSelfStudio.EditorEngine.Tests;

using ScriSelfStudio.BookArchitecture;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Persistence.Projects;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Поведение сессии редактора: текст, форматирование, история, commit (ТЗ п. 11, 22, 23).
/// </summary>
public sealed class SceneEditorEngineTests
{
    [Fact]
    public async Task Open_exposes_document_revision_and_plain_text()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        var engine = harness.Engine;

        var opened = await engine.OpenSceneAsync(harness.SceneId);

        Assert.True(opened.IsSuccess, opened.Error?.TechnicalDetails);
        Assert.Equal(harness.SceneId, engine.SceneId);
        Assert.Equal(1, engine.BaseRevision);
        Assert.Equal(SampleProjects.SceneText, engine.PlainText);
        Assert.False(engine.IsDirty);
        Assert.False(engine.CanUndo);
        Assert.False(engine.CanRedo);
    }

    [Fact]
    public async Task InsertText_appends_and_marks_dirty()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        var original = harness.Engine.PlainText;

        var result = harness.Engine.InsertText(new TextPosition(original.Length), " Дописано.");

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetails);
        Assert.Equal(original + " Дописано.", harness.Engine.PlainText);
        Assert.True(harness.Engine.IsDirty);
        Assert.True(harness.Engine.CanUndo);
    }

    [Fact]
    public async Task InsertText_inherits_formatting_at_caret()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText, firstLineFormat: TextFormat.Bold);
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        var endOfFirstLine = harness.Engine.PlainText.IndexOf('\n');

        harness.Engine.InsertText(new TextPosition(endOfFirstLine), " жирный");

        var format = harness.Engine.GetFormatting(new TextRange(
            new TextPosition(endOfFirstLine),
            new TextPosition(endOfFirstLine + " жирный".Length)));
        Assert.Equal(TextFormat.Bold, format.Require());
    }

    [Fact]
    public async Task Style_survives_edit_without_applying_it_to_untouched_neighbours()
    {
        // Смежные спаны с одинаковыми флагами, но разными цветами не должны
        // сливаться: сравнение только по TextFormat потеряло бы подсветку
        // (ADR 003, шаг 3.1.5).
        var harness = await Harness.CreateAsync("АБВГДЕ");
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        harness.Engine.ApplyStyle(
            new TextRange(new TextPosition(0), new TextPosition(2)),
            foreground: TextColor.Parse("#FF0000"));
        harness.Engine.ApplyStyle(
            new TextRange(new TextPosition(2), new TextPosition(6)),
            foreground: TextColor.Parse("#00FF00"));

        // Вставка внутри второго цвета.
        harness.Engine.InsertText(new TextPosition(4), "Ж");

        var style = harness.Engine.GetStyle(new TextRange(
            new TextPosition(4), new TextPosition(5))).Require();
        Assert.Equal(TextColor.Parse("#00FF00"), TextColor.Parse(style.Foreground));

        // Первый цвет не расползся на весь абзац.
        var head = harness.Engine.GetStyle(new TextRange(
            new TextPosition(0), new TextPosition(2))).Require();
        Assert.Equal("#FFFF0000", head.Foreground);

        // Текст не пострадал.
        Assert.Equal("АБВГЖДЕ", harness.Engine.PlainText);
    }

    [Fact]
    public async Task GetStyle_reports_mixed_font_as_unset()
    {
        var harness = await Harness.CreateAsync("АБВГДЕ");
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        harness.Engine.ApplyStyle(
            new TextRange(new TextPosition(0), new TextPosition(3)),
            changeFontFamily: true,
            fontFamily: "Consolas");
        harness.Engine.ApplyStyle(
            new TextRange(new TextPosition(3), new TextPosition(6)),
            changeFontFamily: true,
            fontFamily: "Georgia");

        var mixed = harness.Engine.GetStyle(new TextRange(
            new TextPosition(0), new TextPosition(6))).Require();
        Assert.Null(mixed.FontFamily);
        Assert.Equal("Consolas", harness.Engine.GetStyle(new TextRange(
            new TextPosition(0), new TextPosition(3))).Require().FontFamily);
    }

    [Fact]
    public async Task ApplyStyle_keeps_flags_and_undo_restores_previous_colors()
    {
        var harness = await Harness.CreateAsync("реплика");
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        harness.Engine.ApplyFormatting(
            new TextRange(new TextPosition(0), new TextPosition(7)), TextFormat.Bold);

        harness.Engine.ApplyStyle(
            new TextRange(new TextPosition(0), new TextPosition(7)),
            changeFontSize: true,
            fontSize: 18d,
            highlight: TextColor.Parse("#FFFF00"));

        var style = harness.Engine.GetStyle(new TextRange(
            new TextPosition(0), new TextPosition(7))).Require();
        Assert.Equal(TextFormat.Bold, style.Flags);
        Assert.Equal(18d, style.FontSize);
        Assert.Equal("#FFFFFF00", style.Highlight);

        harness.Engine.Undo();

        var afterUndo = harness.Engine.GetStyle(new TextRange(
            new TextPosition(0), new TextPosition(7))).Require();
        Assert.Equal(TextFormat.Bold, afterUndo.Flags);
        Assert.Null(afterUndo.FontSize);
        Assert.Equal(string.Empty, afterUndo.Highlight);
    }

    [Fact]
    public async Task ApplyStyle_requires_selection_and_valid_size()
    {
        var harness = await Harness.CreateAsync("реплика");
        await harness.Engine.OpenSceneAsync(harness.SceneId);

        var empty = harness.Engine.ApplyStyle(
            new TextRange(new TextPosition(3), new TextPosition(3)),
            foreground: TextColor.Parse("#FF0000"));
        Assert.False(empty.IsSuccess);

        var tooBig = harness.Engine.ApplyStyle(
            new TextRange(new TextPosition(0), new TextPosition(3)),
            changeFontSize: true,
            fontSize: 900d);
        Assert.False(tooBig.IsSuccess);
    }

    [Fact]
    public async Task Inserting_newline_splits_the_block()
    {
        var harness = await Harness.CreateAsync("Абзац целиком");
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        var original = harness.Engine.PlainText;

        harness.Engine.InsertText(new TextPosition(5), "\n");

        Assert.Equal(original[..5] + "\n" + original[5..], harness.Engine.PlainText);
        var document = harness.Engine.CreateCommitRequest().Require().Document;
        Assert.Equal(2, document.Blocks.Count);
    }

    [Fact]
    public async Task Delete_across_blocks_merges_text_without_loss()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        var original = harness.Engine.PlainText;
        var start = original.IndexOf('\n') - 2;
        const int length = 5;

        var result = harness.Engine.DeleteRange(new TextRange(
            new TextPosition(start),
            new TextPosition(start + length)));

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetails);
        Assert.Equal(original.Remove(start, length), harness.Engine.PlainText);
    }

    [Fact]
    public async Task Undo_and_redo_restore_text_and_dirty_state()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        var original = harness.Engine.PlainText;
        harness.Engine.InsertText(new TextPosition(0), "Начало. ");
        Assert.True(harness.Engine.IsDirty);

        Assert.True(harness.Engine.Undo().IsSuccess);

        Assert.Equal(original, harness.Engine.PlainText);
        Assert.False(harness.Engine.IsDirty);
        Assert.True(harness.Engine.CanRedo);

        Assert.True(harness.Engine.Redo().IsSuccess);

        Assert.Equal("Начало. " + original, harness.Engine.PlainText);
        Assert.True(harness.Engine.IsDirty);
    }

    [Fact]
    public async Task Undo_without_history_reports_error()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);

        var result = harness.Engine.Undo();

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.EditorOperationFailed, result.Error!.Code);
    }

    [Fact]
    public async Task ReplaceRange_replaces_selection_in_one_undo_step()
    {
        var harness = await Harness.CreateAsync("Первая строка рукописи.");
        await harness.Engine.OpenSceneAsync(harness.SceneId);

        var result = harness.Engine.ReplaceRange(
            new TextRange(new TextPosition(0), new TextPosition(6)),
            "Вторая");

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetails);
        Assert.Equal("Вторая строка рукописи.", harness.Engine.PlainText);

        Assert.True(harness.Engine.Undo().IsSuccess);
        Assert.Equal("Первая строка рукописи.", harness.Engine.PlainText);
    }

    [Fact]
    public async Task ReplaceRange_keeps_surrounding_formatting()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText, firstLineFormat: TextFormat.Bold);
        await harness.Engine.OpenSceneAsync(harness.SceneId);

        harness.Engine.ReplaceRange(new TextRange(new TextPosition(0), new TextPosition(6)), "Начало");

        var format = harness.Engine.GetFormatting(new TextRange(new TextPosition(0), new TextPosition(6)));
        Assert.Equal(TextFormat.Bold, format.Require());
        Assert.StartsWith("Начало", harness.Engine.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyFormatting_sets_flags_and_second_call_toggles_them_off()
    {
        var harness = await Harness.CreateAsync("Форматирование текста");
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        var range = new TextRange(new TextPosition(0), new TextPosition(13));

        Assert.True(harness.Engine.ApplyFormatting(range, TextFormat.Bold | TextFormat.Italic).IsSuccess);
        Assert.Equal(TextFormat.Bold | TextFormat.Italic, harness.Engine.GetFormatting(range).Require());

        Assert.True(harness.Engine.ApplyFormatting(range, TextFormat.Bold | TextFormat.Italic).IsSuccess);
        Assert.Equal(TextFormat.None, harness.Engine.GetFormatting(range).Require());
    }

    [Fact]
    public async Task ApplyFormatting_keeps_text_and_is_undoable()
    {
        var harness = await Harness.CreateAsync("Форматирование");
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        var range = new TextRange(new TextPosition(0), new TextPosition(4));

        harness.Engine.ApplyFormatting(range, TextFormat.Underline);
        Assert.Equal("Форматирование", harness.Engine.PlainText);
        Assert.True(harness.Engine.IsDirty);

        Assert.True(harness.Engine.Undo().IsSuccess);

        Assert.Equal(TextFormat.None, harness.Engine.GetFormatting(range).Require());
        Assert.False(harness.Engine.IsDirty);
    }

    [Fact]
    public async Task ApplyFormatting_on_empty_range_is_rejected()
    {
        var harness = await Harness.CreateAsync("Текст");
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        var caret = new TextRange(new TextPosition(2), new TextPosition(2));

        var result = harness.Engine.ApplyFormatting(caret, TextFormat.Bold);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ApplyBlockStyle_sets_kind_alignment_and_indent()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        var document = harness.Projects.GetSceneDocument(harness.SceneId).Require().Document;
        var firstLineLength = document.Blocks[0].Length;
        var range = new TextRange(new TextPosition(0), new TextPosition(firstLineLength));

        var result = harness.Engine.ApplyBlockStyle(
            range,
            kind: BlockKind.Heading1,
            alignment: TextAlignment.Center,
            indentLevel: 1);

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetails);
        var style = harness.Engine.GetBlockStyleAt(new TextPosition(0)).Require();
        Assert.Equal(BlockKind.Heading1, style.Kind);
        Assert.Equal(TextAlignment.Center, style.Alignment);
        Assert.Equal(1, style.IndentLevel);
        Assert.Equal(2, harness.Engine.CreateCommitRequest().Require().Document.Blocks.Count);
    }

    [Fact]
    public async Task ApplyBlockStyle_only_touches_blocks_in_range()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        var document = harness.Projects.GetSceneDocument(harness.SceneId).Require().Document;
        var firstLineLength = document.Blocks[0].Length;
        var secondLineStart = firstLineLength + 1;

        harness.Engine.ApplyBlockStyle(
            new TextRange(new TextPosition(secondLineStart), new TextPosition(secondLineStart + 3)),
            kind: BlockKind.Quote);

        Assert.Equal(BlockKind.Paragraph, harness.Engine.GetBlockStyleAt(new TextPosition(0)).Require().Kind);
        Assert.Equal(BlockKind.Quote, harness.Engine.GetBlockStyleAt(new TextPosition(secondLineStart)).Require().Kind);
    }

    [Fact]
    public async Task Search_reports_matches_with_previews()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);

        var result = harness.Engine.Search("строка");

        var search = result.Require();
        Assert.Equal(2, search.TotalCount);
        Assert.All(search.Matches, match => Assert.Contains("строка", match.Preview, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Statistics_count_words_and_graphemes_with_emoji()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);

        var statistics = harness.Engine.Statistics;

        Assert.Equal(9, statistics.Words);
        Assert.Equal(2, statistics.Paragraphs);
        Assert.True(statistics.GraphemeClusters < statistics.Characters);
        Assert.Equal(harness.Engine.PlainText.Length, statistics.Characters);
    }

    [Fact]
    public async Task Commit_writes_document_and_bumps_revision()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        harness.Engine.InsertText(new TextPosition(0), "Эпиграф. ");

        var committed = await harness.Engine.CommitAsync();

        Assert.True(committed.IsSuccess, committed.Error?.TechnicalDetails);
        Assert.Equal(2, harness.Engine.BaseRevision);
        Assert.False(harness.Engine.IsDirty);
        Assert.Equal("Эпиграф. " + SampleProjects.SceneText, harness.Engine.PlainText);
        Assert.Equal(
            "Эпиграф. " + SampleProjects.SceneText,
            harness.Projects.GetSceneDocument(harness.SceneId).Require().Document.ToPlainText());
        Assert.True(harness.Projects.IsDirty);
    }

    [Fact]
    public async Task Commit_reports_conflict_when_revision_is_stale()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        harness.Engine.InsertText(new TextPosition(0), "Правка. ");
        var stale = harness.Engine.CreateCommitRequest().Require();
        await harness.Engine.CommitAsync();

        var second = await harness.Projects.UpdateSceneDocumentAsync(new UpdateSceneDocumentRequest
        {
            SceneId = stale.SceneId,
            BaseRevision = stale.BaseRevision,
            Document = stale.Document,
        });

        Assert.False(second.IsSuccess);
        Assert.Equal(ErrorCode.EditorOperationConflict, second.Error!.Code);
    }

    [Fact]
    public async Task Commit_without_changes_keeps_revision()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);

        var committed = await harness.Engine.CommitAsync();

        Assert.True(committed.IsSuccess);
        Assert.Equal(1, harness.Engine.BaseRevision);
        Assert.False(harness.Projects.IsDirty);
    }

    [Fact]
    public async Task Operations_require_an_open_scene()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);

        Assert.Equal(ErrorCode.EditorOperationFailed, harness.Engine.InsertText(new TextPosition(0), "x").Error!.Code);
        Assert.Equal(ErrorCode.EditorOperationFailed, harness.Engine.DeleteRange(new TextRange(new TextPosition(0), new TextPosition(1))).Error!.Code);
        Assert.Equal(ErrorCode.EditorOperationFailed, harness.Engine.Undo().Error!.Code);
        Assert.Equal(ErrorCode.EditorOperationFailed, harness.Engine.Search("x").Error!.Code);
        Assert.Equal(ErrorCode.EditorOperationFailed, harness.Engine.CreateCommitRequest().Error!.Code);
        Assert.Equal(ErrorCode.EditorOperationFailed, (await harness.Engine.CommitAsync()).Error!.Code);
        Assert.Null(harness.Engine.SceneId);
        Assert.Equal(string.Empty, harness.Engine.PlainText);
        Assert.Equal(0, harness.Engine.Statistics.Words);
    }

    [Fact]
    public async Task Out_of_bounds_edits_are_rejected()
    {
        var harness = await Harness.CreateAsync("Короткий текст");
        await harness.Engine.OpenSceneAsync(harness.SceneId);

        Assert.Equal(
            ErrorCode.EditorOperationFailed,
            harness.Engine.InsertText(new TextPosition(500), "x").Error!.Code);
        Assert.Equal(
            ErrorCode.EditorOperationFailed,
            harness.Engine.DeleteRange(new TextRange(new TextPosition(0), new TextPosition(500))).Error!.Code);
        Assert.Equal("Короткий текст", harness.Engine.PlainText);
    }

    [Fact]
    public async Task Close_clears_the_session()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        harness.Engine.InsertText(new TextPosition(0), "x");

        var closed = await harness.Engine.CloseAsync();

        Assert.True(closed.IsSuccess);
        Assert.Null(harness.Engine.SceneId);
        Assert.False(harness.Engine.IsDirty);
        Assert.False(harness.Engine.CanUndo);
        Assert.Equal(string.Empty, harness.Engine.PlainText);
    }

    [Fact]
    public async Task Text_with_emoji_and_cyrillic_survives_commit_save_and_reopen()
    {
        var harness = await Harness.CreateAsync("Заготовка");
        await harness.Engine.OpenSceneAsync(harness.SceneId);
        const string added = "Ёжик 🚀 и пара e\u0301 с комбинируемой ударной.";
        harness.Engine.InsertText(new TextPosition(harness.Engine.PlainText.Length), " " + added);
        var expected = harness.Engine.PlainText;
        Assert.True((await harness.Engine.CommitAsync()).IsSuccess);
        Assert.True((await harness.Projects.SaveAsync()).IsSuccess);

        await harness.Engine.CloseAsync();
        var reopened = await harness.Engine.OpenSceneAsync(harness.SceneId);

        Assert.True(reopened.IsSuccess, reopened.Error?.TechnicalDetails);
        Assert.Equal(expected, harness.Engine.PlainText);
        Assert.False(harness.Engine.IsDirty);
        Assert.Equal(2, harness.Engine.BaseRevision);
    }

    [Fact]
    public async Task Editor_document_changed_event_is_published()
    {
        var harness = await Harness.CreateAsync(SampleProjects.SceneText);
        var changes = new List<Contracts.Events.EditorDocumentChanged>();
        using var subscription = harness.Events.Subscribe<Contracts.Events.EditorDocumentChanged>(changes.Add);
        await harness.Engine.OpenSceneAsync(harness.SceneId);

        harness.Engine.InsertText(new TextPosition(0), "x");

        Assert.True(changes.Count >= 2);
        Assert.Equal(harness.SceneId, changes[^1].SceneId);
        Assert.True(changes[^1].IsDirty);
    }

    private sealed class Harness
    {
        private Harness(ProjectService projects, SceneEditorEngine engine, EventBus events, Guid sceneId)
        {
            Projects = projects;
            Engine = engine;
            Events = events;
            SceneId = sceneId;
        }

        public ProjectService Projects { get; }

        public SceneEditorEngine Engine { get; }

        public EventBus Events { get; }

        public Guid SceneId { get; }

        public static async Task<Harness> CreateAsync(string sceneText, TextFormat firstLineFormat = TextFormat.None)
        {
            var clock = new FakeClock();
            var events = new EventBus();
            var projects = new ProjectService(new InMemoryProjectStore(), new InMemoryRecoveryStore(clock), events, clock);
            var path = Path.Combine(Path.GetTempPath(), "scriself-engine-tests", Guid.NewGuid().ToString("N") + ".scri");
            var created = await projects.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Роман" });
            Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);

            var architecture = new BookArchitectureService(projects, clock);
            await architecture.CreateBookAsync(new CreateBookRequest { SeriesId = projects.Current!.Series[0].Id, Title = "Книга" });
            var bookId = projects.Current!.Series[0].Books[0].Id;
            await architecture.CreateChapterAsync(new CreateChapterRequest { BookId = bookId, Title = "Глава" });
            var chapterId = projects.Current!.Series[0].Books[0].Chapters[0].Id;
            await architecture.CreateSceneAsync(new CreateSceneRequest { ChapterId = chapterId, Title = "Сцена" });
            var sceneId = projects.Current!.Series[0].Books[0].Chapters[0].Scenes[0].Id;

            var seed = await projects.UpdateSceneDocumentAsync(new UpdateSceneDocumentRequest
            {
                SceneId = sceneId,
                BaseRevision = 0,
                Document = SampleProjects.DocumentWithText(sceneText, firstLineFormat),
            });
            Assert.True(seed.IsSuccess, seed.Error?.TechnicalDetails);
            Assert.True((await projects.SaveAsync()).IsSuccess);

            return new Harness(projects, new SceneEditorEngine(projects, events), events, sceneId);
        }
    }
}
