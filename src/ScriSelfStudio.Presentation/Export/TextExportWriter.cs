namespace ScriSelfStudio.Presentation.Export;

using System.Text;

/// <summary>Экспорт в простой текст и Markdown.</summary>
public static class TextExportWriter
{
    public static void WriteTxt(ExportManuscript manuscript, string path, ExportOptions options)
        => File.WriteAllText(path, BuildTxt(manuscript, options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    public static void WriteMarkdown(ExportManuscript manuscript, string path, ExportOptions options)
        => File.WriteAllText(path, BuildMarkdown(manuscript, options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    public static string BuildTxt(ExportManuscript manuscript, ExportOptions options)
    {
        var builder = new StringBuilder();
        WriteTitlePage(builder, manuscript, options);
        WriteToc(builder, manuscript, options, markdown: false);

        var chapterNumber = 1;
        foreach (var book in manuscript.Books)
        {
            foreach (var chapter in book.Chapters)
            {
                builder.AppendLine($"Глава {chapterNumber++}. {chapter.Title}");
                builder.AppendLine();

                var sceneNumber = 1;
                foreach (var scene in chapter.Scenes)
                {
                    builder.AppendLine($"Сцена {sceneNumber++}: {scene.Title}");
                    foreach (var paragraph in scene.Paragraphs)
                    {
                        builder.AppendLine(FormatPlain(paragraph));
                    }

                    builder.AppendLine();
                }
            }
        }

        return builder.ToString();
    }

    public static string BuildMarkdown(ExportManuscript manuscript, ExportOptions options)
    {
        var builder = new StringBuilder();
        WriteTitlePage(builder, manuscript, options);
        WriteToc(builder, manuscript, options, markdown: true);

        var chapterNumber = 1;
        foreach (var book in manuscript.Books)
        {
            foreach (var chapter in book.Chapters)
            {
                builder.AppendLine($"# Глава {chapterNumber++}. {chapter.Title}");
                builder.AppendLine();

                var sceneNumber = 1;
                foreach (var scene in chapter.Scenes)
                {
                    builder.AppendLine($"## Сцена {sceneNumber++}: {scene.Title}");
                    builder.AppendLine();
                    foreach (var paragraph in scene.Paragraphs)
                    {
                        builder.AppendLine(FormatMarkdown(paragraph));
                        builder.AppendLine();
                    }
                }
            }
        }

        return builder.ToString();
    }

    private static void WriteTitlePage(StringBuilder builder, ExportManuscript manuscript, ExportOptions options)
    {
        if (!options.IncludeTitlePage)
        {
            return;
        }

        builder.AppendLine(manuscript.Title.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(manuscript.Subtitle))
        {
            builder.AppendLine(manuscript.Subtitle);
        }

        builder.AppendLine();
        builder.AppendLine();
    }

    private static void WriteToc(StringBuilder builder, ExportManuscript manuscript, ExportOptions options, bool markdown)
    {
        if (!options.TableOfContents)
        {
            return;
        }

        if (markdown)
        {
            builder.AppendLine("## Содержание");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("--- СОДЕРЖАНИЕ ---");
        }

        var chapterNumber = 1;
        foreach (var chapter in manuscript.Books.SelectMany(book => book.Chapters))
        {
            builder.AppendLine($"{(markdown ? "- " : string.Empty)}Глава {chapterNumber++}. {chapter.Title}");
        }

        builder.AppendLine();
    }

    private static string FormatPlain(ExportParagraph paragraph)
    {
        var text = paragraph.PlainText;
        return paragraph.Kind switch
        {
            ExportBlockKind.Bullet => $"• {text}",
            ExportBlockKind.Numbered => $"1. {text}",
            ExportBlockKind.Quote => $"«{text}»",
            _ => text,
        };
    }

    private static string FormatMarkdown(ExportParagraph paragraph)
    {
        var text = string.Concat(paragraph.Runs.Select(FormatRun));
        return paragraph.Kind switch
        {
            ExportBlockKind.Bullet => $"- {text}",
            ExportBlockKind.Numbered => $"1. {text}",
            ExportBlockKind.Quote => $"> {text}",
            ExportBlockKind.Heading => $"### {text}",
            ExportBlockKind.Separator => "---",
            _ => text,
        };
    }

    private static string FormatRun(ExportRun run)
    {
        var text = run.Text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        if (run.Strikethrough)
        {
            text = $"~~{text}~~";
        }

        if (run.Underline)
        {
            text = $"<u>{text}</u>";
        }

        if (run.Italic)
        {
            text = $"*{text}*";
        }

        if (run.Bold)
        {
            text = $"**{text}**";
        }

        return text;
    }
}
