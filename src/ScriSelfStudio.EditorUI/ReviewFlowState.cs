namespace ScriSelfStudio.EditorUI;

using AvaloniaRichEditor.Documents;
using ScriSelfStudio.Core.Text;

/// <summary>Synchronizes review metadata before native undo snapshots, including edits before a render.</summary>
internal sealed class ReviewFlowState(RichDocument snapshot, double fontSize) : IFlowDocumentState
{
    public RichDocument Snapshot { get; private set; } = snapshot.Clone();
    public string? Error { get; private set; }

    public long EstimatedBytes => Estimate(Snapshot);

    private static long Estimate(RichDocument document)
    {
        var bytes = document.Blocks.Sum(b => 128L + b.Spans.Sum(s => 80L + s.Text.Length * 2L));
        var reviews = document.Review.Sections ?? [document.Review];
        foreach (var review in reviews)
        {
            bytes += review.Comments.Sum(c => 160L + (c.Text.Length + c.Quote.Length) * 2L);
            bytes += review.Changes.Sum(c => 160L + Estimate(c.Before));
        }
        return bytes;
    }

    public void Synchronize(FlowDocument document)
    {
        try
        {
            var current = RichDocumentAdapter.ReadFlowContent(document, fontSize);
            var count = Snapshot.Review.Sections?.Count ?? 1;
            if (count == 0) count = 1;
            var previousScenes = RichDocumentAdapter.SplitRibbonDocument(Snapshot, count);
            var currentScenes = RichDocumentAdapter.SplitRibbonDocument(current, count);
            for (var i = 0; i < count; i++) ReviewEditing.Capture(previousScenes[i], currentScenes[i], DateTimeOffset.UtcNow);
            Snapshot = Snapshot.Review.Sections is null ? currentScenes[0]
                : RichDocumentAdapter.ToRibbonDocument(currentScenes);
            Error = null;
        }
        catch (InvalidDataException error)
        {
            // Keep the last valid metadata until a damaged scene boundary is undone.
            Error = error.Message;
        }
    }

    public object Clone() => new ReviewFlowState(Snapshot, fontSize);
}
