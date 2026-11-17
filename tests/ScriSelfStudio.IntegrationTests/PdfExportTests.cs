namespace ScriSelfStudio.IntegrationTests;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Presentation.Export;
using ScriSelfStudio.TestSupport;

/// <summary>Проверяет текст, шрифты и геометрию готового PDF, а не внутренний алгоритм writer.</summary>
public sealed class PdfExportTests
{
    private static readonly ExportOptions Options = new("Inter", 12, 1.15, false, false, false);

    [Fact]
    public void Pdf_keeps_mixed_styles_and_draws_decorations_on_the_corresponding_text()
    {
        using var workspace = new TempWorkspace();
        using var pdf = Write(workspace, "mixed-styles", [new("Scene", [new(ExportBlockKind.Paragraph,
        [
            new("Обычный "), new("Жирный ", Bold: true), new("Курсив ", Italic: true),
            new("Подчёркнутый ", Underline: true), new("Зачёркнутый", Strikethrough: true),
        ])])]);
        var page = ReadPage(pdf.Pages[0]);
        Assert.Contains("Обычный Жирный Курсив Подчёркнутый Зачёркнутый", page.Text);
        var regular = Assert.Single(page.Fragments, fragment => fragment.Text == "Обычный ");
        var bold = Assert.Single(page.Fragments, fragment => fragment.Text == "Жирный ");
        var italic = Assert.Single(page.Fragments, fragment => fragment.Text == "Курсив ");
        Assert.DoesNotContain("Bold", regular.Font, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bold", bold.Font, StringComparison.OrdinalIgnoreCase);
        Assert.True(italic.Slanted || italic.Font.Contains("Italic", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(regular.Y, bold.Y, 3);
        Assert.Equal(regular.Y, italic.Y, 3);
        var underline = Assert.Single(page.Fragments, fragment => fragment.Text == "Подчёркнутый ");
        var strike = Assert.Single(page.Fragments, fragment => fragment.Text == "Зачёркнутый");
        Assert.Contains(page.Lines, line => Math.Abs(line.X - underline.X) < 0.1
            && line.EndX > underline.X + 30 && line.Y < underline.Y && line.Y > underline.Y - 3);
        Assert.Contains(page.Lines, line => Math.Abs(line.X - strike.X) < 0.1
            && line.EndX > strike.X + 30 && line.Y > strike.Y && line.Y < strike.Y + 5);
    }

    [Fact]
    public void Pdf_numbers_consecutive_items_and_restarts_after_a_paragraph_or_scene()
    {
        using var workspace = new TempWorkspace();
        using var pdf = Write(workspace, "numbered-lists",
        [
            new("First scene",
            [
                Paragraph("FIRST", ExportBlockKind.Numbered), Paragraph("SECOND", ExportBlockKind.Numbered),
                Paragraph("Break"), Paragraph("RESTART", ExportBlockKind.Numbered),
                Paragraph("CONTINUE", ExportBlockKind.Numbered),
            ]),
            new("Second scene",
            [
                Paragraph("NEW SCENE", ExportBlockKind.Numbered), Paragraph("NEXT", ExportBlockKind.Numbered),
                Paragraph("Bullet", ExportBlockKind.Bullet), Paragraph("AFTER BULLET", ExportBlockKind.Numbered),
            ]),
        ]);
        var text = ReadPage(pdf.Pages[0]).Text;
        foreach (var expected in new[] { "1. FIRST", "2. SECOND", "1. RESTART", "2. CONTINUE", "1. NEW SCENE", "2. NEXT", "• Bullet", "1. AFTER BULLET" })
        {
            Assert.Contains(expected, text);
        }
    }

    [Fact]
    public void Pdf_wraps_styled_long_words_and_keeps_all_text_across_pages()
    {
        using var workspace = new TempWorkspace();
        var words = string.Join(" ", Enumerable.Range(0, 600).Select(index => $"слово{index:D3}"));
        var longWord = new string('Ж', 160) + new string('W', 160);
        using var pdf = Write(workspace, "page-wrapping", [new("Scene",
        [
            new(ExportBlockKind.Paragraph, [new(words, Italic: true)]),
            new(ExportBlockKind.Paragraph, [new(longWord[..160], Bold: true), new(longWord[160..], Italic: true)]),
            Paragraph("END OF MANUSCRIPT"),
        ])]);
        Assert.True(pdf.PageCount >= 3);
        var pages = pdf.Pages.Cast<PdfPage>().Select(ReadPage).ToArray();
        var text = string.Concat(pages.Select(page => page.Text));
        Assert.EndsWith(RemoveWhitespace(words + longWord + "END OF MANUSCRIPT"), RemoveWhitespace(text));
        Assert.All(pages.SelectMany(page => page.Fragments), fragment =>
        {
            Assert.InRange(fragment.X, 56, 539);
            Assert.InRange(fragment.Y, 56, 786);
        });
    }

    [Fact]
    public void Pdf_respects_alignment_hard_breaks_and_a_style_boundary_inside_a_word()
    {
        using var workspace = new TempWorkspace();
        using var pdf = Write(workspace, "alignment", [new("Scene",
        [
            Paragraph("LEFT"), Paragraph("CENTER", alignment: TextAlignment.Center),
            Paragraph("RIGHT", alignment: TextAlignment.Right),
            new(ExportBlockKind.Paragraph, [new("BOUND", Bold: true), new("ARY", Italic: true), new("\nHARD BREAK")]),
            Paragraph(string.Join(" ", Enumerable.Repeat("Justified words", 15)), alignment: TextAlignment.Justify),
        ])]);
        var page = ReadPage(pdf.Pages[0]);
        Assert.InRange(Assert.Single(page.Fragments, fragment => fragment.Text == "LEFT").X, 56, 57);
        Assert.InRange(Assert.Single(page.Fragments, fragment => fragment.Text == "CENTER").X, 220, 320);
        Assert.InRange(Assert.Single(page.Fragments, fragment => fragment.Text == "RIGHT").X, 480, 539);
        var first = Assert.Single(page.Fragments, fragment => fragment.Text == "BOUND");
        var second = Assert.Single(page.Fragments, fragment => fragment.Text == "ARY");
        Assert.Equal(first.Y, second.Y, 3);
        Assert.True(second.X > first.X);
        Assert.True(Assert.Single(page.Fragments, fragment => fragment.Text == "HARD BREAK").Y < first.Y - 10);
        var justified = page.Fragments.Where(fragment => fragment.Text == "Justified").ToArray();
        Assert.True(justified.Length > 1);
        Assert.Contains(page.Fragments, fragment => Math.Abs(fragment.Y - justified[0].Y) < 0.01 && fragment.X > 470);
    }

    [Fact]
    public void Pdf_title_contents_and_character_cards_preserve_their_text()
    {
        using var workspace = new TempWorkspace();
        using var pdf = Write(workspace, "complete-book", [new("Сцена", [Paragraph("Текст рукописи")])],
            Options with { IncludeTitlePage = true, TableOfContents = true, PageNumbering = true, IncludeCharacterCards = true });
        var text = string.Concat(pdf.Pages.Cast<PdfPage>().Select(page => ReadPage(page).Text));
        Assert.Contains("Книга для проверки", text);
        Assert.Contains("Подзаголовок", text);
        Assert.Contains("Содержание", text);
        Assert.Contains("Текст рукописи", text);
        Assert.Contains("Анна", text);
        Assert.Contains("Характер: Сдержанная", text);
        Assert.True(pdf.PageCount >= 4);
    }

    private static ExportParagraph Paragraph(string text, ExportBlockKind kind = ExportBlockKind.Paragraph,
        TextAlignment alignment = TextAlignment.Left) => new(kind, [new(text)], alignment);

    private static PdfDocument Write(TempWorkspace workspace, string name, IReadOnlyList<ExportScene> scenes,
        ExportOptions? options = null)
    {
        var manuscript = new ExportManuscript("Книга для проверки", "Подзаголовок", [new("Book", [new("Глава", scenes)])])
        { Characters = [new("Анна", [new("Характер", "Сдержанная")])] };
        var path = workspace.FilePath(name + ".pdf");
        PdfExportWriter.Write(manuscript, path, options ?? Options);
        // По запросу проверяющего сохраняем готовые файлы для независимой визуальной проверки.
        var artifactDirectory = Environment.GetEnvironmentVariable("SCRISELF_AUDIT_PDF_OUTPUT_DIR");
        if (!string.IsNullOrEmpty(artifactDirectory))
        {
            Directory.CreateDirectory(artifactDirectory);
            File.Copy(path, Path.Combine(artifactDirectory, name + ".pdf"), overwrite: true);
        }
        return PdfReader.Open(path, PdfDocumentOpenMode.Import);
    }

    private sealed record Fragment(string Text, string Font, double X, double Y, bool Slanted);
    private sealed record Line(double X, double Y, double EndX, double EndY);
    private sealed record Page(IReadOnlyList<Fragment> Fragments, IReadOnlyList<Line> Lines)
    {
        public string Text => string.Concat(Fragments.Select(fragment => fragment.Text));
    }

    private static Page ReadPage(PdfPage page)
    {
        var fonts = page.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
        var maps = fonts.Elements.Keys.ToDictionary(key => key,
            key => ReadUnicodeMap(fonts.Elements.GetDictionary(key)!));
        var fragments = new List<Fragment>();
        var lines = new List<Line>();
        string? font = null;
        double x = 0, y = 0, shear = 0, lineX = 0, lineY = 0;
        foreach (var operation in ContentReader.ReadContent(page).OfType<COperator>())
        {
            double Number(int index) => double.Parse(operation.Operands[index].ToString()!, CultureInfo.InvariantCulture);
            switch (operation.Name)
            {
                case "BT": x = 0; y = 0; shear = 0; break;
                case "Tf": font = ((CName)operation.Operands[0]).Name; break;
                case "Tm": x = Number(4); y = Number(5); shear = Number(2); break;
                case "Td": x += Number(0); y += Number(1); break;
                case "Tj":
                    Assert.NotNull(font);
                    var encoded = ((CString)operation.Operands[0]).Value;
                    var decoded = new StringBuilder();
                    for (var index = 0; index < encoded.Length; index += 2)
                    {
                        var cid = (encoded[index] << 8) | encoded[index + 1];
                        Assert.True(maps[font].TryGetValue(cid, out var value), $"No Unicode mapping for {cid:X4}");
                        decoded.Append(value);
                    }
                    fragments.Add(new(decoded.ToString(), fonts.Elements.GetDictionary(font)!.Elements.GetName("/BaseFont"), x, y, Math.Abs(shear) > 0.01));
                    break;
                case "m": lineX = Number(0); lineY = Number(1); break;
                case "l": lines.Add(new(lineX, lineY, Number(0), Number(1))); break;
            }
        }
        return new(fragments, lines);
    }

    private static Dictionary<int, string> ReadUnicodeMap(PdfDictionary font)
    {
        var map = Encoding.ASCII.GetString(font.Elements.GetDictionary("/ToUnicode")!.Stream.UnfilteredValue);
        var result = new Dictionary<int, string>();
        foreach (Match match in Regex.Matches(map, @"<([\dA-Fa-f]{4})>\s*<([\dA-Fa-f]{4})>\s*<([\dA-Fa-f]+)>"))
        {
            var start = int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var end = int.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var unicode = Convert.FromHexString(match.Groups[3].Value);
            Assert.Equal(start, end); // PDFsharp записывает отдельную строку диапазона для каждого CID.
            result[start] = Encoding.BigEndianUnicode.GetString(unicode);
        }
        return result;
    }

    private static string RemoveWhitespace(string text) => Regex.Replace(text, @"\s", string.Empty);
}
