namespace ScriSelfStudio.Persistence.Language;

using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScriSelfStudio.Contracts.Editor;
using WeCantSpell.Hunspell;

/// <summary>Hunspell для орфографии; отдельные группы wiki-ru-wordnet для синонимов.</summary>
public sealed partial class TextToolsService : ITextToolsService
{
    private static readonly Lazy<Task<WordList>> Russian = new(() => Task.Run(() => LoadDictionary("ru_RU")));
    private static readonly Lazy<Task<WordList>> English = new(() => Task.Run(() => LoadDictionary("en_US")));
    private static readonly Lazy<Task<Dictionary<string, string[]>>> Synonyms = new(() => Task.Run(LoadSynonyms));

    [GeneratedRegex(@"[A-Za-zА-Яа-яЁё]+(?:[-'’][A-Za-zА-Яа-яЁё]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex Words();

    [GeneratedRegex(@"(?:https?://|www\.)\S+|\S+@\S+|\b\w*\d\w*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExcludedTokens();

    public async Task<IReadOnlyList<SpellingIssue>> CheckAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) return [];
        var ru = await Russian.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        var en = await English.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run<IReadOnlyList<SpellingIssue>>(() =>
        {
            var excluded = ExcludedTokens().Matches(text).Cast<Match>().ToArray();
            var result = new List<SpellingIssue>();
            var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (Match match in Words().Matches(text))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var word = match.Value;
                if (word.Length > 80 || (word.Length > 1 && word.All(char.IsUpper))
                    || excluded.Any(e => match.Index >= e.Index && match.Index < e.Index + e.Length)) continue;
                if (!cache.TryGetValue(word, out var valid))
                {
                    var russian = word.Any(IsRussian);
                    var latin = word.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
                    valid = !(russian && latin) && (russian ? ru : en).Check(word);
                    cache[word] = valid;
                }
                if (!valid) result.Add(new SpellingIssue(match.Index, match.Length, word));
            }
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> SuggestAsync(string word, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (word.Length is 0 or > 80) return [];
        var dictionary = await (word.Any(IsRussian) ? Russian : English).Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run<IReadOnlyList<string>>(() =>
        {
            // The default 50 ms steps can miss common corrections on a busy machine.
            // Suggestions run off the UI thread and remain bounded and cancellable.
            var options = new QueryOptions
            {
                TimeLimitSuggestStep = TimeSpan.FromMilliseconds(250),
                TimeLimitCompoundSuggest = TimeSpan.FromMilliseconds(250),
                TimeLimitCompoundCheck = TimeSpan.FromMilliseconds(100),
                TimeLimitSuggestGlobal = TimeSpan.FromSeconds(1),
            };
            var suggestions = dictionary.Suggest(word, options, cancellationToken).Take(8).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return suggestions;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> SynonymsAsync(string word, CancellationToken cancellationToken = default)
    {
        var lookup = await Synonyms.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return lookup.TryGetValue(word.ToLowerInvariant().Replace('ё', 'е'), out var words)
            ? words.Where(w => !w.Equals(word, StringComparison.OrdinalIgnoreCase)).Take(30).ToArray() : [];
    }

    private static bool IsRussian(char c) => c is >= 'А' and <= 'я' or 'Ё' or 'ё';

    private static WordList LoadDictionary(string name)
    {
        using var dic = Open(name + ".dic");
        using var aff = Open(name + ".aff");
        return WordList.CreateFromStreams(dic, aff);
    }

    private static Dictionary<string, string[]> LoadSynonyms()
    {
        using var resource = Open("wiki-synonyms.json.gz");
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        var groups = JsonSerializer.Deserialize<string[][]>(gzip) ?? [];
        var lookup = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var word in group)
            {
                var key = word.ToLowerInvariant().Replace('ё', 'е');
                if (!lookup.TryGetValue(key, out var synonyms)) lookup[key] = synonyms = new(StringComparer.OrdinalIgnoreCase);
                synonyms.UnionWith(group);
            }
        }
        return lookup.ToDictionary(pair => pair.Key, pair => pair.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.Ordinal);
    }

    private static Stream Open(string name) => typeof(TextToolsService).Assembly.GetManifestResourceStream("ScriSelfStudio.Language." + name)
        ?? throw new InvalidOperationException("Не найден локальный словарь: " + name);
}
