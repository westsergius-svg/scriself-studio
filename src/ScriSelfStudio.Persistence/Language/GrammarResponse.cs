namespace ScriSelfStudio.Persistence.Language;

using System.Text.Json;
using ScriSelfStudio.Contracts.Editor;

public static class GrammarResponse
{
    public const int ChunkLength = 8000;

    public static string DetectLanguage(string text) => text.Count(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё')
        > text.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') ? "ru" : "en-US";

    public static IEnumerable<(int Start, string Text)> Chunks(string text)
    {
        for (var start = 0; start < text.Length;)
        {
            var end = Math.Min(text.Length, start + ChunkLength);
            if (end < text.Length)
            {
                var boundary = end;
                while (boundary > start + ChunkLength / 2 && text[boundary - 1] is not ('.' or '!' or '?' or '\n')) boundary--;
                if (boundary > start + ChunkLength / 2) end = boundary;
                if (char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
            }
            yield return (start, text[start..end]);
            start = end;
        }
    }

    public static IReadOnlyList<GrammarIssue> Parse(string json, string original)
    {
        using var document = JsonDocument.Parse(json);
        var results = new List<GrammarIssue>();
        foreach (var match in document.RootElement.GetProperty("matches").EnumerateArray())
        {
            var rule = match.GetProperty("rule");
            var category = rule.GetProperty("category").GetProperty("id").GetString();
            var type = rule.GetProperty("issueType").GetString();
            var id = rule.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing grammar rule id.");
            if (category is not ("GRAMMAR" or "PUNCTUATION") && type is not ("grammar" or "duplication")
                && id != "COMMA_PARENTHESIS_WHITESPACE") continue;
            var start = match.GetProperty("offset").GetInt32();
            var length = match.GetProperty("length").GetInt32();
            if (start < 0 || length < 0 || start > original.Length - length || SplitsPair(start) || SplitsPair(start + length))
                throw new InvalidDataException("Invalid grammar range.");
            var replacements = match.GetProperty("replacements").EnumerateArray()
                .Select(r => r.GetProperty("value").GetString()).OfType<string>()
                .Where(r => r.Length <= ChunkLength && !r.Contains('\n') && !r.Contains('\r')).Distinct().Take(8).ToArray();
            results.Add(new(start, length, original.Substring(start, length), match.GetProperty("message").GetString() ?? id, id, replacements));
        }
        return results.OrderBy(i => i.Start).ToArray();
        bool SplitsPair(int offset) => offset > 0 && offset < original.Length
            && char.IsHighSurrogate(original[offset - 1]) && char.IsLowSurrogate(original[offset]);
    }
}
