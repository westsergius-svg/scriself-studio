namespace ScriSelfStudio.Core.Tests;

using ScriSelfStudio.Core.Text;

public sealed class SentenceCapitalizationTests
{
    [Theory]
    [InlineData("Конец. слово", 7)]
    [InlineData("End. word", 5)]
    [InlineData("Что? слово", 5)]
    [InlineData("«Конец.» слово", 9)]
    public void Completed_sentence_word_is_detected(string text, int expected) =>
        Assert.Equal(expected, SentenceCapitalization.FindWordStart(text));

    [Theory]
    [InlineData("т. е. слово")]
    [InlineData("ул. ленина")]
    [InlineData("Dr. smith")]
    [InlineData("Конец... слово")]
    [InlineData("3.14 слово")]
    [InlineData("https://example.com")]
    [InlineData("Конец.слово")]
    [InlineData("Обычное слово")]
    public void Abbreviations_numbers_urls_and_continuing_sentences_are_untouched(string text) =>
        Assert.Null(SentenceCapitalization.FindWordStart(text));
}
