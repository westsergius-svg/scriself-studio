namespace ScriSelfStudio.Contracts.Editor;

public sealed record SpellingIssue(int Start, int Length, string Word);

/// <summary>Локальные словари; отсутствие синонима не является орфографической ошибкой.</summary>
public interface ITextToolsService
{
    Task<IReadOnlyList<SpellingIssue>> CheckAsync(string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> SuggestAsync(string word, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> SynonymsAsync(string word, CancellationToken cancellationToken = default);
}
