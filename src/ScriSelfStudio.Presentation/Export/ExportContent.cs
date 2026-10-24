namespace ScriSelfStudio.Presentation.Export;

using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Core.Text;

/// <summary>Вид абзаца в экспортируемой рукописи.</summary>
public enum ExportBlockKind
{
    Paragraph,
    Heading,
    Quote,
    Bullet,
    Numbered,
    Separator,
}

/// <summary>Фрагмент текста с одинаковым начертанием.</summary>
public sealed record ExportRun(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strikethrough = false);

/// <summary>Абзац рукописи, не зависящий от формата файла.</summary>
public sealed record ExportParagraph(
    ExportBlockKind Kind,
    IReadOnlyList<ExportRun> Runs,
    TextAlignment Alignment = TextAlignment.Left)
{
    public string PlainText => string.Concat(Runs.Select(run => run.Text));
}

public sealed record ExportScene(string Title, IReadOnlyList<ExportParagraph> Paragraphs);

public sealed record ExportChapter(string Title, IReadOnlyList<ExportScene> Scenes);

public sealed record ExportBook(string Title, IReadOnlyList<ExportChapter> Chapters);

/// <summary>Готовая рукопись для DOCX, PDF, EPUB, TXT и Markdown.</summary>
public sealed record ExportManuscript(string Title, string Subtitle, IReadOnlyList<ExportBook> Books);

/// <summary>Настройки оформления, общие для всех форматов экспорта.</summary>
public sealed record ExportOptions(
    string FontFamily,
    double FontSize,
    double LineSpacing,
    bool IncludeTitlePage,
    bool TableOfContents,
    bool PageNumbering);

/// <summary>
/// Собирает формат-нозависимую рукопись из структуры книги и документов сцен.
/// Форматирование фрагментов переносится в <see cref="ExportRun"/>; заголовки
/// остаются заголовками, а списки и цитаты — отдельными видами абзацев.
/// </summary>
public static class ManuscriptExportBuilder
{
    public static ExportManuscript Build(
        string title,
        IReadOnlyList<BookDto> books,
        Func<Guid, RichDocument?> readDocument,
        bool preserveFormatting)
    {
        var exportBooks = books
            .Select(book => new ExportBook(
                book.Title,
                book.Chapters
                    .Select(chapter => new ExportChapter(
                        chapter.Title,
                        chapter.Scenes
                            .Select(scene => new ExportScene(
                                scene.Title,
                                BuildParagraphs(readDocument(scene.Id), preserveFormatting)))
                            .ToList()))
                    .ToList()))
            .ToList();

        var subtitle = books.FirstOrDefault()?.Title ?? string.Empty;
        return new ExportManuscript(title, subtitle, exportBooks);
    }

    private static IReadOnlyList<ExportParagraph> BuildParagraphs(RichDocument? document, bool preserveFormatting)
    {
        if (document is null)
        {
            return [];
        }

        var paragraphs = new List<ExportParagraph>(document.Blocks.Count);
        foreach (var block in document.Blocks)
        {
            var kind = block.Kind switch
            {
                BlockKind.Heading1 or BlockKind.Heading2 or BlockKind.Heading3
                    or BlockKind.Heading4 or BlockKind.Heading5 or BlockKind.Heading6 => ExportBlockKind.Heading,
                BlockKind.Quote => ExportBlockKind.Quote,
                BlockKind.BulletListItem => ExportBlockKind.Bullet,
                BlockKind.NumberedListItem => ExportBlockKind.Numbered,
                BlockKind.Separator => ExportBlockKind.Separator,
                _ => ExportBlockKind.Paragraph,
            };

            if (kind == ExportBlockKind.Separator)
            {
                paragraphs.Add(new ExportParagraph(
                    kind,
                    [new ExportRun("* * *")],
                    TextAlignment.Center));
                continue;
            }

            var runs = new List<ExportRun>();
            foreach (var span in block.Spans)
            {
                if (span.Text.Length == 0)
                {
                    continue;
                }

                var flags = preserveFormatting ? span.Style.Flags : TextFormat.None;
                runs.Add(new ExportRun(
                    span.Text,
                    flags.HasFlag(TextFormat.Bold),
                    flags.HasFlag(TextFormat.Italic),
                    flags.HasFlag(TextFormat.Underline),
                    flags.HasFlag(TextFormat.Strikethrough)));
            }

            paragraphs.Add(new ExportParagraph(kind, runs, block.Alignment));
        }

        return paragraphs;
    }
}
