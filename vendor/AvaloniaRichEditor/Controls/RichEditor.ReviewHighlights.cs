using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

public sealed record ReviewHighlightSpan(Paragraph Paragraph, int Start, int Length, bool IsComment);

public partial class RichEditor
{
    private IReadOnlyDictionary<Paragraph, ReviewHighlightSpan[]> _reviewHighlights = new Dictionary<Paragraph, ReviewHighlightSpan[]>();
    private static readonly IBrush CommentHighlight = new SolidColorBrush(Color.Parse("#40E8B442"));
    private static readonly Pen RevisionUnderline = new(new SolidColorBrush(Color.Parse("#218858")), 1.6);

    public void SetReviewHighlights(IEnumerable<ReviewHighlightSpan> spans)
    {
        _reviewHighlights = spans.GroupBy(s => s.Paragraph).ToDictionary(g => g.Key, g => g.ToArray());
        InvalidateVisual();
    }

    private void DrawReviewHighlights(DrawingContext context, Paragraph paragraph, TextLayout layout, double x, double y)
    {
        if (!_reviewHighlights.TryGetValue(paragraph, out var spans)) return;
        var length = GetParagraphLength(paragraph);
        foreach (var span in spans)
        {
            if (span.Start < 0 || span.Length <= 0 || span.Start > length - span.Length) continue;
            foreach (var rect in layout.HitTestTextRange(span.Start, span.Length))
            {
                if (span.IsComment) context.DrawRectangle(CommentHighlight, null, new Rect(x + rect.X, y + rect.Y, rect.Width, rect.Height));
                else context.DrawLine(RevisionUnderline, new Point(x + rect.Left, y + rect.Bottom), new Point(x + rect.Right, y + rect.Bottom));
            }
        }
    }
}
