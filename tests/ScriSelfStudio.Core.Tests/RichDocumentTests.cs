namespace ScriSelfStudio.Core.Tests;

using ScriSelfStudio.Core.Text;
using ScriSelfStudio.TestSupport;

public sealed class RichDocumentTests
{
    [Fact]
    public void ToPlainText_joins_blocks_with_newline()
    {
        var document = SampleProjects.DocumentWithText("Первая строка.\nВторая строка.");

        Assert.Equal("Первая строка.\nВторая строка.", document.ToPlainText());
        Assert.False(document.IsEmpty());
    }

    [Fact]
    public void Clone_is_independent_from_source()
    {
        var document = SampleProjects.DocumentWithText("Исходный текст", TextFormat.Bold);

        var clone = document.Clone();
        clone.Blocks[0].Spans[0].Text = "Изменённый текст";
        clone.Blocks[0].Spans[0].Style = clone.Blocks[0].Spans[0].Style.WithFlags(TextFormat.Italic);
        clone.Blocks.Add(new TextBlock());

        Assert.Equal("Исходный текст", document.ToPlainText());
        Assert.Equal(TextFormat.Bold, document.Blocks[0].Spans[0].Style.Flags);
        Assert.Single(document.Blocks);
        Assert.Equal(2, clone.Blocks.Count);
    }

    [Fact]
    public void Empty_document_is_detected()
    {
        var document = RichDocument.CreateEmpty();

        Assert.True(document.IsEmpty());
        Assert.Equal(string.Empty, document.ToPlainText());
        Assert.Equal(RichDocument.CurrentSchemaVersion, document.SchemaVersion);
    }
}
