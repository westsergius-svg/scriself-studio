namespace ScriSelfStudio.Presentation.Export;

using System.Globalization;
using System.IO.Compression;
using System.Text;

/// <summary>
/// Экспорт в EPUB 3 (с резервным toc.ncx для старых читалок). Контейнер
/// собирается вручную: зависимости нужны только стандартные средства ZIP.
/// </summary>
public static class EpubExportWriter
{
    public static void Write(ExportManuscript manuscript, string path, ExportOptions options)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        WriteEntry(archive, "META-INF/container.xml", ContainerXml, CompressionLevel.Optimal);

        var chapters = new List<(string FileName, string Title, string Xhtml)>();
        var chapterNumber = 1;
        foreach (var chapter in manuscript.Books.SelectMany(book => book.Chapters))
        {
            var title = $"Глава {chapterNumber}. {chapter.Title}";
            var fileName = $"chapter{chapterNumber.ToString("D3", CultureInfo.InvariantCulture)}.xhtml";
            chapters.Add((fileName, title, BuildChapterXhtml(title, chapter)));
            chapterNumber++;
        }

        WriteEntry(archive, "OEBPS/style.css", StyleCss(options), CompressionLevel.Optimal);
        if (options.IncludeTitlePage)
        {
            WriteEntry(archive, "OEBPS/title.xhtml", BuildTitleXhtml(manuscript), CompressionLevel.Optimal);
        }

        foreach (var chapter in chapters)
        {
            WriteEntry(archive, "OEBPS/" + chapter.FileName, chapter.Xhtml, CompressionLevel.Optimal);
        }

        // nav.xhtml обязателен для EPUB 3 независимо от того, показываем ли мы
        // его в оглавлении устройства.
        WriteEntry(archive, "OEBPS/nav.xhtml", BuildNavXhtml(manuscript, chapters), CompressionLevel.Optimal);

        WriteEntry(archive, "OEBPS/toc.ncx", BuildNcx(manuscript, chapters), CompressionLevel.Optimal);
        WriteEntry(archive, "OEBPS/content.opf", BuildPackage(manuscript, chapters, options), CompressionLevel.Optimal);
    }

    private const string ContainerXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """;

    private static string BuildPackage(
        ExportManuscript manuscript,
        IReadOnlyList<(string FileName, string Title, string Xhtml)> chapters,
        ExportOptions options)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"book-id\">");
        builder.AppendLine("  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
        builder.AppendLine($"    <dc:identifier id=\"book-id\">urn:uuid:{Guid.NewGuid():D}</dc:identifier>");
        builder.AppendLine($"    <dc:title>{Xml(manuscript.Title)}</dc:title>");
        builder.AppendLine("    <dc:language>ru</dc:language>");
        builder.AppendLine("    <dc:creator>ScriSelf Studio</dc:creator>");
        builder.AppendLine("    <meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta>");
        builder.AppendLine("  </metadata>");
        builder.AppendLine("  <manifest>");
        builder.AppendLine("    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");
        builder.AppendLine("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>");
        builder.AppendLine("    <item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>");
        if (options.IncludeTitlePage)
        {
            builder.AppendLine("    <item id=\"title\" href=\"title.xhtml\" media-type=\"application/xhtml+xml\"/>");
        }

        for (var index = 0; index < chapters.Count; index++)
        {
            builder.AppendLine(
                $"    <item id=\"chapter{index + 1}\" href=\"{chapters[index].FileName}\" media-type=\"application/xhtml+xml\"/>");
        }

        builder.AppendLine("  </manifest>");
        builder.AppendLine("  <spine toc=\"ncx\">");
        if (options.IncludeTitlePage)
        {
            builder.AppendLine("    <itemref idref=\"title\"/>");
        }

        if (options.TableOfContents)
        {
            builder.AppendLine("    <itemref idref=\"nav\"/>");
        }

        for (var index = 0; index < chapters.Count; index++)
        {
            builder.AppendLine($"    <itemref idref=\"chapter{index + 1}\"/>");
        }

        builder.AppendLine("  </spine>");
        builder.AppendLine("</package>");
        return builder.ToString();
    }

    private static string BuildNavXhtml(
        ExportManuscript manuscript,
        IReadOnlyList<(string FileName, string Title, string Xhtml)> chapters)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xml:lang=\"ru\" lang=\"ru\">");
        builder.AppendLine($"<head><title>Содержание — {Xml(manuscript.Title)}</title></head>");
        builder.AppendLine("<body><nav epub:type=\"toc\" id=\"toc\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><h1>Содержание</h1><ol>");
        foreach (var chapter in chapters)
        {
            builder.AppendLine($"<li><a href=\"{chapter.FileName}\">{Xml(chapter.Title)}</a></li>");
        }

        builder.AppendLine("</ol></nav></body></html>");
        return builder.ToString();
    }

    private static string BuildNcx(
        ExportManuscript manuscript,
        IReadOnlyList<(string FileName, string Title, string Xhtml)> chapters)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">");
        builder.AppendLine($"<head><meta name=\"dtb:uid\" content=\"urn:uuid:{Guid.NewGuid():D}\"/></head>");
        builder.AppendLine($"<docTitle><text>{Xml(manuscript.Title)}</text></docTitle>");
        builder.AppendLine("<navMap>");
        for (var index = 0; index < chapters.Count; index++)
        {
            builder.AppendLine(
                $"<navPoint id=\"nav{index + 1}\" playOrder=\"{index + 1}\"><navLabel><text>{Xml(chapters[index].Title)}</text></navLabel><content src=\"{chapters[index].FileName}\"/></navPoint>");
        }

        builder.AppendLine("</navMap></ncx>");
        return builder.ToString();
    }

    private static string BuildTitleXhtml(ExportManuscript manuscript)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xml:lang=\"ru\" lang=\"ru\">");
        builder.AppendLine($"<head><title>{Xml(manuscript.Title)}</title><link rel=\"stylesheet\" href=\"style.css\" type=\"text/css\"/></head>");
        builder.AppendLine("<body class=\"titlepage\">");
        builder.AppendLine($"<h1>{Xml(manuscript.Title)}</h1>");
        if (!string.IsNullOrWhiteSpace(manuscript.Subtitle))
        {
            builder.AppendLine($"<p class=\"subtitle\">{Xml(manuscript.Subtitle)}</p>");
        }

        builder.AppendLine("<p class=\"publisher\">ScriSelf Studio</p>");
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static string BuildChapterXhtml(string title, ExportChapter chapter)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xml:lang=\"ru\" lang=\"ru\">");
        builder.AppendLine($"<head><title>{Xml(title)}</title><link rel=\"stylesheet\" href=\"style.css\" type=\"text/css\"/></head>");
        builder.AppendLine("<body>");
        builder.AppendLine($"<h1>{Xml(title)}</h1>");

        var sceneNumber = 1;
        foreach (var scene in chapter.Scenes)
        {
            builder.AppendLine($"<h2>Сцена {sceneNumber++}: {Xml(scene.Title)}</h2>");
            foreach (var paragraph in scene.Paragraphs)
            {
                var text = RenderParagraph(paragraph);
                var cssClass = paragraph.Kind switch
                {
                    ExportBlockKind.Quote => " class=\"quote\"",
                    ExportBlockKind.Bullet or ExportBlockKind.Numbered => " class=\"list\"",
                    ExportBlockKind.Separator => " class=\"separator\"",
                    _ => string.Empty,
                };
                var tag = paragraph.Kind switch
                {
                    ExportBlockKind.Quote => "blockquote",
                    ExportBlockKind.Heading => "h3",
                    _ => "p",
                };
                builder.AppendLine($"<{tag}{cssClass}>{text}</{tag}>");
            }
        }

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static string RenderParagraph(ExportParagraph paragraph)
    {
        var builder = new StringBuilder();
        foreach (var run in paragraph.Runs)
        {
            var text = Xml(run.Text);
            if (run.Strikethrough)
            {
                text = $"<s>{text}</s>";
            }

            if (run.Underline)
            {
                text = $"<u>{text}</u>";
            }

            if (run.Italic)
            {
                text = $"<em>{text}</em>";
            }

            if (run.Bold)
            {
                text = $"<strong>{text}</strong>";
            }

            builder.Append(text);
        }

        if (paragraph.Kind == ExportBlockKind.Bullet)
        {
            builder.Insert(0, "• ");
        }
        else if (paragraph.Kind == ExportBlockKind.Numbered)
        {
            builder.Insert(0, "1. ");
        }

        return builder.ToString();
    }

    private static string StyleCss(ExportOptions options)
    {
        var fontSize = options.FontSize.ToString("0.##", CultureInfo.InvariantCulture);
        var lineHeight = options.LineSpacing.ToString("0.##", CultureInfo.InvariantCulture);
        return $$"""
            body { font-family: "{{options.FontFamily}}", Georgia, serif; font-size: {{fontSize}}pt; line-height: {{lineHeight}}; margin: 1em; text-align: justify; }
            h1 { font-size: 1.7em; text-align: center; margin: 2em 0 1.5em; page-break-before: always; }
            h2 { font-size: 1.2em; margin: 1.6em 0 0.8em; }
            p { margin: 0 0 0.7em; text-indent: 1.2em; }
            p.list { text-indent: 0; padding-left: 1.2em; }
            blockquote { margin: 0.8em 2em; font-style: italic; }
            p.separator { text-align: center; text-indent: 0; margin: 1.2em 0; }
            p.subtitle { text-align: center; font-size: 1.2em; text-indent: 0; }
            p.publisher { text-align: center; margin-top: 3em; text-indent: 0; }
            body.titlepage { text-align: center; }
            """;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content, CompressionLevel level)
    {
        var entry = archive.CreateEntry(name, level);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string Xml(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
