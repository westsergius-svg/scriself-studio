namespace ScriSelfStudio.Core.Text;

using System.Text.Json.Serialization;

/// <summary>Абзац или другой блок документа.</summary>
public sealed class TextBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public BlockKind Kind { get; set; } = BlockKind.Paragraph;

    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    public int IndentLevel { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ListMarkerKind ListMarker { get; set; }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<TextSpan> Spans { get; } = [];

    public int Length => Spans.Sum(span => span.Text.Length);
}
