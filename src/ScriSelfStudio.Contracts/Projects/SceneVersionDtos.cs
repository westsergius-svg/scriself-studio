namespace ScriSelfStudio.Contracts.Projects;

using ScriSelfStudio.Core.Text;

public sealed record SceneVersionInfo(Guid Id, string Name, DateTimeOffset CreatedAt,
    long SourceRevision, bool IsSafetySnapshot, int Words);

public sealed record SceneVersionDocument(SceneVersionInfo Info, RichDocument Document);
