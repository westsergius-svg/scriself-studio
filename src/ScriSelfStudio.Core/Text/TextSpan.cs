namespace ScriSelfStudio.Core.Text;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Непрерывный фрагмент текста с одинаковым оформлением.
/// </summary>
/// <remarks>
/// <see cref="Style"/> — составное значение (флаги, шрифт, цвета), а не только
/// <see cref="TextFormat"/>: цвет в набор флагов не помещается, а сравнение
/// соседних спанов только по флагам слило бы их и потеряло оформление
/// (ADR 003, шаг 3.1.5; ТЗ п. 43). Старые файлы читаются: у них был только
/// <c>Format</c>, поэтому прежнее свойство оставлено и переносится в
/// <see cref="Style"/> при чтении (миграция схемы не нужна: поля JSON аддитивны).
/// </remarks>
[JsonConverter(typeof(TextSpanJsonConverter))]
public sealed class TextSpan
{
    public string Text { get; set; } = string.Empty;

    /// <summary>Оформление фрагмента — единственный источник истины.</summary>
    public SpanStyle Style { get; set; }
}

/// <summary>
/// Чтение и запись фрагмента текста с учётом смены модели оформления
/// (ADR 003, шаг 3.1.5). Старые документы хранили только <c>format</c>
/// (символьные флаги), новые — составной <c>style</c>. Оба ключа читаются,
/// но записывается только <c>style</c>: держать два сериализуемых свойства
/// значило бы завести второе представление оформления, которое со временем
/// разошлось бы с <see cref="TextSpan.Style"/> (ТЗ п. 43).
/// </summary>
public sealed class TextSpanJsonConverter : JsonConverter<TextSpan>
{
    public override TextSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = string.Empty;
        var flags = TextFormat.None;
        SpanStyle? style = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            _ = reader.Read();
            switch (name)
            {
                case "text":
                    text = reader.GetString() ?? string.Empty;
                    break;
                case "format":
                    flags = (TextFormat)reader.GetInt32();
                    break;
                case "style":
                    style = JsonSerializer.Deserialize<SpanStyle>(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        // Старый файл без style: флаги переносятся в составное оформление.
        var result = new TextSpan { Text = text, Style = SpanStyle.Default.WithFlags(flags) };
        if (style is not null)
        {
            // Оба ключа присутствуют (правка вручную или сторонний инструмент):
            // флаги из style считаются источником истины, старый ключ дополняет.
            result.Style = style.Value.WithFlags(style.Value.Flags | flags);
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, TextSpan value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("text", value.Text);
        writer.WritePropertyName("style");
        JsonSerializer.Serialize(writer, value.Style, options);
        writer.WriteEndObject();
    }
}
