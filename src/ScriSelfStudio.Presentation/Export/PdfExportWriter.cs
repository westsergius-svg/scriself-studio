namespace ScriSelfStudio.Presentation.Export;

using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ScriSelfStudio.Core.Text;

/// <summary>
/// Экспорт в PDF: A4, поля 2 см, перенос строк по ширине страницы,
/// нумерация страниц и титульный лист.
/// </summary>
public static class PdfExportWriter
{
    private const double PageMargin = 56.7;
    private static readonly object FontLock = new();
    private static bool _fontsConfigured;

    public static void Write(ExportManuscript manuscript, string path, ExportOptions options)
    {
        EnsureFonts();

        using var document = new PdfDocument();
        document.Info.Title = manuscript.Title;
        document.Info.Creator = "ScriSelf Studio";

        var family = ResolveFont(options.FontFamily);
        var layout = new PdfLayout(document, family, options);

        if (options.IncludeTitlePage)
        {
            layout.WriteTitle(manuscript);
            layout.StartNewPage();
        }

        if (options.TableOfContents)
        {
            layout.WriteToc(manuscript);
            layout.StartNewPage();
        }

        var chapterNumber = 1;
        foreach (var chapter in manuscript.Books.SelectMany(book => book.Chapters))
        {
            layout.WriteHeading($"Глава {chapterNumber++}. {chapter.Title}", options.FontSize + 8);
            var sceneNumber = 1;
            foreach (var scene in chapter.Scenes)
            {
                if (sceneNumber > 1)
                {
                    layout.StartNewPage();
                }

                layout.WriteHeading($"Сцена {sceneNumber++}: {scene.Title}", options.FontSize + 2);
                foreach (var paragraph in scene.Paragraphs)
                {
                    layout.WriteParagraph(paragraph);
                }
            }
        }

        if (options.PageNumbering)
        {
            layout.Finish();
            layout.DrawPageNumbers(options.IncludeTitlePage);
        }

        document.Save(path);
    }

    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (_fontsConfigured)
            {
                return;
            }

            // PDFsharp ищет шрифты в системе; приложение Windows-only, поэтому
            // этого достаточно и не нужно встраивать файлы шрифтов в поставку.
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            _fontsConfigured = true;
        }
    }

    private static string ResolveFont(string fontFamily) => fontFamily switch
    {
        "Times New Roman" => "Times New Roman",
        "Consolas" => "Consolas",
        "Georgia" => "Georgia",
        "Segoe UI" => "Segoe UI",
        "Inter" => "Segoe UI",
        _ => "Georgia",
    };

    private sealed class PdfLayout
    {
        private readonly PdfDocument _document;
        private readonly string _family;
        private readonly ExportOptions _options;
        private readonly XGraphics _measure;
        private readonly double _contentWidth;
        private XGraphics? _graphics;
        private double _y;

        public PdfLayout(PdfDocument document, string family, ExportOptions options)
        {
            _document = document;
            _family = family;
            _options = options;
            _measure = XGraphics.CreateMeasureContext(
                new XSize(595.28, 841.89),
                XGraphicsUnit.Point,
                XPageDirection.Downwards);
            _contentWidth = 595.28 - (PageMargin * 2);
        }

        public void WriteTitle(ExportManuscript manuscript)
        {
            StartPageIfNeeded();
            var titleFont = CreateFont(28, bold: true);
            var subtitleFont = CreateFont(16);
            DrawLine(manuscript.Title, titleFont, TextAlignment.Center, PageMargin, 220);
            if (!string.IsNullOrWhiteSpace(manuscript.Subtitle))
            {
                DrawLine(manuscript.Subtitle, subtitleFont, TextAlignment.Center, PageMargin, 16);
            }

            DrawLine("ScriSelf Studio", CreateFont(11), TextAlignment.Center, PageMargin, 180);
        }

        public void WriteToc(ExportManuscript manuscript)
        {
            WriteHeading("Содержание", _options.FontSize + 8);
            var chapterNumber = 1;
            foreach (var chapter in manuscript.Books.SelectMany(book => book.Chapters))
            {
                WriteParagraph(
                    new ExportParagraph(
                        ExportBlockKind.Paragraph,
                        [new ExportRun($"Глава {chapterNumber++}. {chapter.Title}")]),
                    indent: 0);
            }
        }

        public void WriteHeading(string text, double size)
        {
            EnsureSpace(size * 2.2);
            _y += size * 0.6;
            DrawLine(text, CreateFont(size, bold: true), TextAlignment.Center, PageMargin, size * 1.35);
            _y += size * 0.5;
        }

        public void WriteParagraph(ExportParagraph paragraph, double indent = 0)
        {
            var isHeading = paragraph.Kind == ExportBlockKind.Heading;
            var size = _options.FontSize + (isHeading ? 4 : 0);
            var font = CreateFont(size, isHeading || IsMostlyBold(paragraph));
            var lineHeight = size * 1.2 * _options.LineSpacing;
            var prefix = paragraph.Kind switch
            {
                ExportBlockKind.Bullet => "• ",
                ExportBlockKind.Numbered => "1. ",
                _ => string.Empty,
            };
            var textIndent = paragraph.Kind == ExportBlockKind.Quote ? 24 : indent;
            var alignment = paragraph.Kind == ExportBlockKind.Separator
                ? TextAlignment.Center
                : paragraph.Alignment;

            var text = prefix + paragraph.PlainText;
            foreach (var line in Wrap(text, font, _contentWidth - textIndent))
            {
                EnsureSpace(lineHeight);
                DrawLine(line, font, alignment, PageMargin + textIndent, lineHeight);
            }

            _y += lineHeight * 0.35;
        }

        public void StartNewPage()
        {
            _graphics?.Dispose();
            _graphics = null;
            _y = 0;
            AddPage();
        }

        public void Finish()
        {
            _graphics?.Dispose();
            _graphics = null;
        }

        public void DrawPageNumbers(bool skipFirst)
        {
            var font = CreateFont(9);
            for (var index = 0; index < _document.PageCount; index++)
            {
                if (skipFirst && index == 0)
                {
                    continue;
                }

                var page = _document.Pages[index];
                using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                graphics.DrawString(
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    font,
                    XBrushes.Black,
                    new XRect(0, page.Height.Point - PageMargin + 10, page.Width.Point, 20),
                    XStringFormats.TopCenter);
            }
        }

        private void StartPageIfNeeded()
        {
            if (_graphics is null)
            {
                AddPage();
            }
        }

        private void AddPage()
        {
            var page = _document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            _graphics = XGraphics.FromPdfPage(page);
            _y = PageMargin;
        }

        private void EnsureSpace(double height)
        {
            StartPageIfNeeded();
            if (_y + height <= 841.89 - PageMargin)
            {
                return;
            }

            StartNewPage();
        }

        private void DrawLine(
            string text,
            XFont font,
            TextAlignment alignment,
            double x,
            double lineHeight)
        {
            StartPageIfNeeded();
            var format = alignment switch
            {
                TextAlignment.Center => XStringFormats.TopCenter,
                TextAlignment.Right => XStringFormats.TopRight,
                _ => XStringFormats.TopLeft,
            };
            var width = 595.28 - PageMargin - x;
            _graphics!.DrawString(text, font, XBrushes.Black, new XRect(x, _y, width, lineHeight), format);
            _y += lineHeight;
        }

        private IEnumerable<string> Wrap(string text, XFont font, double width)
        {
            foreach (var hardLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (hardLine.Length == 0)
                {
                    yield return string.Empty;
                    continue;
                }

                var line = new System.Text.StringBuilder();
                foreach (var word in hardLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (_measure.MeasureString(candidate, font).Width <= width || line.Length == 0)
                    {
                        line.Clear();
                        line.Append(candidate);
                        continue;
                    }

                    yield return line.ToString();
                    line.Clear();
                    line.Append(word);
                }

                if (line.Length > 0)
                {
                    yield return line.ToString();
                }
            }
        }

        private XFont CreateFont(double size, bool bold = false)
        {
            var style = bold ? XFontStyleEx.Bold : XFontStyleEx.Regular;
            try
            {
                // Unicode-кодировка добавляет ToUnicode-карту: текст в готовом PDF
                // можно выделять и искать, а не только читать глазами.
                return new XFont(_family, size, style, XPdfFontOptions.UnicodeDefault);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return new XFont("Arial", size, style, XPdfFontOptions.UnicodeDefault);
            }
        }

        private static bool IsMostlyBold(ExportParagraph paragraph) =>
            paragraph.Runs.Count > 0 && paragraph.Runs.All(run => run.Bold);
    }
}
