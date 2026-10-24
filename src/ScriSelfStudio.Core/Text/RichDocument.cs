namespace ScriSelfStudio.Core.Text;

using System.Text.Json.Serialization;

using System.Text;

/// <summary>
/// Каноническая модель текста сцены. Владелец данных — текстовый движок;
/// Avalonia отображает модель через адаптер и не изменяет её напрямую (ТЗ п. 11).
/// </summary>
public sealed class RichDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<TextBlock> Blocks { get; } = [];

    public static RichDocument CreateEmpty()
    {
        var document = new RichDocument();
        document.Blocks.Add(new TextBlock());
        return document;
    }

    /// <summary>Плоский текст документа. Блоки разделяются переводом строки.</summary>
    public string ToPlainText()
    {
        var builder = new StringBuilder();
        for (var index = 0; index < Blocks.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            foreach (var span in Blocks[index].Spans)
            {
                builder.Append(span.Text);
            }
        }

        return builder.ToString();
    }

    /// <summary>Пустой ли документ (нет ни одного непробельного символа).</summary>
    public bool IsEmpty()
    {
        foreach (var block in Blocks)
        {
            foreach (var span in block.Spans)
            {
                if (!string.IsNullOrWhiteSpace(span.Text))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Полная копия документа. Используется, чтобы UI и движок не изменяли
    /// канонический документ проекта напрямую (ТЗ п. 11, 22).
    /// </summary>
    public RichDocument Clone()
    {
        var copy = new RichDocument { SchemaVersion = SchemaVersion };
        foreach (var block in Blocks)
        {
            var blockCopy = new TextBlock
            {
                Id = block.Id,
                Kind = block.Kind,
                Alignment = block.Alignment,
                IndentLevel = block.IndentLevel,
                ListMarker = block.ListMarker,
            };

            foreach (var span in block.Spans)
            {
                blockCopy.Spans.Add(new TextSpan { Text = span.Text, Style = span.Style });
            }

            copy.Blocks.Add(blockCopy);
        }

        return copy;
    }
}
