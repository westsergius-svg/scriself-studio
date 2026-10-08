namespace ScriSelfStudio.EditorEngine;

using System.Globalization;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Core.Text;

/// <summary>
/// Подсчёт слов и символов (ТЗ п. 11). Учитываются кириллица, emoji и составные
/// символы: «символы» считаются в кодовых единицах UTF-16, «глифы» — по графемам.
/// </summary>
public static class TextStatisticsCalculator
{
    public static TextStatisticsDto Calculate(RichDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paragraphs = document.Blocks.Count(block => !IsEmpty(block));
        return Calculate(document.ToPlainText(), paragraphs);
    }

    public static TextStatisticsDto Calculate(string text, int paragraphs)
    {
        ArgumentNullException.ThrowIfNull(text);

        var graphemes = CountGraphemes(text);
        return new TextStatisticsDto
        {
            Characters = text.Length,
            CharactersWithoutSpaces = graphemes.WithoutSpaces,
            Words = WordCounting.Count(text),
            Paragraphs = paragraphs < 0 ? 0 : paragraphs,
            GraphemeClusters = graphemes.Total,
        };
    }

    private static bool IsEmpty(TextBlock block)
    {
        foreach (var span in block.Spans)
        {
            if (!string.IsNullOrWhiteSpace(span.Text))
            {
                return false;
            }
        }

        return true;
    }

    private static (int Total, int WithoutSpaces) CountGraphemes(string text)
    {
        var total = 0;
        var withoutSpaces = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);

        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.GetTextElement();
            total++;
            if (!string.IsNullOrWhiteSpace(element))
            {
                withoutSpaces++;
            }
        }

        return (total, withoutSpaces);
    }
}
