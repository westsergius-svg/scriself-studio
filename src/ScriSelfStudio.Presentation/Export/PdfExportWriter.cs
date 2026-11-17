namespace ScriSelfStudio.Presentation.Export;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
        using var layout = new PdfLayout(document, family, options);

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
            foreach (var scene in chapter.Scenes)
            {
                layout.ResetListNumbering();
                if (options.IncludeSceneHeadings)
                {
                    layout.WriteHeading($"Сцена: {scene.Title}", options.FontSize + 2);
                }

                foreach (var paragraph in scene.Paragraphs)
                {
                    layout.WriteParagraph(paragraph);
                }
            }
        }

        if (options.IncludeCharacterCards && manuscript.Characters.Count > 0)
        {
            layout.StartNewPage();
            layout.WriteHeading("Персонажи", options.FontSize + 8);
            foreach (var character in manuscript.Characters)
            {
                layout.WriteHeading(character.Name, options.FontSize + 3);
                foreach (var field in character.Fields)
                {
                    layout.WriteParagraph(new ExportParagraph(
                        ExportBlockKind.Paragraph,
                        [new ExportRun($"{field.Label}: ", Bold: true), new ExportRun(field.Value)]));
                }
            }
        }

        if (options.PageNumbering)
        {
            layout.Finish();
            layout.DrawPageNumbers(options.IncludeTitlePage);
        }

        layout.Finish();
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

            if (OperatingSystem.IsWindows())
            {
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;
                GlobalFontSettings.FallbackFontResolver ??= new PortableFontResolver();
            }
            else
            {
                var fontDirectory = OperatingSystem.IsMacOS()
                    ? "/System/Library/Fonts/Supplemental" : null;
                GlobalFontSettings.FontResolver ??= new PortableFontResolver(fontDirectory);
            }
            _fontsConfigured = true;
        }
    }

    private static string ResolveFont(string fontFamily) => fontFamily switch
    {
        "Times New Roman" => "Times New Roman",
        "Consolas" => "Consolas",
        "Georgia" => "Georgia",
        "Segoe UI" => "Segoe UI",
        "Inter" => "Inter",
        _ => "Georgia",
    };

    private sealed class PdfLayout : IDisposable
    {
        private readonly PdfDocument _document;
        private readonly string _family;
        private readonly ExportOptions _options;
        private readonly XGraphics _measure;
        private readonly double _contentWidth;
        private XGraphics? _graphics;
        private double _y;
        private int _numberedItem;
        private readonly Dictionary<(double Size, bool Bold, bool Italic), XFont> _fonts = [];

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
            ResetListNumbering();
            EnsureSpace(size * 2.2);
            _y += size * 0.6;
            DrawLine(text, CreateFont(size, bold: true), TextAlignment.Center, PageMargin, size * 1.35);
            _y += size * 0.5;
        }

        public void WriteParagraph(ExportParagraph paragraph, double indent = 0)
        {
            var isHeading = paragraph.Kind == ExportBlockKind.Heading;
            var size = _options.FontSize + (isHeading ? 4 : 0);
            var lineHeight = size * 1.2 * _options.LineSpacing;
            _numberedItem = paragraph.Kind == ExportBlockKind.Numbered ? _numberedItem + 1 : 0;
            var prefix = paragraph.Kind switch
            {
                ExportBlockKind.Bullet => "• ",
                ExportBlockKind.Numbered => _numberedItem.ToString(CultureInfo.InvariantCulture) + ". ",
                _ => string.Empty,
            };
            var textIndent = paragraph.Kind == ExportBlockKind.Quote ? 24 : indent;
            var alignment = paragraph.Kind == ExportBlockKind.Separator
                ? TextAlignment.Center
                : paragraph.Alignment;

            var marker = new StyledPiece(prefix, CreateFont(size), false, false);
            var markerWidth = _measure.MeasureString(prefix, marker.Font).Width;
            var contentWidth = _contentWidth - textIndent - markerWidth;
            var firstLine = true;
            foreach (var line in WrapRuns(paragraph.Runs, size, isHeading, contentWidth))
            {
                EnsureSpace(lineHeight);
                var x = PageMargin + textIndent + markerWidth;
                if (firstLine && prefix.Length > 0 && alignment is TextAlignment.Center or TextAlignment.Right)
                {
                    DrawRichLine([marker, .. line.Pieces], alignment, PageMargin + textIndent,
                        contentWidth + markerWidth, lineHeight, line.SoftBreak);
                }
                else
                {
                    if (firstLine && prefix.Length > 0)
                    {
                        DrawRichLine([marker], TextAlignment.Left, PageMargin + textIndent, markerWidth, lineHeight, false, advance: false);
                    }
                    DrawRichLine(line.Pieces, alignment, x, contentWidth, lineHeight, line.SoftBreak);
                }
                firstLine = false;
            }

            _y += lineHeight * 0.35;
        }

        public void ResetListNumbering() => _numberedItem = 0;

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

        private sealed record StyledPiece(string Text, XFont Font, bool Underline, bool Strikethrough);
        private sealed record RichToken(IReadOnlyList<StyledPiece> Pieces, bool Space, bool NewLine = false);
        private sealed record RichLine(IReadOnlyList<StyledPiece> Pieces, bool SoftBreak);

        private double Measure(IEnumerable<StyledPiece> pieces) =>
            pieces.Sum(piece => _measure.MeasureString(piece.Text, piece.Font).Width);

        private static void Append(List<StyledPiece> destination, StyledPiece piece)
        {
            if (piece.Text.Length == 0) { return; }
            if (destination.Count > 0 && destination[^1] is var last
                && ReferenceEquals(last.Font, piece.Font) && last.Underline == piece.Underline
                && last.Strikethrough == piece.Strikethrough)
            {
                destination[^1] = last with { Text = last.Text + piece.Text };
            }
            else { destination.Add(piece); }
        }

        private static void Append(List<StyledPiece> destination, IEnumerable<StyledPiece> pieces)
        {
            foreach (var piece in pieces) { Append(destination, piece); }
        }

        // Слово может пересекать границу начертаний; эта граница не является переносом.
        private IEnumerable<RichToken> Tokens(IReadOnlyList<ExportRun> runs, double size, bool heading)
        {
            var pieces = new List<StyledPiece>();
            bool? space = null;
            foreach (var run in runs)
            {
                var font = CreateFont(size, heading || run.Bold, run.Italic);
                var segment = new StringBuilder();
                var elements = StringInfo.GetTextElementEnumerator(run.Text
                    .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
                while (elements.MoveNext())
                {
                    var element = elements.GetTextElement();
                    var isSpace = element is not "\u00A0" and not "\u202F" && char.IsWhiteSpace(element[0]);
                    if (element == "\n" || (space.HasValue && space.Value != isSpace))
                    {
                        Append(pieces, new StyledPiece(segment.ToString(), font, run.Underline, run.Strikethrough));
                        segment.Clear();
                        if (pieces.Count > 0) { yield return new RichToken(pieces.ToArray(), space ?? false); }
                        pieces.Clear();
                        space = null;
                    }
                    if (element == "\n") { yield return new RichToken([], false, true); continue; }
                    space = isSpace;
                    segment.Append(element == "\t" ? "    " : element);
                }
                Append(pieces, new StyledPiece(segment.ToString(), font, run.Underline, run.Strikethrough));
            }
            if (pieces.Count > 0) { yield return new RichToken(pieces.ToArray(), space ?? false); }
        }

        private IEnumerable<RichLine> WrapRuns(IReadOnlyList<ExportRun> runs, double size, bool heading, double width)
        {
            var line = new List<StyledPiece>();
            var spaces = new List<StyledPiece>();
            var lineWidth = 0d;
            foreach (var token in Tokens(runs, size, heading))
            {
                if (token.NewLine)
                {
                    Append(line, spaces);
                    yield return new RichLine(line.ToArray(), false);
                    line.Clear(); spaces.Clear(); lineWidth = 0;
                    continue;
                }
                if (token.Space) { Append(spaces, token.Pieces); continue; }
                var tokenWidth = Measure(token.Pieces);
                if (line.Count > 0 && lineWidth + Measure(spaces) + tokenWidth > width)
                {
                    yield return new RichLine(line.ToArray(), true);
                    line.Clear(); spaces.Clear(); lineWidth = 0;
                }
                Append(line, spaces); spaces.Clear();
                lineWidth = Measure(line);
                if (tokenWidth <= width)
                {
                    Append(line, token.Pieces);
                    lineWidth = Measure(line);
                    continue;
                }

                // Длинные слова и китайский текст разбиваются по целым графемам.
                foreach (var piece in token.Pieces)
                {
                    var elements = StringInfo.GetTextElementEnumerator(piece.Text);
                    while (elements.MoveNext())
                    {
                        var element = piece with { Text = elements.GetTextElement() };
                        var elementWidth = Measure([element]);
                        if (line.Count > 0 && lineWidth + elementWidth > width)
                        {
                            yield return new RichLine(line.ToArray(), true);
                            line.Clear(); lineWidth = 0;
                        }
                        Append(line, element);
                        lineWidth += elementWidth;
                    }
                }
            }
            Append(line, spaces);
            yield return new RichLine(line.ToArray(), false);
        }

        private void DrawRichLine(IReadOnlyList<StyledPiece> pieces, TextAlignment alignment,
            double x, double width, double lineHeight, bool softBreak, bool advance = true)
        {
            StartPageIfNeeded();
            var textWidth = Measure(pieces);
            x += alignment switch
            {
                TextAlignment.Center => (width - textWidth) / 2,
                TextAlignment.Right => width - textWidth,
                _ => 0,
            };
            var ascent = pieces.Count == 0 ? _options.FontSize : pieces.Max(piece =>
                piece.Font.GetHeight() * piece.Font.CellAscent / piece.Font.CellSpace);
            var baseline = _y + ascent;
            var justify = alignment == TextAlignment.Justify && softBreak;
            var spaceCount = justify ? pieces.Sum(piece => piece.Text.Count(character => character == ' ')) : 0;
            var extraSpace = spaceCount > 0 ? Math.Max(0, width - textWidth) / spaceCount : 0;
            var drawingPieces = justify ? pieces.SelectMany(piece =>
                Regex.Matches(piece.Text, " +|[^ ]+").Select(match => piece with { Text = match.Value })) : pieces;
            foreach (var piece in drawingPieces)
            {
                var pieceWidth = _measure.MeasureString(piece.Text, piece.Font).Width
                    + extraSpace * piece.Text.Count(character => character == ' ');
                _graphics!.DrawString(piece.Text, piece.Font, XBrushes.Black, new XPoint(x, baseline), XStringFormats.BaseLineLeft);
                var pen = new XPen(XColors.Black, Math.Max(0.5, piece.Font.Size / 16));
                if (piece.Underline) { _graphics.DrawLine(pen, x, baseline + piece.Font.Size * 0.1, x + pieceWidth, baseline + piece.Font.Size * 0.1); }
                if (piece.Strikethrough) { _graphics.DrawLine(pen, x, baseline - piece.Font.Size * 0.3, x + pieceWidth, baseline - piece.Font.Size * 0.3); }
                x += pieceWidth;
            }
            if (advance) { _y += lineHeight; }
        }

        private XFont CreateFont(double size, bool bold = false, bool italic = false)
        {
            var key = (size, bold, italic);
            if (_fonts.TryGetValue(key, out var cached)) { return cached; }
            var style = (bold, italic) switch
            {
                (true, true) => XFontStyleEx.BoldItalic,
                (true, false) => XFontStyleEx.Bold,
                (false, true) => XFontStyleEx.Italic,
                _ => XFontStyleEx.Regular,
            };
            XFont font;
            try
            {
                // Unicode-кодировка добавляет ToUnicode-карту: текст в готовом PDF
                // можно выделять и искать, а не только читать глазами.
                font = new XFont(_family, size, style, XPdfFontOptions.UnicodeDefault);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                font = new XFont("Arial", size, style, XPdfFontOptions.UnicodeDefault);
            }
            _fonts[key] = font;
            return font;
        }

        public void Dispose() { Finish(); _measure.Dispose(); }
    }
}
