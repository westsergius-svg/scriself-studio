namespace ScriSelfStudio.EditorEngine;

using ScriSelfStudio.Contracts.Editor;

/// <summary>Поиск по тексту сцены (ТЗ п. 6, 11).</summary>
public static class TextSearchService
{
    /// <summary>Ограничение числа совпадений: панель поиска не должна строиться бесконечно.</summary>
    public const int MaxMatches = 5000;

    private const int PreviewPadding = 24;

    public static SearchResult Search(string text, string query, SearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var effective = options ?? SearchOptions.Default;
        if (string.IsNullOrEmpty(query) || text.Length == 0 || query.Length > text.Length)
        {
            return new SearchResult { Matches = [], TotalCount = 0 };
        }

        var comparison = effective.MatchCase
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var matches = new List<SearchMatch>();
        var index = 0;

        while (index <= text.Length - query.Length && matches.Count < MaxMatches)
        {
            var found = text.IndexOf(query, index, comparison);
            if (found < 0)
            {
                break;
            }

            if (!effective.WholeWord || IsWholeWord(text, found, query.Length))
            {
                matches.Add(new SearchMatch
                {
                    Range = new TextRange(new TextPosition(found), new TextPosition(found + query.Length)),
                    Preview = BuildPreview(text, found, query.Length),
                });
            }

            index = found + Math.Max(1, query.Length);
        }

        return new SearchResult { Matches = matches, TotalCount = matches.Count };
    }

    private static bool IsWholeWord(string text, int start, int length)
    {
        var beforeOk = start == 0 || !IsWordCharacter(text[start - 1]);
        var afterIndex = start + length;
        var afterOk = afterIndex >= text.Length || !IsWordCharacter(text[afterIndex]);
        return beforeOk && afterOk;
    }

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static string BuildPreview(string text, int start, int length)
    {
        var from = Math.Max(0, start - PreviewPadding);
        var to = Math.Min(text.Length, start + length + PreviewPadding);
        var fragment = text[from..to].Replace('\r', ' ').Replace('\n', ' ');
        var prefix = from > 0 ? "…" : string.Empty;
        var suffix = to < text.Length ? "…" : string.Empty;
        return prefix + fragment + suffix;
    }
}
