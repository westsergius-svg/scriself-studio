namespace ScriSelfStudio.EditorUI;

using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using ScriSelfStudio.Core.Text;
using RichTextAlignment = Avalonia.Media.TextAlignment;
using RichBlockKind = ScriSelfStudio.Core.Text.BlockKind;
using RichDocumentModel = ScriSelfStudio.Core.Text.RichDocument;
using RichListMarker = AvaloniaRichEditor.Documents.ListMarkerStyle;
using RichListKind = AvaloniaRichEditor.Documents.ListKind;
using RichParagraph = AvaloniaRichEditor.Documents.Paragraph;
using RichRun = AvaloniaRichEditor.Documents.Run;
using RichTextBlock = ScriSelfStudio.Core.Text.TextBlock;
using RichTextColor = ScriSelfStudio.Core.Text.TextColor;
using RichTextFormat = ScriSelfStudio.Core.Text.TextFormat;
using RichTextSpan = ScriSelfStudio.Core.Text.TextSpan;

internal static class RichDocumentAdapter
{
    private const char RibbonHeaderMarker = '\u200B';

    /// <summary>
    /// Дополнительный отступ перед разделителем сцены. Разделитель-<c>DividerBlock</c>
    /// уже несёт собственный вертикальный отступ, поэтому лента не добавляет свой:
    /// иначе разрыв между сценами удваивался и выглядел заметно шире вордовского.
    /// </summary>
    private const double RibbonSceneBottomSpacing = 0;

    /// <summary>
    /// Каждая следующая сцена начинается с новой страницы. В непрерывной ленте
    /// это эквивалент 46 пустых строк Word при стандартном форматировании.
    /// </summary>
    private const int SceneSpacingLines = 46;
    private const double SceneLineHeightFactor = 1.5;

    internal sealed record RibbonSceneHeader(int Order, string Title, string StatusLabel, int Words);

    public static RichDocument ToRibbonDocument(IEnumerable<RichDocument> sceneDocuments)
    {
        var ribbon = new RichDocument();
        var hasScene = false;
        foreach (var sceneDocument in sceneDocuments)
        {
            if (sceneDocument.Blocks.Any(block => block.Kind == RichBlockKind.Separator))
            {
                throw new InvalidDataException("A scene document cannot contain ribbon separators.");
            }

            if (hasScene)
            {
                ribbon.Blocks.Add(new RichTextBlock { Kind = RichBlockKind.Separator });
            }

            foreach (var block in sceneDocument.Blocks)
            {
                ribbon.Blocks.Add(CloneBlock(block));
            }

            if (sceneDocument.Blocks.Count == 0)
            {
                ribbon.Blocks.Add(new RichTextBlock());
            }

            hasScene = true;
        }

        if (!hasScene)
        {
            ribbon.Blocks.Add(new RichTextBlock());
        }

        return ribbon;
    }

    public static IReadOnlyList<RichDocument> SplitRibbonDocument(RichDocument ribbon, int sceneCount)
    {
        ArgumentNullException.ThrowIfNull(ribbon);
        if (sceneCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneCount));
        }

        var scenes = new List<RichDocument>(sceneCount);
        var current = new RichDocument();
        foreach (var block in ribbon.Blocks)
        {
            if (block.Kind == RichBlockKind.Separator)
            {
                scenes.Add(EnsureNonEmpty(current));
                current = new RichDocument();
            }
            else
            {
                current.Blocks.Add(CloneBlock(block));
            }
        }

        scenes.Add(EnsureNonEmpty(current));
        if (scenes.Count != sceneCount)
        {
            throw new InvalidDataException(
                "Scene boundaries in the editor were changed. Restore the scene separators before continuing.");
        }

        return scenes;
    }

    public static FlowDocument ToRibbonFlowDocument(
        RichDocument ribbon,
        double defaultFontSize,
        IReadOnlyList<RibbonSceneHeader>? sceneHeaders = null)
    {
        var flowDocument = ToFlowDocument(ribbon, defaultFontSize);
        foreach (var block in flowDocument.Blocks)
        {
            if (block is DividerBlock)
            {
                var previousBlock = flowDocument.Blocks
                    .TakeWhile(candidate => !ReferenceEquals(candidate, block))
                    .LastOrDefault();
                if (previousBlock is not null)
                {
                    previousBlock.MarginBottom = RibbonSceneBottomSpacing;
                }

                block.MarginTop = SceneSpacingLines * defaultFontSize * SceneLineHeightFactor;
            }
        }

        if (sceneHeaders is { Count: > 0 })
        {
            InsertSceneHeaders(flowDocument, sceneHeaders, defaultFontSize);
        }

        return flowDocument;
    }

    /// <summary>
    /// Добавляет в непрерывный документ видимые заголовки сцен. Заголовок — это
    /// обычный абзац с невидимой меткой в начале; при обратном преобразовании
    /// такие абзацы отбрасываются и никогда не попадают в текст сцены.
    /// </summary>
    private static void InsertSceneHeaders(
        FlowDocument flowDocument,
        IReadOnlyList<RibbonSceneHeader> sceneHeaders,
        double defaultFontSize)
    {
        var insertions = new List<(int Index, Block Header)>();
        var sceneIndex = 0;
        for (var index = 0; index < flowDocument.Blocks.Count && sceneIndex < sceneHeaders.Count; index++)
        {
            var startsScene = index == 0 || flowDocument.Blocks[index - 1] is DividerBlock;
            if (!startsScene)
            {
                continue;
            }

            insertions.Add((index, CreateSceneHeader(sceneHeaders[sceneIndex], defaultFontSize)));
            sceneIndex++;
        }

        for (var index = insertions.Count - 1; index >= 0; index--)
        {
            flowDocument.Blocks.Insert(insertions[index].Index, insertions[index].Header);
        }
    }

    private static Block CreateSceneHeader(RibbonSceneHeader scene, double defaultFontSize)
    {
        var captionSize = Math.Max(9, defaultFontSize * 0.72);
        var paragraph = new RichParagraph
        {
            MarginTop = Math.Max(16, defaultFontSize * 1.4),
            MarginBottom = Math.Max(4, defaultFontSize * 0.35),
        };
        paragraph.Inlines.Add(new RichRun
        {
            Text = $"{RibbonHeaderMarker}{scene.Order + 1:D2}  СЦЕНА",
            FontSize = captionSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#8A8F90")),
        });
        paragraph.Inlines.Add(new RichRun
        {
            Text = $"   {scene.Title}",
            FontSize = Math.Max(15, defaultFontSize * 1.45),
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#25282A")),
        });
        paragraph.Inlines.Add(new RichRun
        {
            Text = $"      {scene.StatusLabel} · {scene.Words} сл.",
            FontSize = captionSize,
            Foreground = new SolidColorBrush(Color.Parse("#8A8F90")),
        });
        return paragraph;
    }

    private static bool IsSceneHeader(Block block) =>
        block is RichParagraph paragraph
        && paragraph.Inlines.OfType<RichRun>()
            .Any(run => run.Text?.StartsWith(RibbonHeaderMarker) == true);

    public static FlowDocument ToFlowDocument(RichDocumentModel document, double defaultFontSize)
    {
        var flowDocument = new FlowDocument();
        foreach (var block in document.Blocks)
        {
            if (block.Kind == RichBlockKind.Separator)
            {
                if (block.Length != 0)
                {
                    throw new InvalidDataException("A separator block cannot contain editable text.");
                }

                flowDocument.Blocks.Add(new DividerBlock());
                continue;
            }

            var paragraph = new RichParagraph
            {
                TextAlignment = ToRichAlignment(block.Alignment),
                HeadingLevel = ToHeadingLevel(block.Kind),
                IsQuote = block.Kind == RichBlockKind.Quote,
                Indent = block.Kind is RichBlockKind.BulletListItem or RichBlockKind.NumberedListItem
                    ? 0
                    : block.IndentLevel * 20,
                ListLevel = block.IndentLevel,
                ListType = block.Kind switch
                {
                    RichBlockKind.BulletListItem => RichListKind.Bullet,
                    RichBlockKind.NumberedListItem => RichListKind.Ordered,
                    _ => RichListKind.None,
                },
                ListMarker = ToRichListMarker(block.ListMarker),
            };

            foreach (var span in block.Spans)
            {
                var flags = span.Style.Flags;
                var decorations = new TextDecorationCollection();
                if (flags.HasFlag(RichTextFormat.Underline))
                {
                    decorations.AddRange(TextDecorations.Underline);
                }

                if (flags.HasFlag(RichTextFormat.Strikethrough))
                {
                    decorations.AddRange(TextDecorations.Strikethrough);
                }

                paragraph.Inlines.Add(new RichRun
                {
                    Text = span.Text,
                    FontFamily = span.Style.FontFamily
                        ?? (flags.HasFlag(RichTextFormat.Monospace) ? "Consolas" : null),
                    FontSize = span.Style.FontSize ?? defaultFontSize,
                    FontWeight = flags.HasFlag(RichTextFormat.Bold) ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = flags.HasFlag(RichTextFormat.Italic) ? FontStyle.Italic : FontStyle.Normal,
                    TextDecorations = decorations.Count == 0 ? null : decorations,
                    Foreground = ToBrush(span.Style.Foreground),
                    Background = ToBrush(span.Style.Highlight),
                });
            }

            flowDocument.Blocks.Add(paragraph);
        }

        return flowDocument;
    }

    public static RichDocumentModel FromFlowDocument(FlowDocument document, double defaultFontSize)
    {
        var richDocument = new RichDocumentModel();
        foreach (var block in document.Blocks)
        {
            if (IsSceneHeader(block))
            {
                continue;
            }

            if (block is DividerBlock)
            {
                richDocument.Blocks.Add(new RichTextBlock { Kind = RichBlockKind.Separator });
                continue;
            }

            if (block is not RichParagraph paragraph)
            {
                throw new InvalidDataException($"Unsupported rich-text block: {block.GetType().Name}.");
            }

            var textBlock = new RichTextBlock
            {
                Kind = ToBlockKind(paragraph),
                Alignment = ToCoreAlignment(paragraph.TextAlignment),
                IndentLevel = paragraph.ListType != RichListKind.None
                    ? Math.Clamp(paragraph.ListLevel, 0, 8)
                    : Math.Clamp((int)Math.Round(paragraph.Indent / 20), 0, 8),
                ListMarker = FromRichListMarker(paragraph.ListMarker),
            };

            foreach (var inline in paragraph.Inlines)
            {
                if (inline is not RichRun run)
                {
                    throw new InvalidDataException($"Unsupported rich-text inline: {inline.GetType().Name}.");
                }

                if (string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                var flags = RichTextFormat.None;
                if (run.FontWeight == FontWeight.Bold)
                {
                    flags |= RichTextFormat.Bold;
                }

                if (run.FontStyle == FontStyle.Italic)
                {
                    flags |= RichTextFormat.Italic;
                }

                if (HasDecoration(run, TextDecorationLocation.Underline))
                {
                    flags |= RichTextFormat.Underline;
                }

                if (HasDecoration(run, TextDecorationLocation.Strikethrough))
                {
                    flags |= RichTextFormat.Strikethrough;
                }

                var fontFamily = run.FontFamily;
                if (string.Equals(fontFamily, "Consolas", StringComparison.OrdinalIgnoreCase))
                {
                    flags |= RichTextFormat.Monospace;
                    fontFamily = null;
                }

                textBlock.Spans.Add(new RichTextSpan
                {
                    Text = run.Text,
                    Style = new SpanStyle
                    {
                        Flags = flags,
                        FontFamily = fontFamily,
                        FontSize = Math.Abs(run.FontSize - defaultFontSize) < 0.01 ? null : run.FontSize,
                        Foreground = FromBrush(run.Foreground),
                        Highlight = FromBrush(run.Background),
                    },
                });
            }

            richDocument.Blocks.Add(textBlock);
        }

        if (richDocument.Blocks.Count == 0)
        {
            richDocument.Blocks.Add(new RichTextBlock());
        }

        return richDocument;
    }

    private static RichDocument EnsureNonEmpty(RichDocument document)
    {
        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new RichTextBlock());
        }

        return document;
    }

    private static RichTextBlock CloneBlock(RichTextBlock block)
    {
        var copy = new RichTextBlock
        {
            Id = block.Id,
            Kind = block.Kind,
            Alignment = block.Alignment,
            IndentLevel = block.IndentLevel,
            ListMarker = block.ListMarker,
        };
        foreach (var span in block.Spans)
        {
            copy.Spans.Add(new RichTextSpan { Text = span.Text, Style = span.Style });
        }

        return copy;
    }

    private static int ToHeadingLevel(RichBlockKind kind) => kind switch
    {
        RichBlockKind.Heading1 => 1,
        RichBlockKind.Heading2 => 2,
        RichBlockKind.Heading3 => 3,
        RichBlockKind.Heading4 => 4,
        RichBlockKind.Heading5 => 5,
        RichBlockKind.Heading6 => 6,
        _ => 0,
    };

    private static RichBlockKind ToBlockKind(RichParagraph paragraph)
    {
        if (paragraph.ListType == RichListKind.Bullet)
        {
            return RichBlockKind.BulletListItem;
        }

        if (paragraph.ListType == RichListKind.Ordered)
        {
            return RichBlockKind.NumberedListItem;
        }

        return paragraph.HeadingLevel switch
        {
            1 => RichBlockKind.Heading1,
            2 => RichBlockKind.Heading2,
            3 => RichBlockKind.Heading3,
            4 => RichBlockKind.Heading4,
            5 => RichBlockKind.Heading5,
            6 => RichBlockKind.Heading6,
            _ when paragraph.IsQuote => RichBlockKind.Quote,
            _ => RichBlockKind.Paragraph,
        };
    }

    private static RichTextAlignment ToRichAlignment(ScriSelfStudio.Core.Text.TextAlignment alignment) => alignment switch
    {
        ScriSelfStudio.Core.Text.TextAlignment.Center => RichTextAlignment.Center,
        ScriSelfStudio.Core.Text.TextAlignment.Right => RichTextAlignment.Right,
        ScriSelfStudio.Core.Text.TextAlignment.Justify => RichTextAlignment.Justify,
        _ => RichTextAlignment.Left,
    };

    private static ScriSelfStudio.Core.Text.TextAlignment ToCoreAlignment(RichTextAlignment alignment) => alignment switch
    {
        RichTextAlignment.Center => ScriSelfStudio.Core.Text.TextAlignment.Center,
        RichTextAlignment.Right => ScriSelfStudio.Core.Text.TextAlignment.Right,
        RichTextAlignment.Justify => ScriSelfStudio.Core.Text.TextAlignment.Justify,
        _ => ScriSelfStudio.Core.Text.TextAlignment.Left,
    };

    private static RichListMarker ToRichListMarker(ListMarkerKind marker) => marker switch
    {
        ListMarkerKind.Disc => RichListMarker.Disc,
        ListMarkerKind.Circle => RichListMarker.Circle,
        ListMarkerKind.Square => RichListMarker.Square,
        ListMarkerKind.Dash => RichListMarker.Dash,
        ListMarkerKind.Decimal => RichListMarker.Decimal,
        ListMarkerKind.DecimalParen => RichListMarker.DecimalParen,
        ListMarkerKind.LowerAlpha => RichListMarker.LowerAlpha,
        ListMarkerKind.UpperAlpha => RichListMarker.UpperAlpha,
        ListMarkerKind.LowerRoman => RichListMarker.LowerRoman,
        _ => RichListMarker.Default,
    };

    private static ListMarkerKind FromRichListMarker(RichListMarker marker) => marker switch
    {
        RichListMarker.Disc => ListMarkerKind.Disc,
        RichListMarker.Circle => ListMarkerKind.Circle,
        RichListMarker.Square => ListMarkerKind.Square,
        RichListMarker.Dash => ListMarkerKind.Dash,
        RichListMarker.Decimal => ListMarkerKind.Decimal,
        RichListMarker.DecimalParen => ListMarkerKind.DecimalParen,
        RichListMarker.LowerAlpha => ListMarkerKind.LowerAlpha,
        RichListMarker.UpperAlpha => ListMarkerKind.UpperAlpha,
        RichListMarker.LowerRoman => ListMarkerKind.LowerRoman,
        _ => ListMarkerKind.Default,
    };

    private static bool HasDecoration(RichRun run, TextDecorationLocation location) =>
        run.TextDecorations?.Any(decoration => decoration.Location == location) == true;

    private static IBrush? ToBrush(RichTextColor color) => color.IsSet
        ? new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B))
        : null;

    private static RichTextColor FromBrush(IBrush? brush) => brush is ISolidColorBrush solid
        ? new RichTextColor(solid.Color.R, solid.Color.G, solid.Color.B, solid.Color.A)
        : RichTextColor.None;
}
