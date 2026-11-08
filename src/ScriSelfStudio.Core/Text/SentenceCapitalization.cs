namespace ScriSelfStudio.Core.Text;

using System.Text.RegularExpressions;

/// <summary>Консервативная автозамена начала нового предложения после завершения слова.</summary>
public static partial class SentenceCapitalization
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "г", "гг", "ул", "д", "кв", "рис", "стр", "им", "т", "п", "см", "др", "пр", "руб", "коп", "мин", "сек",
        "Mr", "Mrs", "Ms", "Dr", "Prof", "vs", "etc", "no", "fig", "vol",
    };

    public static int? FindWordStart(string textBeforeCaret)
    {
        var word = TrailingWord().Match(textBeforeCaret);
        if (!word.Success || !char.IsLower(word.Value[0]) || word.Index == 0) return null;
        var before = textBeforeCaret[..word.Index];
        if (!char.IsWhiteSpace(before[^1])) return null;
        before = before.TrimEnd().TrimEnd('»', '”', '"', '\'', ')', ']');
        if (before.Length == 0 || before[^1] is not ('.' or '!' or '?')) return null;
        if (before[^1] == '.')
        {
            var prefix = before[..^1];
            if (prefix.EndsWith('.')) return null;
            var previous = TrailingWord().Match(prefix);
            if (previous.Success && (previous.Length == 1 || Abbreviations.Contains(previous.Value))) return null;
        }
        return word.Index;
    }

    [GeneratedRegex(@"[\p{L}\p{M}]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingWord();
}
