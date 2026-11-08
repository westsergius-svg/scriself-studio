namespace ScriSelfStudio.Core.Text;

using System.Text.Json.Serialization;

public sealed class DocumentReview
{
    public bool TrackChanges { get; set; }
    public string Author { get; set; } = "Автор";
    public List<TextComment> Comments { get; set; } = [];
    public List<TrackedTextChange> Changes { get; set; } = [];

    // Temporary composite-document transport. Persisted scene documents have no sections.
    [JsonIgnore] public List<DocumentReview>? Sections { get; set; }

    public DocumentReview Clone() => new()
    {
        TrackChanges = TrackChanges, Author = Author,
        Comments = Comments.Select(c => c with { }).ToList(),
        Changes = Changes.Select(c => c with { Before = c.Before.Clone() }).ToList(),
        Sections = Sections?.Select(s => s.Clone()).ToList(),
    };
}

public sealed record TextComment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Author { get; init; } = "Автор";
    public DateTimeOffset CreatedAt { get; init; }
    public string Text { get; set; } = string.Empty;
    public string Quote { get; init; } = string.Empty;
    public int Start { get; set; }
    public int Length { get; set; }
    public Guid StartBlockId { get; set; }
    public Guid EndBlockId { get; set; }
    public bool IsOrphaned { get; set; }
    public bool IsResolved { get; set; }
}

public sealed record TrackedTextChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Author { get; init; } = "Автор";
    public DateTimeOffset CreatedAt { get; init; }
    public int Start { get; set; }
    public int Length { get; set; }
    public int OriginalStart { get; init; }
    public int OriginalLength { get; init; }
    public RichDocument Before { get; init; } = RichDocument.CreateEmpty();
}
