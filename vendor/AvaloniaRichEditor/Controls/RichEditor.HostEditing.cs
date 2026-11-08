using System;
using System.Linq;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

public partial class RichEditor
{
    /// <summary>The current selection in paragraph coordinates, independent of rendered pages.</summary>
    public (Paragraph? Start, int StartOffset, Paragraph? End, int EndOffset) GetHostSelection()
    {
        var start = _selectionStart;
        var end = _selectionEnd;
        if (start.CompareTo(end) > 0) (start, end) = (end, start);
        return (start.Paragraph, start.Offset, end.Paragraph, end.Offset);
    }

    public void SelectHostRange(Paragraph start, int startOffset, Paragraph end, int endOffset)
    {
        if (!GetAllParagraphsInOrder().Contains(start) || !GetAllParagraphsInOrder().Contains(end)) return;
        _selectionStart = new TextPointer(start, startOffset);
        _selectionEnd = new TextPointer(end, endOffset);
        _caretPosition = new TextPointer(end, endOffset);
        ResetCaretBlink();
    }

    /// <summary>Apply a host edit with one normal undo checkpoint, preserving the caret where possible.</summary>
    public void ApplyHostDocument(FlowDocument document)
    {
        if (IsReadOnly || Document == null) return;
        var index = GetAllParagraphsInOrder().ToList().IndexOf(_caretPosition.Paragraph!);
        var offset = _caretPosition.Offset;
        PushUndo();
        Document = document;
        var paragraphs = GetAllParagraphsInOrder().ToList();
        var paragraph = paragraphs.Count == 0 ? null : paragraphs[Math.Clamp(index, 0, paragraphs.Count - 1)];
        _caretPosition = new TextPointer(paragraph, Math.Min(offset, paragraph?.Inlines.OfType<Run>().Sum(r => r.Text?.Length ?? 0) ?? 0));
        CollapseSelectionToCaret();
        MarkTextChanged();
        ResetCaretBlink();
    }
}
