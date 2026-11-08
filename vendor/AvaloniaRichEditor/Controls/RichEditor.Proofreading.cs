// ScriSelf extension to AvaloniaRichEditor 1.2.1. Purely visual annotations;
// no spell-check state is written to the document or export.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

public sealed record ProofreadingSpan(Paragraph Paragraph, int Start, string Text);

public sealed class TextContextMenuEventArgs(ContextMenu menu, TextPointer position) : EventArgs
{
    public ContextMenu Menu { get; } = menu;
    public TextPointer Position { get; } = position;
}

public partial class RichEditor
{
    private IReadOnlyDictionary<Paragraph, ProofreadingSpan[]> _proofreading = new Dictionary<Paragraph, ProofreadingSpan[]>();
    private static readonly Pen ProofreadingPen = new(new SolidColorBrush(Color.Parse("#D64550")), 1.1);

    public event EventHandler<TextContextMenuEventArgs>? TextContextMenuOpening;

    public void SetProofreadingSpans(IEnumerable<ProofreadingSpan> spans)
    {
        _proofreading = spans.GroupBy(s => s.Paragraph).ToDictionary(g => g.Key, g => g.ToArray());
        InvalidateVisual();
    }

    public TextPointer GetCaretPosition() => new(_caretPosition.Paragraph, _caretPosition.Offset);

    public bool HasTextSelection => _selectionStart.CompareTo(_selectionEnd) != 0;

    public bool CompleteSentenceWord(int start, string expected)
    {
        var p = _caretPosition.Paragraph;
        if (IsReadOnly || HasTextSelection || p is null || expected.Length == 0 || !char.IsLower(expected[0])) return false;
        var text = BuildPlain(p);
        int end = start + expected.Length;
        if (start < 0 || end != _caretPosition.Offset || end > text.Length || text.Substring(start, expected.Length) != expected) return false;
        PushUndo();
        ReplaceProofreadingText(p, start, expected[..1], char.ToUpperInvariant(expected[0]).ToString());
        _caretPosition = new TextPointer(p, end);
        CollapseSelectionToCaret();
        TryInsertTextCore(p, " ", end);
        _caretPosition.Offset++;
        CollapseSelectionToCaret();
        ResetCaretBlink();
        NotifyStatus();
        InvalidateVisual();
        return true;
    }

    public static string GetParagraphText(Paragraph paragraph) => BuildPlain(paragraph);

    public bool ReplaceProofreadingWord(ProofreadingSpan span, string replacement)
    {
        if (IsReadOnly || Document is null || !GetAllParagraphsInOrder().Contains(span.Paragraph)) return false;
        string text = BuildPlain(span.Paragraph);
        if (span.Start < 0 || span.Start + span.Text.Length > text.Length
            || !text.AsSpan(span.Start, span.Text.Length).SequenceEqual(span.Text)) return false;
        if (span.Text == replacement) return true;
        PushUndo();
        ReplaceProofreadingText(span.Paragraph, span.Start, span.Text, replacement);
        ResetCaretBlink();
        NotifyStatus();
        InvalidateVisual();
        return true;
    }

    private void ReplaceProofreadingText(Paragraph paragraph, int start, string original, string replacement)
    {
        // Keep the unchanged prefix/suffix and their formatting. New letters inherit
        // the replaced text's style, not that of a neighbouring space or punctuation.
        int prefix = 0, suffix = 0;
        while (prefix < original.Length && prefix < replacement.Length && original[prefix] == replacement[prefix]) prefix++;
        while (suffix < original.Length - prefix && suffix < replacement.Length - prefix
            && original[^(suffix + 1)] == replacement[^(suffix + 1)]) suffix++;
        var style = RunAtOffset(paragraph, start + Math.Min(prefix, Math.Max(0, original.Length - 1)));
        var inserted = (Run)(style?.Clone() ?? new Run());
        inserted.Text = replacement.Substring(prefix, replacement.Length - prefix - suffix);
        SelectMatch(paragraph, start + prefix, original.Length - prefix - suffix);
        if (inserted.Text.Length > 0) InsertInlines(new List<Inline> { inserted });
        else DeleteSelection();
        TextRange.CoalesceRuns(paragraph);
        _caretPosition = new TextPointer(paragraph, start + replacement.Length);
        CollapseSelectionToCaret();
        MarkTextChanged();
    }

    private void DrawProofreading(DrawingContext context, Paragraph paragraph, TextLayout layout, double x, double y)
    {
        if (string.IsNullOrEmpty(_preeditText)) DrawReviewHighlights(context, paragraph, layout, x, y);
        if (string.IsNullOrEmpty(_preeditText)) DrawGrammar(context, paragraph, layout, x, y);
        if (!string.IsNullOrEmpty(_preeditText) || !_proofreading.TryGetValue(paragraph, out var spans)) return;
        string text = BuildPlain(paragraph);
        foreach (var span in spans)
        {
            if (span.Start < 0 || span.Start + span.Text.Length > text.Length
                || !text.AsSpan(span.Start, span.Text.Length).SequenceEqual(span.Text)) continue;
            foreach (var rect in layout.HitTestTextRange(span.Start, span.Text.Length))
            {
                double end = x + rect.Right, baseline = y + rect.Bottom - 1;
                bool up = true;
                for (double left = x + rect.Left; left < end; left += 2)
                {
                    context.DrawLine(ProofreadingPen, new Point(left, baseline + (up ? 0 : 1.6)),
                        new Point(Math.Min(end, left + 2), baseline + (up ? 1.6 : 0)));
                    up = !up;
                }
            }
        }
    }
}
