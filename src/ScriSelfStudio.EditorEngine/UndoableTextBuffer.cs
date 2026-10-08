namespace ScriSelfStudio.EditorEngine;

using ScriSelfStudio.Contracts.Errors;

/// <summary>
/// Буфер с единственной историей отмены (ТЗ п. 22: два независимых стека недопустимы).
/// </summary>
public sealed class UndoableTextBuffer
{
    public const int DefaultHistoryLimit = 200;

    private readonly List<TextEdit> _undo = [];
    private readonly List<TextEdit> _redo = [];
    private readonly int _historyLimit;

    public UndoableTextBuffer(string? text = null, int historyLimit = DefaultHistoryLimit)
    {
        Buffer = new TextBuffer(text);
        _historyLimit = Math.Max(1, historyLimit);
    }

    public TextBuffer Buffer { get; }

    public string Text => Buffer.Text;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoDepth => _undo.Count;

    public int RedoDepth => _redo.Count;

    public Result Insert(int offset, string text) => Execute(offset, text, offset, 0);

    public Result Delete(int offset, int length) => Execute(offset, string.Empty, offset, length);

    public Result Undo()
    {
        if (_undo.Count == 0)
        {
            return Result.FromError(ErrorCode.EditorOperationFailed, "Отменять нечего.");
        }

        var edit = _undo[^1];
        var applied = edit.ApplyBackward(Buffer);
        if (!applied.IsSuccess)
        {
            return applied;
        }

        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(edit);
        return Result.Success;
    }

    public Result Redo()
    {
        if (_redo.Count == 0)
        {
            return Result.FromError(ErrorCode.EditorOperationFailed, "Повторять нечего.");
        }

        var edit = _redo[^1];
        var applied = edit.ApplyForward(Buffer);
        if (!applied.IsSuccess)
        {
            return applied;
        }

        _redo.RemoveAt(_redo.Count - 1);
        Push(_undo, edit);
        return Result.Success;
    }

    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private Result Execute(int offset, string insertedText, int removedOffset, int removedLength)
    {
        var validation = Buffer.ValidateRange(removedOffset, removedLength);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        var removed = removedLength == 0 ? string.Empty : Buffer.Slice(removedOffset, removedLength);
        if (insertedText.Length == 0 && removed.Length == 0)
        {
            return Result.Success;
        }

        var applied = Buffer.Replace(offset, removedLength, insertedText);
        if (!applied.IsSuccess)
        {
            return applied;
        }

        Push(_undo, new TextEdit(offset, removed, insertedText));
        _redo.Clear();
        return Result.Success;
    }

    private void Push(List<TextEdit> history, TextEdit edit)
    {
        history.Add(edit);
        while (history.Count > _historyLimit)
        {
            history.RemoveAt(0);
        }
    }

    private readonly record struct TextEdit(int Offset, string Removed, string Inserted)
    {
        public Result ApplyForward(TextBuffer buffer) => buffer.Replace(Offset, Removed.Length, Inserted);

        public Result ApplyBackward(TextBuffer buffer) => buffer.Replace(Offset, Inserted.Length, Removed);
    }
}
