namespace ScriSelfStudio.BookArchitecture.Map;

/// <summary>
/// Параметры раскладки карты книги. Карта — горизонтальная лента столбцов
/// «одна глава — один столбец»: ширина столбца фиксирована и не зависит от числа
/// глав, поэтому карточки читаемы при любом масштабе, а карта прокручивается
/// по горизонтали (ТЗ п. 27).
/// </summary>
public sealed record BookMapLayoutOptions
{
    /// <summary>Ширина столбца карты.</summary>
    public double ColumnWidth { get; init; } = 236d;

    /// <summary>Зазор между столбцами.</summary>
    public double Gap { get; init; } = 14d;

    public static BookMapLayoutOptions Default { get; } = new();
}

/// <summary>Геометрия одного столбца карты.</summary>
public readonly record struct BookMapColumnGeometry(int Index, double X, double Width);

/// <summary>Раскладка всей карты: позиции столбцов, шаг и признак «не влезает».</summary>
public sealed record BookMapLayout(
    IReadOnlyList<BookMapColumnGeometry> Columns,
    double Step,
    bool Fits,
    double Margin)
{
    /// <summary>Ширина содержимого карты — по ней выставляется область прокрутки.</summary>
    public double ContentWidth => Columns.Count == 0
        ? 0d
        : Columns[^1].X + Columns[^1].Width;
}

/// <summary>
/// Расчёт раскладки карты книги: столбец на главу. Позиция столбца зависит только
/// от его индекса и шага, поэтому выбор главы или сцены ничего не сдвигает.
/// </summary>
public static class BookMapLayoutCalculator
{
    public static BookMapLayout Calculate(
        int chapterCount,
        double viewportWidth,
        BookMapLayoutOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chapterCount);

        var settings = options ?? BookMapLayoutOptions.Default;
        var width = settings.ColumnWidth > 0d ? settings.ColumnWidth : 1d;
        var gap = Math.Max(0d, settings.Gap);
        var step = width + gap;
        var viewport = double.IsFinite(viewportWidth) && viewportWidth > 0d ? viewportWidth : 0d;

        var columns = new List<BookMapColumnGeometry>(chapterCount);
        for (var index = 0; index < chapterCount; index++)
        {
            columns.Add(new BookMapColumnGeometry(index, index * step, width));
        }

        var contentWidth = chapterCount == 0 ? 0d : ((chapterCount - 1) * step) + width;
        var fits = chapterCount == 0 || contentWidth <= viewport;

        return new BookMapLayout(columns, step, fits, Margin: 0d);
    }
}
