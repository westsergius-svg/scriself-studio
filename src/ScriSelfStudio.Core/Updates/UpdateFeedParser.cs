namespace ScriSelfStudio.Core.Updates;

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Разбор канала обновлений Scriborium: манифеста <c>latest.json</c> и листинга
/// каталога. Повторяет контракт старого апдейтера Scriborium 1.1.3.3S, чтобы
/// сборки публиковались на том же сайте и по тому же адресу.
/// </summary>
public static class UpdateFeedParser
{
    /// <summary>Числовые части версии: <c>1.1.3.3S</c> → <c>1.1.3.3</c>.</summary>
    private static readonly Regex VersionPattern = new(
        @"(\d+(?:\.\d+)+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Ссылки на установщики в листинге каталога.</summary>
    private static readonly Regex InstallerHrefPattern = new(
        "href=[\"']([^\"'#?]+?\\.(?:exe|zip|dmg))[\"']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Сборки не для десктопного канала: Lite, Modular и прочие варианты.</summary>
    private static readonly string[] SkipTokens = ["modular", "lite", "mini", "legacy"];

    /// <summary>
    /// Префиксы имён установщиков в каталоге: сборки прежнего Scriborium и сборки
    /// самого ScriSelf Studio. Имя установщика — это то, что отличает сборку от прочих
    /// файлов каталога, поэтому оба имени принимаются одинаково.
    /// </summary>
    private static readonly string[] SetupPrefixes = ["scriborium-setup", "scriselfstudio-setup"];

    /// <summary>Числовые части версии; пустой список, если версии в строке нет.</summary>
    public static IReadOnlyList<int> ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var match = VersionPattern.Match(text);
        if (!match.Success)
        {
            return [];
        }

        var parts = new List<int>();
        foreach (var part in match.Groups[1].Value.Split('.'))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return [];
            }
            parts.Add(number);
        }

        return parts;
    }

    /// <summary>
    /// Сравнение версий: <c>-1</c>, <c>0</c> или <c>1</c>. Недостающие части
    /// считаются нулями, поэтому <c>1.1.4</c> новее <c>1.1.3.3</c>.
    /// </summary>
    public static int CompareVersions(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        var length = Math.Max(left.Count, right.Count);
        for (var index = 0; index < length; index++)
        {
            var a = index < left.Count ? left[index] : 0;
            var b = index < right.Count ? right[index] : 0;
            if (a != b)
            {
                return a < b ? -1 : 1;
            }
        }

        return 0;
    }

    /// <summary>Версия <paramref name="candidate"/> новее <paramref name="current"/>.</summary>
    public static bool IsNewer(IReadOnlyList<int> candidate, IReadOnlyList<int> current) =>
        CompareVersions(candidate, current) > 0;

    /// <summary>Собирает адрес файла в каталоге обновлений, экранируя имя.</summary>
    public static string JoinUrl(string baseUrl, string fileName) =>
        baseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(fileName.TrimStart('/'));

    /// <summary>Разбирает <c>latest.json</c>. <c>null</c> — запись без файла сборки.</summary>
    public static UpdateInfo? ParseManifest(string json, string baseUrl, string runtimeIdentifier = "win-x64")
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var versionText = ReadString(root, "version") ?? ReadString(root, "version_label");
        var fileName = ReadString(root, "file") ?? ReadString(root, "file_name") ?? ReadString(root, "setup");
        if (string.IsNullOrWhiteSpace(fileName) || !SupportsRuntime(fileName, runtimeIdentifier))
        {
            return null;
        }

        var version = ParseVersion(versionText);
        if (version.Count == 0)
        {
            version = ParseVersion(fileName);
        }

        if (version.Count == 0)
        {
            return null;
        }

        return new UpdateInfo(
            version,
            string.IsNullOrWhiteSpace(versionText) ? string.Join('.', version) : versionText,
            fileName,
            JoinUrl(baseUrl, fileName),
            ReadString(root, "notes") ?? string.Empty,
            "manifest")
        {
            Sha256 = ReadString(root, "sha256"),
            Signature = ReadString(root, "signature"),
        };
    }

    /// <summary>
    /// Разбирает листинг каталога: все ссылки на <c>*.exe</c> с именем вида
    /// <c>Scriborium-Setup-*</c> или <c>ScriSelfStudio-Setup-*</c>, кроме сборок
    /// Lite/Modular.
    /// </summary>
    public static IReadOnlyList<UpdateInfo> ParseListing(string html, string baseUrl, string runtimeIdentifier = "win-x64")
    {
        var results = new List<UpdateInfo>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return results;
        }

        foreach (var href in InstallerHrefPattern.Matches(html).Select(match => match.Groups[1].Value))
        {
            var name = Uri.UnescapeDataString(href.Split('/')[^1]);
            var lower = name.ToLowerInvariant();
            var recognizedName = runtimeIdentifier.StartsWith("osx-", StringComparison.Ordinal)
                ? lower.StartsWith("scriselfstudio-", StringComparison.Ordinal)
                : SetupPrefixes.Any(prefix => lower.StartsWith(prefix, StringComparison.Ordinal));
            if (!recognizedName || !SupportsRuntime(name, runtimeIdentifier) ||
                SkipTokens.Any(token => lower.Contains(token, StringComparison.Ordinal)))
            {
                continue;
            }

            var version = ParseVersion(name);
            if (version.Count == 0)
            {
                continue;
            }

            var downloadUrl = JoinUrl(baseUrl, name);
            if (href.Contains("://", StringComparison.Ordinal)
                && (!Uri.TryCreate(href, UriKind.Absolute, out var linkedUrl)
                    || !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var channelUrl) || linkedUrl != channelUrl))
            {
                continue;
            }
            results.Add(new UpdateInfo(
                version,
                string.Join('.', version),
                name,
                downloadUrl,
                string.Empty,
                "listing"));
        }

        return results;
    }

    /// <summary>Самая новая сборка из списка; <c>null</c> для пустого списка.</summary>
    public static UpdateInfo? Newest(IEnumerable<UpdateInfo> infos)
    {
        UpdateInfo? newest = null;
        foreach (var info in infos)
        {
            if (newest is null || CompareVersions(info.Version, newest.Version) > 0)
            {
                newest = info;
            }
        }

        return newest;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Windows и две архитектуры Mac никогда не предлагают сборки друг друга.</summary>
    internal static bool SupportsRuntime(string fileName, string runtimeIdentifier)
    {
        if (fileName.IndexOfAny(['/', '\\']) >= 0)
        {
            return false;
        }

        if (runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal))
        {
            return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        return runtimeIdentifier is "osx-arm64" or "osx-x64"
            && (fileName.EndsWith("-" + runtimeIdentifier + ".zip", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith("-" + runtimeIdentifier + ".dmg", StringComparison.OrdinalIgnoreCase));
    }
}
