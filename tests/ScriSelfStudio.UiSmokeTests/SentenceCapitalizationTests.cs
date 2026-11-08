namespace ScriSelfStudio.UiSmokeTests;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using ScriSelfStudio.EditorUI;

public sealed class SentenceCapitalizationTests
{
    [AvaloniaFact]
    public void Capitalization_preserves_the_first_letters_formatting()
    {
        var paragraph = new Paragraph
        {
            Inlines =
            {
                new Run { Text = "Конец. " },
                new Run { Text = "с", FontWeight = FontWeight.Bold, FontStyle = FontStyle.Italic, FontSize = 18 },
                new Run { Text = "лово" },
            },
        };
        var editor = new RichEditor { Document = new FlowDocument { Blocks = { paragraph } } };
        var window = new Window { Content = editor };
        window.Show();
        editor.FocusDocumentEnd();
        try
        {
            var before = editor.ToJson();
            Assert.True(editor.CompleteSentenceWord(7, "слово"));
            Assert.Equal("Конец. Слово ", editor.GetPlainText());
            var first = paragraph.Inlines.OfType<Run>().Single(run => run.Text == "С");
            Assert.Equal(FontWeight.Bold, first.FontWeight);
            Assert.Equal(FontStyle.Italic, first.FontStyle);
            Assert.Equal(18, first.FontSize);
            editor.Undo();
            Assert.Equal(before, editor.ToJson());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Space_capitalizes_fresh_word_and_undo_is_respected()
    {
        var editor = new RichEditor { Document = new FlowDocument { Blocks = { new Paragraph() } } };
        var window = new Window { Content = editor };
        _ = new SentenceCapitalizationBehavior(editor, () => true);
        window.Show();
        editor.FocusDocumentEnd();
        try
        {
            editor.InsertText("Конец. ");
            foreach (var c in "слово") window.KeyTextInput(c.ToString());
            Assert.Equal("Конец. слово", editor.GetPlainText());
            window.KeyTextInput(" ");
            Assert.Equal("Конец. Слово ", editor.GetPlainText());
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
            Assert.Equal("Конец. слово", editor.GetPlainText());
            window.KeyTextInput(" ");
            Assert.Equal("Конец. слово ", editor.GetPlainText());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Manually_lowercased_letter_and_inserted_text_remain_lowercase()
    {
        var editor = new RichEditor { Document = new FlowDocument { Blocks = { new Paragraph() } } };
        var window = new Window { Content = editor };
        var enabled = true;
        _ = new SentenceCapitalizationBehavior(editor, () => enabled);
        window.Show();
        editor.FocusDocumentEnd();
        try
        {
            editor.InsertText("Конец. Слово");
            for (var i = 0; i < 5; i++) window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
            window.KeyTextInput("с");
            window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
            window.KeyTextInput(" ");
            Assert.Equal("Конец. слово ", editor.GetPlainText());
            editor.InsertText("Конец. вставлено");
            window.KeyTextInput(" ");
            Assert.EndsWith("Конец. вставлено ", editor.GetPlainText());
            enabled = false;
            editor.InsertText("Конец. ");
            foreach (var c in "слово") window.KeyTextInput(c.ToString());
            window.KeyTextInput(" ");
            Assert.EndsWith("Конец. слово ", editor.GetPlainText());
        }
        finally { window.Close(); }
    }
}
