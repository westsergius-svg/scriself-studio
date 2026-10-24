namespace ScriSelfStudio.Presentation.Export;

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScriSelfStudio.Core.Text;
using TextAlignment = ScriSelfStudio.Core.Text.TextAlignment;

/// <summary>Экспорт рукописи в DOCX (Office Open XML).</summary>
public static class DocxExportWriter
{
    private const int HalfPointsPerPoint = 2;
    private const int TwipsPerCentimeter = 567;

    public static void Write(ExportManuscript manuscript, string path, ExportOptions options)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        var body = new Body();
        mainPart.Document = new Document(body);

        var bodyFont = ResolveFont(options.FontFamily);
        var baseHalfPoints = PointsToHalfPoints(options.FontSize);

        if (options.IncludeTitlePage)
        {
            AppendTitlePage(body, manuscript, bodyFont);
        }

        if (options.TableOfContents)
        {
            AppendToc(body, manuscript, bodyFont);
        }

        var chapterNumber = 1;
        var firstChapter = true;
        foreach (var chapter in manuscript.Books.SelectMany(book => book.Chapters))
        {
            if (!firstChapter)
            {
                body.Append(CreatePageBreak());
            }

            firstChapter = false;
            body.Append(CreateParagraph(
                $"Глава {chapterNumber++}. {chapter.Title}",
                bodyFont,
                baseHalfPoints + 8,
                bold: true,
                alignment: TextAlignment.Center,
                spacingBefore: 0,
                spacingAfter: 240));

            var sceneNumber = 1;
            foreach (var scene in chapter.Scenes)
            {
                if (sceneNumber > 1)
                {
                    body.Append(CreatePageBreak());
                }

                body.Append(CreateParagraph(
                    $"Сцена {sceneNumber++}: {scene.Title}",
                    bodyFont,
                    baseHalfPoints + 2,
                    bold: true,
                    spacingBefore: 200,
                    spacingAfter: 120));

                foreach (var paragraph in scene.Paragraphs)
                {
                    body.Append(CreateParagraph(paragraph, bodyFont, baseHalfPoints, options.LineSpacing));
                }
            }
        }

        if (options.PageNumbering)
        {
            var footerPart = mainPart.AddNewPart<FooterPart>();
            footerPart.Footer = CreateFooter();
            body.Append(new SectionProperties(
                new FooterReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(footerPart) },
                CreatePageSize(),
                CreatePageMargins()));
        }
        else
        {
            body.Append(new SectionProperties(CreatePageSize(), CreatePageMargins()));
        }

        mainPart.Document.Save();
    }

    private static void AppendTitlePage(Body body, ExportManuscript manuscript, string font)
    {
        body.Append(CreateParagraph(
            manuscript.Title,
            font,
            36,
            bold: true,
            alignment: TextAlignment.Center,
            spacingBefore: 3600,
            spacingAfter: 240));

        if (!string.IsNullOrWhiteSpace(manuscript.Subtitle))
        {
            body.Append(CreateParagraph(
                manuscript.Subtitle,
                font,
                24,
                alignment: TextAlignment.Center,
                spacingAfter: 240));
        }

        body.Append(CreateParagraph(
            "ScriSelf Studio",
            font,
            20,
            alignment: TextAlignment.Center,
            spacingBefore: 2400));
        body.Append(CreatePageBreak());
    }

    private static void AppendToc(Body body, ExportManuscript manuscript, string font)
    {
        body.Append(CreateParagraph(
            "Содержание",
            font,
            32,
            bold: true,
            alignment: TextAlignment.Center,
            spacingAfter: 240));

        var chapterNumber = 1;
        foreach (var chapter in manuscript.Books.SelectMany(book => book.Chapters))
        {
            body.Append(CreateParagraph(
                $"Глава {chapterNumber++}. {chapter.Title}",
                font,
                24,
                spacingAfter: 60));
        }

        body.Append(CreatePageBreak());
    }

    private static Paragraph CreateParagraph(
        ExportParagraph paragraph,
        string font,
        int halfPoints,
        double lineSpacing)
    {
        var isHeading = paragraph.Kind == ExportBlockKind.Heading;
        if (isHeading)
        {
            halfPoints += 4;
        }

        var properties = new ParagraphProperties();
        if (paragraph.Kind is ExportBlockKind.Bullet or ExportBlockKind.Numbered)
        {
            properties.Append(new Indentation { Left = "567", Hanging = "284" });
        }
        else if (paragraph.Kind == ExportBlockKind.Quote)
        {
            properties.Append(new Indentation { Left = "1134", Right = "567" });
        }

        properties.Append(new SpacingBetweenLines
        {
            Before = (isHeading ? 240 : 0).ToString(CultureInfo.InvariantCulture),
            Line = ((int)Math.Round(halfPoints * 10 * lineSpacing)).ToString(CultureInfo.InvariantCulture),
            LineRule = LineSpacingRuleValues.Auto,
            After = "120",
        });
        properties.Append(new Justification { Val = ToJustification(paragraph.Alignment) });

        var wordParagraph = new Paragraph(properties);
        if (paragraph.Kind == ExportBlockKind.Bullet)
        {
            wordParagraph.Append(CreateRun("• ", font, halfPoints, isHeading));
        }
        else if (paragraph.Kind == ExportBlockKind.Numbered)
        {
            wordParagraph.Append(CreateRun("1. ", font, halfPoints, isHeading));
        }

        foreach (var run in paragraph.Runs)
        {
            wordParagraph.Append(CreateRun(
                run.Text,
                font,
                halfPoints,
                run.Bold || isHeading,
                run.Italic,
                run.Underline,
                run.Strikethrough));
        }

        return wordParagraph;
    }

    private static Paragraph CreateParagraph(
        string text,
        string font,
        int halfPoints,
        bool bold = false,
        TextAlignment alignment = TextAlignment.Left,
        int spacingBefore = 0,
        int spacingAfter = 120)
    {
        var properties = new ParagraphProperties(
            new SpacingBetweenLines
            {
                Before = spacingBefore.ToString(CultureInfo.InvariantCulture),
                After = spacingAfter.ToString(CultureInfo.InvariantCulture),
            },
            new Justification { Val = ToJustification(alignment) });

        return new Paragraph(properties, CreateRun(text, font, halfPoints, bold));
    }

    private static Run CreateRun(
        string text,
        string font,
        int halfPoints,
        bool bold = false,
        bool italic = false,
        bool underline = false,
        bool strikethrough = false)
    {
        var properties = new RunProperties(
            new RunFonts
            {
                Ascii = font,
                HighAnsi = font,
                EastAsia = font,
                ComplexScript = font,
            });

        if (bold)
        {
            properties.Append(new Bold());
        }

        if (italic)
        {
            properties.Append(new Italic());
        }

        if (strikethrough)
        {
            properties.Append(new Strike());
        }

        properties.Append(new FontSize { Val = halfPoints.ToString(CultureInfo.InvariantCulture) });
        if (underline)
        {
            properties.Append(new Underline { Val = UnderlineValues.Single });
        }

        return new Run(properties, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static Paragraph CreatePageBreak() =>
        new(new Run(new Break { Type = BreakValues.Page }));

    private static Footer CreateFooter() =>
        new(new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("1")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End })));

    private static PageSize CreatePageSize() => new()
    {
        Width = (uint)(21 * TwipsPerCentimeter),
        Height = (uint)(29.7 * TwipsPerCentimeter),
    };

    private static PageMargin CreatePageMargins() => new()
    {
        Top = 2 * TwipsPerCentimeter,
        Bottom = 2 * TwipsPerCentimeter,
        Left = (int)(2.5 * TwipsPerCentimeter),
        Right = 2 * TwipsPerCentimeter,
        Header = 708,
        Footer = 708,
        Gutter = 0,
    };

    private static JustificationValues ToJustification(TextAlignment alignment) => alignment switch
    {
        TextAlignment.Center => JustificationValues.Center,
        TextAlignment.Right => JustificationValues.Right,
        TextAlignment.Justify => JustificationValues.Both,
        _ => JustificationValues.Left,
    };

    private static int PointsToHalfPoints(double points) =>
        Math.Max(HalfPointsPerPoint, (int)Math.Round(points * HalfPointsPerPoint));

    /// <summary>Word должен получить существующее семейство шрифтов.</summary>
    private static string ResolveFont(string fontFamily) => fontFamily switch
    {
        "Georgia" => "Georgia",
        "Times New Roman" => "Times New Roman",
        "Consolas" => "Consolas",
        "Segoe UI" => "Segoe UI",
        "Inter" => "Segoe UI",
        _ => "Georgia",
    };
}
