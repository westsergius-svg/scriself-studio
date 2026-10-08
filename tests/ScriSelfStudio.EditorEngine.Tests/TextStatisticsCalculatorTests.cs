namespace ScriSelfStudio.EditorEngine.Tests;

using ScriSelfStudio.EditorEngine;
using ScriSelfStudio.TestSupport;

public sealed class TextStatisticsCalculatorTests
{
    [Fact]
    public void Counts_cyrillic_words_and_characters()
    {
        var statistics = TextStatisticsCalculator.Calculate("Привет мир", paragraphs: 1);

        Assert.Equal(10, statistics.Characters);
        Assert.Equal(9, statistics.CharactersWithoutSpaces);
        Assert.Equal(2, statistics.Words);
        Assert.Equal(10, statistics.GraphemeClusters);
        Assert.Equal(1, statistics.Paragraphs);
    }

    [Fact]
    public void Emoji_is_two_code_units_but_one_grapheme()
    {
        var statistics = TextStatisticsCalculator.Calculate("🚀 три", paragraphs: 1);

        Assert.Equal(6, statistics.Characters);
        Assert.Equal(5, statistics.GraphemeClusters);
        Assert.Equal(1, statistics.Words);
    }

    [Fact]
    public void Combining_characters_are_single_grapheme()
    {
        var statistics = TextStatisticsCalculator.Calculate("e\u0301", paragraphs: 1);

        Assert.Equal(2, statistics.Characters);
        Assert.Equal(1, statistics.GraphemeClusters);
        Assert.Equal(1, statistics.Words);
    }

    [Fact]
    public void Empty_text_gives_zeroes()
    {
        var statistics = TextStatisticsCalculator.Calculate(string.Empty, paragraphs: 0);

        Assert.Equal(0, statistics.Characters);
        Assert.Equal(0, statistics.Words);
        Assert.Equal(0, statistics.GraphemeClusters);
    }

    [Fact]
    public void Document_statistics_count_only_non_empty_paragraphs()
    {
        var document = SampleProjects.DocumentWithText("Первый абзац.\n\nВторой абзац.");

        var statistics = TextStatisticsCalculator.Calculate(document);

        Assert.Equal(2, statistics.Paragraphs);
        Assert.Equal(4, statistics.Words);
    }
}
