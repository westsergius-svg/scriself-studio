namespace ScriSelfStudio.IntegrationTests;

using ScriSelfStudio.Persistence.Language;

public sealed class TextToolsTests
{
    private readonly TextToolsService _service = new();

    [Theory]
    [InlineData("Мама мыла раму. Писатели работают над книгами.")]
    [InlineData("The writer is working on a book.")]
    [InlineData("Ёжик живёт в лесу. Ежик живет в лесу.")]
    [InlineData("NASA СССР https://example.com/typpoo name@typpoo.com abc123")]
    public async Task Valid_words_inflections_and_non_words_are_not_marked(string text) =>
        Assert.Empty(await _service.CheckAsync(text));

    [Fact]
    public async Task Typos_have_exact_offsets_and_real_suggestions()
    {
        const string text = "😀 превет и worlld.";
        var issues = await _service.CheckAsync(text);
        Assert.Equal(new[] { "превет", "worlld" }, issues.Select(i => i.Word));
        Assert.All(issues, issue => Assert.Equal(issue.Word, text.Substring(issue.Start, issue.Length)));
        Assert.Contains("привет", await _service.SuggestAsync("превет"));
        Assert.Contains("world", await _service.SuggestAsync("worlld"));
    }

    [Fact]
    public async Task Mixed_keyboard_word_is_not_silently_accepted() =>
        Assert.Single(await _service.CheckAsync("привeт"));

    [Fact]
    public async Task Wiki_wordnet_supplies_synonyms_separately_from_spelling()
    {
        var synonyms = await _service.SynonymsAsync("автомобиль");
        Assert.Contains("машина", synonyms);
        Assert.DoesNotContain("автомобиль", synonyms);
        Assert.Empty(await _service.SynonymsAsync("превет"));
        Assert.Empty(await _service.CheckAsync("работают"));
    }

    [Fact]
    public async Task Cancelled_check_cannot_return_stale_results()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.CheckAsync("превет", cancel.Token));
    }

    [Fact]
    public async Task Cancelled_suggestion_cannot_return_partial_results()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.SuggestAsync("превет", cancel.Token));
    }
}
