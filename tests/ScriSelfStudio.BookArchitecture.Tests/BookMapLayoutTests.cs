namespace ScriSelfStudio.BookArchitecture.Tests;

using ScriSelfStudio.BookArchitecture.Map;

/// <summary>
/// Раскладка карты книги: столбец на главу, ширина не зависит от числа глав,
/// карта прокручивается по горизонтали (ТЗ п. 27).
/// </summary>
public sealed class BookMapLayoutTests
{
    [Fact]
    public void Column_width_does_not_depend_on_the_number_of_chapters()
    {
        var few = BookMapLayoutCalculator.Calculate(5, 1600d);
        var many = BookMapLayoutCalculator.Calculate(20, 1600d);

        Assert.Equal(BookMapLayoutOptions.Default.ColumnWidth, few.Columns[0].Width);
        Assert.Equal(BookMapLayoutOptions.Default.ColumnWidth, many.Columns[0].Width);
        Assert.Equal(few.Step, many.Step);
    }

    [Fact]
    public void Positions_depend_only_on_the_index_and_step()
    {
        var layout = BookMapLayoutCalculator.Calculate(38, 1600d);

        foreach (var column in layout.Columns)
        {
            Assert.Equal(column.Index * layout.Step, column.X);
        }
    }

    [Fact]
    public void Content_width_covers_the_last_column()
    {
        var layout = BookMapLayoutCalculator.Calculate(4, 1600d);
        var last = layout.Columns[^1];

        Assert.Equal(last.X + last.Width, layout.ContentWidth);
        Assert.Equal(3 * layout.Step + last.Width, layout.ContentWidth);
    }

    [Fact]
    public void Few_chapters_fit_the_viewport()
    {
        var layout = BookMapLayoutCalculator.Calculate(3, 1600d);

        Assert.True(layout.Fits);
    }

    [Fact]
    public void Too_many_chapters_report_that_they_do_not_fit()
    {
        // 38 столбцов по 250px (236 + 14) не помещаются в 1600px.
        var layout = BookMapLayoutCalculator.Calculate(38, 1600d);

        Assert.False(layout.Fits);
        Assert.True(layout.ContentWidth > 1600d);
    }

    [Fact]
    public void Zoom_changes_column_width_and_gap()
    {
        var zoomed = BookMapLayoutCalculator.Calculate(
            3,
            1600d,
            new BookMapLayoutOptions { ColumnWidth = 118d, Gap = 7d });

        Assert.Equal(118d, zoomed.Columns[0].Width);
        Assert.Equal(125d, zoomed.Step);
    }

    [Fact]
    public void Empty_book_has_no_columns()
    {
        var layout = BookMapLayoutCalculator.Calculate(0, 1600d);

        Assert.Empty(layout.Columns);
        Assert.True(layout.Fits);
        Assert.Equal(0d, layout.ContentWidth);
    }

    [Fact]
    public void Zero_viewport_does_not_break_the_calculation()
    {
        var layout = BookMapLayoutCalculator.Calculate(3, 0d);

        Assert.Equal(BookMapLayoutOptions.Default.ColumnWidth, layout.Columns[0].Width);
        Assert.False(layout.Fits);
        Assert.Equal(3, layout.Columns.Count);
    }
}
