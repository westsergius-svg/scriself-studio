namespace ScriSelfStudio.EditorEngine;

using ScriSelfStudio.Core.Text;

/// <summary>
/// Преобразование между плоским текстом и канонической моделью документа.
/// Нужно редактору, пока не выбран компонент форматированного текста (ТЗ п. 21).
/// </summary>
public static class DocumentTextAdapter
{
    public static RichDocument FromPlainText(string? text)
    {
        var document = new RichDocument();
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

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
