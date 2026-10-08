namespace ScriSelfStudio.EditorEngine.Tests;

using ScriSelfStudio.EditorEngine;

public sealed class UndoableTextBufferTests
{
    [Fact]
    public void Undo_and_redo_restore_previous_states()
    {
        var buffer = new UndoableTextBuffer("Начало");

        buffer.Insert(6, " текста");
        Assert.Equal("Начало текста", buffer.Text);

        Assert.True(buffer.Undo().IsSuccess);
        Assert.Equal("Начало", buffer.Text);
        Assert.False(buffer.CanUndo);
        Assert.True(buffer.CanRedo);

        Assert.True(buffer.Redo().IsSuccess);
        Assert.Equal("Начало текста", buffer.Text);
        Assert.True(buffer.CanUndo);
        Assert.False(buffer.CanRedo);
    }

    [Fact]
    public void Undo_restores_deleted_text()
    {
        var buffer = new UndoableTextBuffer("Мама мыла раму");

        buffer.Delete(5, 5);
        Assert.Equal("Мама раму", buffer.Text);

        Assert.True(buffer.Undo().IsSuccess);
        Assert.Equal("Мама мыла раму", buffer.Text);
    }

    [Fact]
    public void New_edit_clears_redo_history()
    {
        var buffer = new UndoableTextBuffer("а");

        buffer.Insert(1, "б");
        buffer.Undo();
        Assert.True(buffer.CanRedo);

        buffer.Insert(0, "в");

        Assert.False(buffer.CanRedo);
        Assert.Equal("ва", buffer.Text);
    }

    [Fact]
    public void History_limit_drops_oldest_edits()
    {
        var buffer = new UndoableTextBuffer(string.Empty, historyLimit: 2);

        buffer.Insert(0, "а");
        buffer.Insert(1, "б");
        buffer.Insert(2, "в");

        Assert.True(buffer.Undo().IsSuccess);
        Assert.True(buffer.Undo().IsSuccess);
        Assert.Equal("а", buffer.Text);
        Assert.False(buffer.Undo().IsSuccess);
    }

    [Fact]
    public void Undo_without_history_fails_without_changing_text()
    {
        var buffer = new UndoableTextBuffer("текст");

        var result = buffer.Undo();

        Assert.False(result.IsSuccess);
        Assert.Equal("текст", buffer.Text);
    }

    [Fact]
    public void Invalid_edit_does_not_touch_history()
    {
        var buffer = new UndoableTextBuffer("текст");

        var result = buffer.Insert(100, "!"); 

        Assert.False(result.IsSuccess);
        Assert.False(buffer.CanUndo);
        Assert.Equal("текст", buffer.Text);
    }
}
