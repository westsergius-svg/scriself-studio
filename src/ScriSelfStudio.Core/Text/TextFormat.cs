namespace ScriSelfStudio.Core.Text;

/// <summary>Символьное форматирование фрагмента текста (ТЗ п. 11).</summary>
[Flags]
public enum TextFormat
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8,

    /// <summary>
    /// Моноширинное начертание. В отличие от остальных флагов это не только
    /// вид букв, но и метрики строки, поэтому представление обязано
    /// применять его к <em>всем</em> фрагментам строки, иначе выравнивание
    /// и перенос ломаются (ADR 003, шаг 3.1.5).
    /// </summary>
    Monospace = 16,
}

/// <summary>
/// Цвет текста или его фона: 8 бит на канал (ТЗ п. 11).
/// Хранится в документе как число, а не как объект Avalonia, чтобы ядро
/// не знало UI (ADR 003). <see cref="None"/> означает «цвет не задан» и
/// наследуется от темы.
/// </summary>
public readonly record struct TextColor(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Разбирает <c>#RRGGBB</c> или <c>#AARRGGBB</c>; иначе — <see cref="None"/>.</summary>
    public static TextColor Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return None;
        }

        var text = value.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return new TextColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        if (text.Length == 8 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var argb))
        {
            return new TextColor((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
        }

        return None;
    }

    /// <summary>Цвет не задан: наследуется от темы.</summary>
    public static TextColor None => default;

    /// <summary>Задан ли цвет явно.</summary>
    public bool IsSet => A != 0;

    /// <summary>Канонический вид <c>#AARRGGBB</c> — для JSON и отладки.</summary>
    public string ToHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// Оформление фрагмента текста: символьные флаги плюс шрифт и цвета
/// (ADR 003, шаг 3.1.5). Значение составное, а не только набор флагов:
/// цвет и шрифт в <see cref="TextFormat"/> не помещаются, а сравнивать спаны
/// только по флагам нельзя — соседние спаны с разным цветом слились бы в один
/// и потеряли бы оформление (ТЗ п. 43).
/// </summary>
public readonly record struct SpanStyle
{
    /// <summary>Символьные флаги: начертание и зачёркивание.</summary>
    public TextFormat Flags { get; init; }

    /// <summary>Семейство шрифта; <c>null</c> — шрифт из настроек проекта.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Кегль в пунктах; <c>null</c> — размер из настроек проекта.</summary>
    public double? FontSize { get; init; }

    /// <summary>Цвет текста; <see cref="TextColor.None"/> — из темы.</summary>
    public TextColor Foreground { get; init; }

    /// <summary>Цвет фона фрагмента (подсветка); <see cref="TextColor.None"/> — нет.</summary>
    public TextColor Highlight { get; init; }

    /// <summary>Оформление по умолчанию: без флагов, шрифт и цвета из темы.</summary>
    public static SpanStyle Default => default;

    /// <summary>Задано ли оформление явно (нужно ли сохранять спан в документе).</summary>
    public bool IsDefault => this == Default;

    public SpanStyle WithFlags(TextFormat flags) => this with { Flags = flags };

    /// <summary>
    /// Пересечение двух оформлений для <c>GetFormatting</c>: флаги — побитовое И,
    /// а необязательные поля берутся только если совпадают, иначе «смешанное
    /// значение» (<c>null</c> / <see cref="TextColor.None"/>), чтобы панель
    /// форматирования не показывала ложное состояние (ADR 004 п. 5).
    /// </summary>
    public SpanStyle Intersect(SpanStyle other) => new()
    {
        Flags = Flags & other.Flags,
        FontFamily = FontFamily == other.FontFamily ? FontFamily : null,
        FontSize = FontSize == other.FontSize ? FontSize : null,
        Foreground = Foreground == other.Foreground ? Foreground : TextColor.None,
        Highlight = Highlight == other.Highlight ? Highlight : TextColor.None,
    };
}
