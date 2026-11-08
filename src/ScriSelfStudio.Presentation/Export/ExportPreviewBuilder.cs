namespace ScriSelfStudio.Presentation.Export;

using ScriSelfStudio.Core.Text;

/// <summary>Абзац предпросмотра с теми же данными и настройками, что у экспорта.</summary>
public sealed record ExportPreviewBlock(
    IReadOnlyList<ExportRun> Runs,
    string FontFamily,
    double FontSize,
    double LineHeight,
    TextAlignment Alignment = TextAlignment.Left)
{
    public string Text => string.Concat(Runs.Select(run => run.Text));
}

public static class ExportPreviewBuilder
{
    public static IReadOnlyList<ExportPreviewBlock> Build(ExportManuscript manuscript, ExportOptions options, string format)
    {
        // Текстовые форматы показывают точное содержимое будущего файла, включая Markdown.
        if (format is "TXT" or "Markdown")
        {
            var text = format == "Markdown"
                ? TextExportWriter.BuildMarkdown(manuscript, options)
                : TextExportWriter.BuildTxt(manuscript, options);
            return [new([new(text)], "Consolas", 13, 19)];
        }

        var result = new List<ExportPreviewBlock>();
        // Размеры экспорта заданы в пунктах; Avalonia использует 96 dpi.
        void Add(IReadOnlyList<ExportRun> runs, double size, TextAlignment alignment = TextAlignment.Left) =>
            result.Add(new(runs, options.FontFamily, size * 4 / 3, size * 4 / 3 * options.LineSpacing, alignment));
        void Heading(string text, double size, TextAlignment alignment = TextAlignment.Left) =>
            Add([new(text, Bold: true)], size, alignment);

        if (options.IncludeTitlePage)
        {
            Heading(manuscript.Title, options.FontSize + 12, TextAlignment.Center);
            if (!string.IsNullOrWhiteSpace(manuscript.Subtitle))
                Add([new(manuscript.Subtitle)], options.FontSize + 2, TextAlignment.Center);
        }
        if (options.TableOfContents)
        {
            Heading("Содержание", options.FontSize + 8);
            var number = 1;
            foreach (var chapter in manuscript.Books.SelectMany(book => book.Chapters))
                Add([new($"Глава {number++}. {chapter.Title}")], options.FontSize);
        }

        var chapterNumber = 1;
        foreach (var chapter in manuscript.Books.SelectMany(book => book.Chapters))
        {
            Heading($"Глава {chapterNumber++}. {chapter.Title}", options.FontSize + 8);
            foreach (var scene in chapter.Scenes)
            {
                if (options.IncludeSceneHeadings)
                    Heading($"Сцена: {scene.Title}", options.FontSize + 2);
                foreach (var paragraph in scene.Paragraphs)
                {
                    IReadOnlyList<ExportRun> runs = paragraph.Kind switch
                    {
                        ExportBlockKind.Bullet => [new("• "), .. paragraph.Runs],
                        ExportBlockKind.Numbered => [new("1. "), .. paragraph.Runs],
                        ExportBlockKind.Quote => [.. paragraph.Runs.Select(run => run with { Italic = true })],
                        ExportBlockKind.Heading => [.. paragraph.Runs.Select(run => run with { Bold = true })],
                        _ => paragraph.Runs,
                    };
                    Add(runs, paragraph.Kind == ExportBlockKind.Heading ? options.FontSize + 2 : options.FontSize,
                        paragraph.Alignment);
                }
            }
        }
        if (options.IncludeCharacterCards && manuscript.Characters.Count > 0)
        {
            Heading("Персонажи", options.FontSize + 8);
            foreach (var character in manuscript.Characters)
            {
                Heading(character.Name, options.FontSize + 3);
                foreach (var field in character.Fields)
                    Add([new($"{field.Label}: ", Bold: true), new(field.Value)], options.FontSize);
            }
        }
        return result;
    }
}
