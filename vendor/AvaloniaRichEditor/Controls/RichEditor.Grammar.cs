using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

public sealed record GrammarSpan(Paragraph Paragraph, int Start, int Length);

public partial class RichEditor
{
    private IReadOnlyDictionary<Paragraph, GrammarSpan[]> _grammar = new Dictionary<Paragraph, GrammarSpan[]>();
    private static readonly Pen GrammarPen = new(new SolidColorBrush(Color.Parse("#337DD6")), 1.1);

    public void SetGrammarSpans(IEnumerable<GrammarSpan> spans)
    {
        _grammar = spans.GroupBy(s => s.Paragraph).ToDictionary(g => g.Key, g => g.ToArray());
        InvalidateVisual();
    }

    private void DrawGrammar(DrawingContext context, Paragraph paragraph, TextLayout layout, double x, double y)
    {
        if (!_grammar.TryGetValue(paragraph, out var spans)) return;
        var length = GetParagraphLength(paragraph);
        if (length == 0) return;
        foreach (var span in spans)
        {
            if (span.Start < 0 || span.Length < 0 || span.Start > length - span.Length) continue;
            var start = Math.Min(span.Start, length - 1);
            foreach (var rect in layout.HitTestTextRange(start, Math.Max(1, span.Length)))
            {
                context.DrawLine(GrammarPen, new Point(x + rect.Left, y + rect.Bottom), new Point(x + rect.Right, y + rect.Bottom));
                context.DrawLine(GrammarPen, new Point(x + rect.Left, y + rect.Bottom + 2), new Point(x + rect.Right, y + rect.Bottom + 2));
            }
        }
    }
}
