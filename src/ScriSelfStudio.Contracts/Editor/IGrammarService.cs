namespace ScriSelfStudio.Contracts.Editor;

using ScriSelfStudio.Contracts.Errors;

/// <summary>Offsets use UTF-16 in the exact text supplied to CheckAsync.</summary>
public sealed record GrammarIssue(int Start, int Length, string OriginalText, string Message,
    string RuleId, IReadOnlyList<string> Replacements);

public interface IGrammarService
{
    /// <summary>Languages: auto (RU/EN by script), ru, en-US. Cancellation throws; failures are explicit.</summary>
    Task<Result<IReadOnlyList<GrammarIssue>>> CheckAsync(string text, string language, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
