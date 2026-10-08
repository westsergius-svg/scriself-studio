namespace ScriSelfStudio.EditorEngine;

using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Events;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Text;

/// <summary>
/// Сессия редактирования одной сцены (ТЗ п. 11, 22). Владеет канонической моделью
/// документа и единственной историей отмены; запись в проект выполняет commit.
/// </summary>
public sealed class SceneEditorEngine : IEditorEngine
{
    public const int HistoryLimit = 200;

    private readonly IProjectService _projects;
    private readonly IEventBus _events;
    private readonly List<IDocumentChange> _undo = [];
    private readonly List<IDocumentChange> _redo = [];

    private RichDocument? _document;
    private RichDocument? _cleanSnapshot;
    private long _baseRevision;

    public SceneEditorEngine(IProjectService projects, IEventBus events)
    {
        _projects = projects;
        _events = events;
    }

    public Guid? SceneId { get; private set; }

    public long BaseRevision => _baseRevision;

    public bool IsDirty => _document is not null &&
        _cleanSnapshot is not null &&
        !DocumentEditor.IsEquivalent(_document, _cleanSnapshot);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string PlainText => _document?.ToPlainText() ?? string.Empty;

    public TextStatisticsDto Statistics => _document is null
        ? new TextStatisticsDto
        {
            Characters = 0,
            CharactersWithoutSpaces = 0,
            Words = 0,
            Paragraphs = 0,
            GraphemeClusters = 0,
        }
        : TextStatisticsCalculator.Calculate(_document);

    public Task<Result<SceneDocumentDto>> OpenSceneAsync(Guid sceneId, CancellationToken cancellationToken = default)
    {
        var loaded = _projects.GetSceneDocument(sceneId);
        if (!loaded.IsSuccess)
        {
            return Task.FromResult(Result<SceneDocumentDto>.Failure(loaded.Error!));
        }

        var dto = loaded.Require();
        SetDocument(sceneId, dto.Document, dto.Revision);
        PublishDocumentChanged();
        return Task.FromResult(Result<SceneDocumentDto>.Success(Snapshot()));
    }

    public Task<Result> CloseAsync(CancellationToken cancellationToken = default)
    {
        _document = null;
        _cleanSnapshot = null;
        SceneId = null;
        _baseRevision = 0;
        _undo.Clear();
        _redo.Clear();
        return Task.FromResult(Result.Success);
    }

    public Result InsertText(TextPosition position, string text) => ReplaceRange(TextRange.Empty(position), text);

    public Result DeleteRange(TextRange range) => ReplaceRange(range, string.Empty);

    public Result ReplaceRange(TextRange range, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return open;
        }

        var start = Math.Min(range.Start.Offset, range.End.Offset);
        var end = Math.Max(range.Start.Offset, range.End.Offset);
        var validation = ValidateRange(document!, start, end - start);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        if (start == end && text.Length == 0)
        {
            return Result.Success;
        }

        var removed = document!.ToPlainText().Substring(start, end - start);
        var before = document!.Clone();
        ApplyChange(new TextChange(start, removed, text, before));
        return Result.Success;
    }

    public Result ApplyFormatting(TextRange range, TextFormat format)
    {
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return open;
        }

        if (format == TextFormat.None)
        {
            return Result.FromError(ErrorCode.ValidationFailed, "Не выбрано форматирование.");
        }

        var start = Math.Min(range.Start.Offset, range.End.Offset);
        var end = Math.Max(range.Start.Offset, range.End.Offset);
        var validation = ValidateRange(document!, start, end - start);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        if (start == end)
        {
            return Result.FromError(ErrorCode.EditorOperationFailed, "Выделите текст для форматирования.");
        }

        var current = DocumentEditor.GetFormatting(document!, start, end);
        var set = (current.Flags & format) != format;
        var before = document!.Clone();
        Func<SpanStyle, SpanStyle> transform = set
            ? AddFormat
            : RemoveFormat;
        DocumentEditor.ApplyFormatting(document!, start, end, transform);
        ApplyChange(new SnapshotChange(before, document!.Clone()));
        return Result.Success;

        // Переключаются только символьные флаги: шрифт и цвета меняются
        // отдельными операциями, иначе панель форматирования не смогла бы
        // отличить «переключить жирный» от «задать цвет» (ADR 004 п. 5).
        SpanStyle AddFormat(SpanStyle value) => value.WithFlags(value.Flags | format);

        SpanStyle RemoveFormat(SpanStyle value) => value.WithFlags(value.Flags & ~format);
    }

    /// <summary>
    /// Задаёт оформление диапазона: шрифт, кегль и цвета. Операция перезаписывает
    /// указанные поля, оставляя остальные (флаги) нетронутыми, поэтому панель
    /// может вызывать её независимо от переключателей начертания.
    /// </summary>
    public Result ApplyStyle(
        TextRange range,
        string? fontFamily = null,
        bool changeFontFamily = false,
        double? fontSize = null,
        bool changeFontSize = false,
        TextColor? foreground = null,
        TextColor? highlight = null)
    {
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return open;
        }

        if (!changeFontFamily && !changeFontSize && foreground is null && highlight is null)
        {
            return Result.FromError(ErrorCode.ValidationFailed, "Не задано оформление.");
        }

        if (fontSize is < 1 or > 400)
        {
            return Result.FromError(ErrorCode.ValidationFailed, "Кегль должен быть от 1 до 400.");
        }

        var start = Math.Min(range.Start.Offset, range.End.Offset);
        var end = Math.Max(range.Start.Offset, range.End.Offset);
        var validation = ValidateRange(document!, start, end - start);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        if (start == end)
        {
            return Result.FromError(ErrorCode.EditorOperationFailed, "Выделите текст для форматирования.");
        }

        var before = document!.Clone();
        DocumentEditor.ApplyFormatting(document!, start, end, value => value with
        {
            FontFamily = changeFontFamily ? fontFamily : value.FontFamily,
            FontSize = changeFontSize ? fontSize : value.FontSize,
            Foreground = foreground ?? value.Foreground,
            Highlight = highlight ?? value.Highlight,
        });
        ApplyChange(new SnapshotChange(before, document!.Clone()));
        return Result.Success;
    }

    /// <summary>Оформление диапазона для панели форматирования.</summary>
    public Result<SpanStyleDto> GetStyle(TextRange range)
    {
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return Result<SpanStyleDto>.Failure(open.Error!);
        }

        var start = Math.Min(range.Start.Offset, range.End.Offset);
        var end = Math.Max(range.Start.Offset, range.End.Offset);
        var validation = ValidateRange(document!, start, end - start);
        if (!validation.IsSuccess)
        {
            return Result<SpanStyleDto>.Failure(validation.Error!);
        }

        var style = DocumentEditor.GetFormatting(document!, start, end);
        return Result<SpanStyleDto>.Success(new SpanStyleDto
        {
            Flags = style.Flags,
            FontFamily = style.FontFamily,
            FontSize = style.FontSize,
            Foreground = ToHexOrEmpty(style.Foreground),
            Highlight = ToHexOrEmpty(style.Highlight),
        });
    }

    // Незаданный цвет должен доезжать до панели пустой строкой, а не
    // «#00000000»: иначе кнопка подсветки показывала бы чёрный цвет там,
    // где оформления нет, и снятие цвета стало бы неотличимым от его выбора.
    private static string ToHexOrEmpty(TextColor color) => color.IsSet ? color.ToHex() : string.Empty;

    public Result<TextFormat> GetFormatting(TextRange range)
    {
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return Result<TextFormat>.Failure(open.Error!);
        }

        var start = Math.Min(range.Start.Offset, range.End.Offset);
        var end = Math.Max(range.Start.Offset, range.End.Offset);
        var validation = ValidateRange(document!, start, end - start);
        if (!validation.IsSuccess)
        {
            return Result<TextFormat>.Failure(validation.Error!);
        }

        // Панель начертания интересуют только символьные флаги; шрифт и цвета
        // приходят отдельной операцией GetStyle.
        return Result<TextFormat>.Success(DocumentEditor.GetFormatting(document!, start, end).Flags);
    }

    public Result<BlockStyleDto> GetBlockStyleAt(TextPosition position)
    {
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return Result<BlockStyleDto>.Failure(open.Error!);
        }

        var validation = ValidateRange(document!, position.Offset, 0);
        if (!validation.IsSuccess)
        {
            return Result<BlockStyleDto>.Failure(validation.Error!);
        }

        var block = DocumentEditor.BlockAt(document!, position.Offset);
        return Result<BlockStyleDto>.Success(new BlockStyleDto
        {
            Kind = block.Kind,
            Alignment = block.Alignment,
            IndentLevel = block.IndentLevel,
        });
    }

    public Result ApplyBlockStyle(TextRange range, BlockKind? kind = null, TextAlignment? alignment = null, int? indentLevel = null)
    {
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return open;
        }

        if (kind is null && alignment is null && indentLevel is null)
        {
            return Result.Success;
        }

        var start = Math.Min(range.Start.Offset, range.End.Offset);
        var end = Math.Max(range.Start.Offset, range.End.Offset);
        var validation = ValidateRange(document!, start, end - start);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        if (indentLevel is < 0 or > 8)
        {
            return Result.FromError(ErrorCode.ValidationFailed, "Отступ должен быть от 0 до 8.");
        }

        var before = document!.Clone();
        foreach (var block in DocumentEditor.BlocksInRange(document!, start, end))
        {
            if (kind is not null)
            {
                block.Kind = kind.Value;
            }

            if (alignment is not null)
            {
                block.Alignment = alignment.Value;
            }

            if (indentLevel is not null)
            {
                block.IndentLevel = indentLevel.Value;
            }
        }

        ApplyChange(new SnapshotChange(before, document!.Clone()));
        return Result.Success;
    }

    public Result Undo()
    {
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return open;
        }

        if (_undo.Count == 0)
        {
            return Result.FromError(ErrorCode.EditorOperationFailed, "Отменять нечего.");
        }

        var change = _undo[^1];
        change.Revert(document!);
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(change);
        PublishDocumentChanged();
        return Result.Success;
    }

    public Result Redo()
    {
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return open;
        }

        if (_redo.Count == 0)
        {
            return Result.FromError(ErrorCode.EditorOperationFailed, "Повторять нечего.");
        }

        var change = _redo[^1];
        change.Apply(document!);
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(change);
        PublishDocumentChanged();
        return Result.Success;
    }

    /// <summary>
    /// Заменяет документ сцены плоским текстом: строки становятся абзацами.
    /// История отмены движка не затрагивается: её ведёт элемент ввода (ТЗ п. 22).
    /// </summary>
    public Result SetPlainText(string text)
    {
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return open;
        }

        var rebuilt = CreatePlainDocument(text);
        if (DocumentEditor.IsEquivalent(document!, rebuilt))
        {
            return Result.Success;
        }

        document!.Blocks.Clear();
        foreach (var block in rebuilt.Blocks)
        {
            document.Blocks.Add(block);
        }

        PublishDocumentChanged();
        return Result.Success;
    }

    /// <summary>Строит документ из простого текста: перевод строки разделяет абзацы.</summary>
    private static RichDocument CreatePlainDocument(string text)
    {
        var document = new RichDocument();
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var block = new TextBlock();
            if (line.Length > 0)
            {
                block.Spans.Add(new TextSpan { Text = line });
            }

            document.Blocks.Add(block);
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new TextBlock());
        }

        return document;
    }
    public Result<SearchResult> Search(string query, SearchOptions? options = null)
    {
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return Result<SearchResult>.Failure(open.Error!);
        }

        return Result<SearchResult>.Success(TextSearchService.Search(document!.ToPlainText(), query, options));
    }

    public Result<UpdateSceneDocumentRequest> CreateCommitRequest()
    {
        var open = EnsureOpen(out var document);
        if (!open.IsSuccess)
        {
            return Result<UpdateSceneDocumentRequest>.Failure(open.Error!);
        }

        return Result<UpdateSceneDocumentRequest>.Success(new UpdateSceneDocumentRequest
        {
            SceneId = SceneId!.Value,
            BaseRevision = _baseRevision,
            Document = document!.Clone(),
        });
    }

    public async Task<Result<SceneDocumentDto>> CommitAsync(CancellationToken cancellationToken = default)
    {
        var request = CreateCommitRequest();
        if (!request.IsSuccess)
        {
            return Result<SceneDocumentDto>.Failure(request.Error!);
        }

        if (!IsDirty)
        {
            return Result<SceneDocumentDto>.Success(Snapshot());
        }

        var updated = await _projects.UpdateSceneDocumentAsync(request.Require(), cancellationToken).ConfigureAwait(true);
        if (!updated.IsSuccess)
        {
            return updated;
        }

        _baseRevision = updated.Require().Revision;
        _cleanSnapshot = _document!.Clone();
        PublishDocumentChanged();
        return updated;
    }

    private void SetDocument(Guid sceneId, RichDocument document, long revision)
    {
        SceneId = sceneId;
        _document = document.Clone();
        DocumentEditor.Normalize(_document!);
        _cleanSnapshot = _document!.Clone();
        _baseRevision = revision;
        _undo.Clear();
        _redo.Clear();
    }

    private SceneDocumentDto Snapshot() => new()
    {
        SceneId = SceneId!.Value,
        Revision = _baseRevision,
        Document = _document!.Clone(),
    };

    private void ApplyChange(IDocumentChange change)
    {
        var document = _document!;
        change.Apply(document);

        _undo.Add(change);
        while (_undo.Count > HistoryLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        PublishDocumentChanged();
    }

    private Result EnsureOpen(out RichDocument? document)
    {
        document = _document;
        return SceneId is not null && _document is not null
            ? Result.Success
            : Result.FromError(ErrorCode.EditorOperationFailed, "Сцена не открыта в редакторе.");
    }

    private Result ValidateRange(RichDocument document, int offset, int length)
    {
        var total = document.ToPlainText().Length;
        if (offset < 0 || offset > total)
        {
            return Result.FromError(ErrorCode.EditorOperationFailed, "Смещение выходит за пределы текста.");
        }

        if (length < 0 || offset + length > total)
        {
            return Result.FromError(ErrorCode.EditorOperationFailed, "Длина фрагмента выходит за пределы текста.");
        }

        return Result.Success;
    }

    private void PublishDocumentChanged()
    {
        if (SceneId is null)
        {
            return;
        }

        _events.Publish(new EditorDocumentChanged(SceneId.Value, _baseRevision, IsDirty));
    }
}