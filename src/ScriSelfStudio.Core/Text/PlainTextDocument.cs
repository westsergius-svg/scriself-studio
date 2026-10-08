namespace ScriSelfStudio.Core.Text;

/// <summary>
/// Преобразование плоского текста в канонический документ. Нужно там, где текст
/// приходит без разметки — например из старых форматов проекта (ТЗ п. 5, 11).
/// </summary>
public static class PlainTextDocument
{
    /// <summary>Каждая строка текста становится абзацем документа.</summary>
    public static RichDocument FromText(string? text)
    {
        var document = new RichDocument();
        var normalized = (text ?? string.Empty).ReplaceLineEndings("\n");

        foreach (var line in normalized.Split('\n'))
        {
            var block = new TextBlock();
            if (line.Length > 0)
            {
                block.Spans.Add(new TextSpan { Text = line });
            }

            document.Blocks.Add(block);
        }

        return document;
    }
}
