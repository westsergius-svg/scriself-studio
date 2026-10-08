namespace ScriSelfStudio.EditorEngine.Tests;

using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.EditorEngine;

public sealed class TextBufferTests
{
    [Fact]
    public void Insert_and_delete_change_text_and_version()
    {
        var buffer = new TextBuffer("Привет");

        Assert.True(buffer.Insert(6, " мир").IsSuccess);
        Assert.Equal("Привет мир", buffer.Text);
        Assert.Equal(1, buffer.Version);

        Assert.True(buffer.Delete(6, 4).IsSuccess);
        Assert.Equal("Привет", buffer.Text);
        Assert.Equal(2, buffer.Version);
        Assert.Equal(6, buffer.Length);
    }

    [Fact]
    public void Replace_removes_and_inserts_simultaneously()
    {
        var buffer = new TextBuffer("Мама мыла раму");

        Assert.True(buffer.Replace(0, 4, "Папа").IsSuccess);

        Assert.Equal("Папа мыла раму", buffer.Text);
    }

    [Fact]
    public void Out_of_range_operations_fail_with_editor_error()
    {
        var buffer = new TextBuffer("текст");

        var beyondEnd = buffer.Insert(6, "!");
        var negative = buffer.Delete(-1, 1);
        var tooLong = buffer.Delete(2, 10);

        Assert.False(beyondEnd.IsSuccess);
        Assert.Equal(ErrorCode.EditorOperationFailed, beyondEnd.Error!.Code);
        Assert.False(negative.IsSuccess);
        Assert.False(tooLong.IsSuccess);
        Assert.Equal("текст", buffer.Text);
        Assert.Equal(0, buffer.Version);
    }

    [Fact]
    public void Empty_operations_are_noop_and_do_not_bump_version()
    {
        var buffer = new TextBuffer("текст");

        Assert.True(buffer.Insert(0, string.Empty).IsSuccess);
        Assert.True(buffer.Delete(0, 0).IsSuccess);
        Assert.Equal(0, buffer.Version);
    }

    [Fact]
    public void Slice_returns_requested_fragment_and_rejects_invalid_range()
    {
        var buffer = new TextBuffer("Кириллица и emoji 🚀");

        Assert.Equal("emoji", buffer.Slice(12, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(0, 1000));
    }
}
