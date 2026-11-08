namespace ScriSelfStudio.Presentation.Export;

using Avalonia.Platform;
using PdfSharp.Fonts;

/// <summary>Шрифты PDF для macOS и запасные встроенные шрифты для других платформ.</summary>
public sealed class PortableFontResolver : IFontResolver
{
    private const string InterRegular = "scriself-inter-regular";
    private const string InterBold = "scriself-inter-bold";
    private readonly Dictionary<string, string> _systemFonts = new(StringComparer.OrdinalIgnoreCase);

    public PortableFontResolver(string? systemFontDirectory = null)
    {
        if (systemFontDirectory is null)
        {
            return;
        }

        foreach (var family in new[] { "Georgia", "Times New Roman", "Arial" })
        {
            foreach (var (suffix, style) in new[]
            {
                (string.Empty, "regular"), (" Bold", "bold"),
                (" Italic", "italic"), (" Bold Italic", "bold-italic"),
            })
            {
                var path = Path.Combine(systemFontDirectory, family + suffix + ".ttf");
                if (File.Exists(path))
                {
                    _systemFonts[family + ":" + style] = path;
                }
            }
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var style = (isBold, isItalic) switch
        {
            (true, true) => "bold-italic",
            (true, false) => "bold",
            (false, true) => "italic",
            _ => "regular",
        };
        var faceName = familyName + ":" + style;
        if (_systemFonts.ContainsKey(faceName))
        {
            return new FontResolverInfo(faceName);
        }

        return new FontResolverInfo(isBold ? InterBold : InterRegular, false, isItalic);
    }

    public byte[] GetFont(string faceName)
    {
        if (_systemFonts.TryGetValue(faceName, out var path))
        {
            return File.ReadAllBytes(path);
        }

        var assetName = faceName switch
        {
            InterRegular => "Inter-Regular.ttf",
            InterBold => "Inter-Bold.ttf",
            _ => throw new ArgumentException("Неизвестный шрифт PDF.", nameof(faceName)),
        };
        // StandardAssetLoader читает ресурсы сборки даже без запущенного UI.
        using var source = new StandardAssetLoader().Open(
            new Uri("avares://Avalonia.Fonts.Inter/Assets/" + assetName));
        using var output = new MemoryStream();
        source.CopyTo(output);
        return output.ToArray();
    }
}
