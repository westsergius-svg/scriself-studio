namespace ScriSelfStudio.Core.Tests;

using System.Text.Json;
using ScriSelfStudio.Core.Json;
using ScriSelfStudio.Core.Text;

public sealed class ReviewEditingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Independent_changes_can_be_rejected_in_either_order()
    {
        var original = Document("Красный дом. Дальний лес.");
        var first = Edit(original, "Синий дом. Дальний лес.");
        var second = Edit(first, "Синий дом. Ближний лес.");
        Assert.Equal(2, second.Review.Changes.Count);
        var changes = second.Review.Changes.OrderBy(c => c.Start).ToArray();
        Assert.True(ReviewEditing.Reject(second, changes[0].Id));
        Assert.Equal("Красный дом. Ближний лес.", second.ToPlainText());
        Assert.True(ReviewEditing.Reject(second, changes[1].Id));
        Assert.Equal(original.ToPlainText(), second.ToPlainText());
        Assert.Empty(second.Review.Changes);
    }

    [Fact]
    public void Overlapping_edits_merge_and_keep_original_formatting()
    {
        var original = Document("Она ждала.", TextFormat.Italic);
        var first = Edit(original, "Она стояла.");
        var second = Edit(first, "Она стояла у окна.");
        second.Review.TrackChanges = false;
        var third = Edit(second, "Она стояла у двери.");
        var change = Assert.Single(third.Review.Changes);
        ReviewEditing.Reject(third, change.Id);
        Assert.Equal("Она ждала.", third.ToPlainText());
        Assert.All(third.Blocks[0].Spans, s => Assert.Equal(TextFormat.Italic, s.Style.Flags));
    }

    [Fact]
    public void Comment_tracks_insertion_split_merge_and_keeps_deleted_quote()
    {
        var original = Document("Начало слово конец");
        ReviewEditing.AddComment(original, 7, 5, "Уточнить", Now);
        var shifted = Edit(original, "Ещё Начало слово конец");
        var comment = Assert.Single(shifted.Review.Comments);
        Assert.Equal(11, comment.Start);
        Assert.Equal(5, comment.Length);
        var split = Edit(shifted, "Ещё Начало сло\nво конец");
        comment = Assert.Single(split.Review.Comments);
        Assert.Equal(6, comment.Length);
        Assert.NotEqual(comment.StartBlockId, comment.EndBlockId);
        var deleted = Edit(split, "Ещё Начало  конец");
        comment = Assert.Single(deleted.Review.Comments);
        Assert.True(comment.IsOrphaned);
        Assert.Equal("слово", comment.Quote);
        Assert.Equal("Уточнить", comment.Text);
    }

    [Theory]
    [InlineData("😀", "😃")]
    [InlineData("Начало\n\nКонец", "Начало Конец")]
    [InlineData("", "новый текст")]
    [InlineData("всё удалено", "")]
    public void Reject_restores_unicode_and_paragraphs(string before, string after)
    {
        var result = Edit(Document(before), after);
        var change = Assert.Single(result.Review.Changes);
        var json = JsonSerializer.Serialize(result, CoreJsonContext.Default.RichDocument);
        result = JsonSerializer.Deserialize(json, CoreJsonContext.Default.RichDocument)!;
        ReviewEditing.Validate(result);
        ReviewEditing.Reject(result, change.Id);
        Assert.Equal(before, result.ToPlainText());
    }

    [Fact]
    public void Accept_keeps_text_and_clone_has_independent_comments_and_history()
    {
        var original = Document("Первый текст");
        ReviewEditing.AddComment(original, 0, 6, "Комментарий", Now);
        var edited = Edit(original, "Второй текст");
        var copy = edited.Clone();
        copy.Review.Comments[0].Text = "Изменённая копия";
        ReviewEditing.Accept(copy, Assert.Single(copy.Review.Changes).Id);
        Assert.Single(edited.Review.Changes);
        Assert.Equal("Комментарий", edited.Review.Comments[0].Text);
        Assert.Equal("Второй текст", copy.ToPlainText());
    }

    [Fact]
    public void Review_equality_detects_changed_original_formatting_and_comment_state()
    {
        var original = Document("Первый текст", TextFormat.Italic);
        ReviewEditing.AddComment(original, 0, 6, "Комментарий", Now);
        var edited = Edit(original, "Второй текст");
        var copy = edited.Clone();
        Assert.True(ReviewEditing.SameReview(edited.Review, copy.Review));
        copy.Review.Changes[0].Before.Blocks[0].Alignment = TextAlignment.Center;
        Assert.False(ReviewEditing.SameReview(edited.Review, copy.Review));
        copy = edited.Clone();
        copy.Review.Comments[0].IsResolved = true;
        Assert.False(ReviewEditing.SameReview(edited.Review, copy.Review));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"comments\":null}")]
    [InlineData("{\"changes\":null}")]
    [InlineData("{\"trackChanges\":true}")]
    public void Corrupt_original_review_is_rejected_before_cloning_or_display(string reviewJson)
    {
        var edited = Edit(Document("Было"), "Стало");
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(edited, CoreJsonContext.Default.RichDocument))!;
        node["review"]!["changes"]![0]!["before"]!["review"] = System.Text.Json.Nodes.JsonNode.Parse(reviewJson);
        var loaded = JsonSerializer.Deserialize(node.ToJsonString(), CoreJsonContext.Default.RichDocument)!;
        Assert.Throws<InvalidDataException>(() => ReviewEditing.Validate(loaded));
    }

    [Fact]
    public void Paragraph_formatting_is_a_rejectable_change_even_without_text_changes()
    {
        var original = Document("Абзац");
        var edited = original.Clone();
        edited.Blocks[0].Alignment = TextAlignment.Center;
        ReviewEditing.Capture(original, edited, Now);
        ReviewEditing.Reject(edited, Assert.Single(edited.Review.Changes).Id);
        Assert.Equal(TextAlignment.Left, edited.Blocks[0].Alignment);
        Assert.Equal(original.Blocks[0].Id, edited.Blocks[0].Id);
    }

    private static RichDocument Edit(RichDocument previous, string text)
    {
        var current = Document(text);
        ReviewEditing.Capture(previous, current, Now);
        return current;
    }

    private static RichDocument Document(string text, TextFormat format = TextFormat.None)
    {
        var document = new RichDocument();
        document.Review.TrackChanges = true;
        foreach (var line in text.Split('\n'))
        {
            var block = new TextBlock();
            block.Spans.Add(new() { Text = line, Style = SpanStyle.Default with { Flags = format } });
            document.Blocks.Add(block);
        }
        return document;
    }
}
