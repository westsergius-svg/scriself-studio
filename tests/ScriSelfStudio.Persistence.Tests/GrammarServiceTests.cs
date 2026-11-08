namespace ScriSelfStudio.Persistence.Tests;

using System.Text.Json;
using ScriSelfStudio.Persistence.Language;
using ScriSelfStudio.TestSupport;

public sealed class GrammarServiceTests
{
    [Fact]
    public void Response_preserves_utf16_offsets_filters_spelling_and_supports_deletion()
    {
        const string text = "😀 She go go.";
        var json = JsonSerializer.Serialize(new { matches = new[]
        {
            Match(7, 5, "GRAMMAR", "grammar", new[] { "goes", "", "line\nbreak" }),
            Match(7, 2, "TYPOS", "misspelling", new[] { "got" }),
        } });
        var issue = Assert.Single(GrammarResponse.Parse(json, text));
        Assert.Equal("go go", issue.OriginalText);
        Assert.Equal(7, issue.Start);
        Assert.Equal(new[] { "goes", "" }, issue.Replacements);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(0, 100)]
    public void Corrupt_or_split_surrogate_range_is_not_a_clean_result(int start, int length)
    {
        var json = JsonSerializer.Serialize(new { matches = new[] { Match(start, length, "GRAMMAR", "grammar", []) } });
        Assert.Throws<InvalidDataException>(() => GrammarResponse.Parse(json, "😀 word"));
    }

    [Fact]
    public void Long_input_is_split_without_losing_or_splitting_unicode()
    {
        var text = new string('a', 7999) + "😀. " + new string('б', 12000);
        var chunks = GrammarResponse.Chunks(text).ToArray();
        Assert.Equal(text, string.Concat(chunks.Select(c => c.Text)));
        Assert.All(chunks, c =>
        {
            Assert.InRange(c.Text.Length, 1, GrammarResponse.ChunkLength);
            Assert.False(char.IsLowSurrogate(c.Text[0]));
            Assert.False(char.IsHighSurrogate(c.Text[^1]));
            Assert.Equal(c.Text, text.Substring(c.Start, c.Text.Length));
        });
        Assert.Equal("ru", GrammarResponse.DetectLanguage("Он сказал: hello."));
        Assert.Equal("en-US", GrammarResponse.DetectLanguage("She said hello."));
    }

    [Fact]
    public async Task Missing_runtime_and_cancelled_request_are_not_reported_as_no_issues()
    {
        using var workspace = new TempWorkspace();
        using var service = new LanguageToolService(workspace.Root);
        Assert.False((await service.CheckAsync("She go home.", "en-US")).IsSuccess);
        Assert.False((await service.CheckAsync("Text", "unknown")).IsSuccess);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckAsync("Text", "ru", cancel.Token));
        await service.StopAsync();
    }

    private static object Match(int start, int length, string category, string type, string[] replacements) => new
    {
        offset = start, length, message = "Проверить согласование",
        rule = new { id = "TEST", issueType = type, category = new { id = category } },
        replacements = replacements.Select(value => new { value }).ToArray(),
    };
}
