namespace ScriSelfStudio.Core.Text;

using System.Text;
using System.Text.RegularExpressions;

public enum SceneDiffKind { Unchanged, Removed, Added }
public sealed record SceneDiffPart(SceneDiffKind Kind, string Text);
public sealed record SceneDiff(IReadOnlyList<SceneDiffPart> Parts, bool IsCoarse);

/// <summary>Сравнение слов с ограниченной памятью; сохраняет все пробелы и знаки.</summary>
public static partial class SceneTextDiff
{
    private const long MaxCells = 2_000_000;

    public static SceneDiff Compare(string before, string after)
    {
        var a = Tokens().Matches(before).Select(m => m.Value).ToArray();
        var b = Tokens().Matches(after).Select(m => m.Value).ToArray();
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix
            && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;
        var m = a.Length - prefix - suffix;
        var n = b.Length - prefix - suffix;
        var coarse = (long)(m + 1) * (n + 1) > MaxCells;
        var parts = new List<SceneDiffPart>();
        var buffer = new StringBuilder();
        var kind = SceneDiffKind.Unchanged;
        void Add(SceneDiffKind next, string text)
        {
            if (text.Length == 0) return;
            if (next != kind && buffer.Length > 0) { parts.Add(new(kind, buffer.ToString())); buffer.Clear(); }
            kind = next;
            buffer.Append(text);
        }
        for (var i = 0; i < prefix; i++) Add(SceneDiffKind.Unchanged, a[i]);
        if (coarse)
        {
            for (var i = prefix; i < prefix + m; i++) Add(SceneDiffKind.Removed, a[i]);
            for (var j = prefix; j < prefix + n; j++) Add(SceneDiffKind.Added, b[j]);
        }
        else
        {
            var lengths = new int[m + 1, n + 1];
            for (var i = m - 1; i >= 0; i--)
                for (var j = n - 1; j >= 0; j--)
                    lengths[i, j] = a[prefix + i] == b[prefix + j]
                        ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            var x = 0;
            var y = 0;
            while (x < m || y < n)
            {
                if (x < m && y < n && a[prefix + x] == b[prefix + y])
                { Add(SceneDiffKind.Unchanged, a[prefix + x++]); y++; }
                else if (x < m && (y == n || lengths[x + 1, y] >= lengths[x, y + 1]))
                    Add(SceneDiffKind.Removed, a[prefix + x++]);
                else Add(SceneDiffKind.Added, b[prefix + y++]);
            }
        }
        for (var i = a.Length - suffix; i < a.Length; i++) Add(SceneDiffKind.Unchanged, a[i]);
        if (buffer.Length > 0) parts.Add(new(kind, buffer.ToString()));
        return new(parts, coarse);
    }

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]+|\s+|[^\p{L}\p{M}\p{N}\s]", RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();
}
