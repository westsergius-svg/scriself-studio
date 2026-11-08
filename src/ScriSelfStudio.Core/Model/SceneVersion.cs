namespace ScriSelfStudio.Core.Model;

using ScriSelfStudio.Core.Text;

/// <summary>Именованный вариант сцены; документ изолирован от текущего текста.</summary>
public sealed record SceneVersion
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required RichDocument Document { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public long SourceRevision { get; init; }
    public bool IsSafetySnapshot { get; init; }
    public SceneVersion Copy() => this with { Document = Document.Clone() };
}
