namespace ScriSelfStudio.EditorEngine.Tests;

using ScriSelfStudio.EditorEngine;

public sealed class DocumentTextAdapterTests
{
    [Fact]
    public void Plain_text_is_split_into_paragraph_blocks()
    {
        var document = DocumentTextAdapter.FromPlainText("Первая\n\nВторая");

        Assert.Equal(3, document.Blocks.Count);
        Assert.Equal("Первая", document.Blocks[0].Spans[0].Text);
        Assert.Empty(document.Blocks[1].Spans);
        Assert.Equal("Вторая", document.Blocks[2].Spans[0].Text);
    }

    [Fact]
    public void Round_trip_preserves_text_and_normalizes_line_endings()
    {
        var document = DocumentTextAdapter.FromPlainText("Строка 1\r\nСтрока 2\rСтрока 3");

        Assert.Equal("Строка 1\nСтрока 2\nСтрока 3", document.ToPlainText());
    }

    [Fact]
    public void Null_or_empty_text_produces_single_empty_paragraph()
    {
        Assert.True(DocumentTextAdapter.FromPlainText(null).IsEmpty());
        Assert.Single(DocumentTextAdapter.FromPlainText(null).Blocks);
    }
}
