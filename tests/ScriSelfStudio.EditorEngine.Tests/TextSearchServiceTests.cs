namespace ScriSelfStudio.EditorEngine.Tests;

using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.EditorEngine;

public sealed class TextSearchServiceTests
{
    [Fact]
    public void Search_is_case_insensitive_by_default()
    {
        var result = TextSearchService.Search("Мама мыла раму. Мама устала.", "мама");

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(0, result.Matches[0].Range.Start.Offset);
        Assert.Equal(4, result.Matches[0].Range.Length);
        Assert.Equal(16, result.Matches[1].Range.Start.Offset);
    }

    [Fact]
    public void Match_case_option_changes_result()
    {
        var result = TextSearchService.Search(
            "Мама мыла раму",
            "мама",
            new SearchOptions { MatchCase = true });

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public void Whole_word_option_skips_partial_matches()
    {
        var partial = TextSearchService.Search("мамаша и мама", "мама");
        var whole = TextSearchService.Search("мамаша и мама", "мама", new SearchOptions { WholeWord = true });

        Assert.Equal(2, partial.TotalCount);
        var match = Assert.Single(whole.Matches);
        Assert.Equal(9, match.Range.Start.Offset);
    }

    [Fact]
    public void Preview_contains_match_and_context()
    {
        var result = TextSearchService.Search("Первое предложение. Искомое слово. Третье.", "Искомое");

        var match = Assert.Single(result.Matches);
        Assert.Contains("Искомое", match.Preview);
        Assert.Contains("предложение", match.Preview);
    }

    [Fact]
    public void Empty_query_or_text_returns_no_matches()
    {
        Assert.Empty(TextSearchService.Search("текст", string.Empty).Matches);
        Assert.Empty(TextSearchService.Search(string.Empty, "текст").Matches);
        Assert.Empty(TextSearchService.Search("текст", "длиннее-текста").Matches);
    }

    [Fact]
    public void Search_handles_cyrillic_and_emoji()
    {
        var result = TextSearchService.Search("ёжик 🚀 и ёжик 🚀", "ёжик");

        Assert.Equal(2, result.TotalCount);
    }
}
