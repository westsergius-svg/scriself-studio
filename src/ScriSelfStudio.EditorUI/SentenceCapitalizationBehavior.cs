namespace ScriSelfStudio.EditorUI;

using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using ScriSelfStudio.Core.Text;

/// <summary>Завершает только вновь набранное слово; ручная правка и вставка не запускают автозамену.</summary>
public sealed class SentenceCapitalizationBehavior
{
    private readonly RichEditor _editor;
    private readonly Func<bool> _enabled;
    private Paragraph? _paragraph;
    private int _start;
    private int _end;
    private string _prefix = string.Empty;
    private bool _manualEdit;

    public SentenceCapitalizationBehavior(RichEditor editor, Func<bool> enabled)
    {
        _editor = editor;
        _enabled = enabled;
        editor.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.PointerPressedEvent, (_, _) => Reset(), RoutingStrategies.Tunnel);
        editor.LostFocus += (_, _) => Reset();
        editor.DocumentChanged += (_, _) => Reset();
    }

    private void Reset()
    {
        _paragraph = null;
        _manualEdit = false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && _paragraph is not null && _editor.GetCaretPosition() is var caret
            && ReferenceEquals(caret.Paragraph, _paragraph) && caret.Offset == _end && _end > _start)
        {
            _end--;
            return;
        }
        if (e.Key is Key.Back or Key.Delete)
        {
            _paragraph = null;
            _manualEdit = true;
        }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.Enter or Key.Tab
            || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) != 0)
        {
            Reset();
        }
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Handled || !_enabled() || _editor.IsReadOnly) { Reset(); return; }
        var caret = _editor.GetCaretPosition();
        var paragraph = caret.Paragraph;
        if (paragraph is null || RichDocumentAdapter.IsSceneHeader(paragraph)) { Reset(); return; }
        var text = RichEditor.GetParagraphText(paragraph);
        if (caret.Offset > text.Length || _editor.HasTextSelection) { Reset(); _manualEdit = true; return; }
        if (e.Text == " ")
        {
            if (_paragraph == paragraph && caret.Offset == _end && _start < _end
                && text[.._start] == _prefix && SentenceCapitalization.FindWordStart(text[.._end]) == _start)
            {
                e.Handled = _editor.CompleteSentenceWord(_start, text[_start.._end]);
            }
            Reset();
            return;
        }
        if (string.IsNullOrEmpty(e.Text) || !e.Text.All(char.IsLetter)) { Reset(); return; }
        if (_paragraph == paragraph && caret.Offset == _end)
        {
            _end += e.Text.Length;
            return;
        }
        _paragraph = null;
        if (_manualEdit || (caret.Offset < text.Length && char.IsLetter(text[caret.Offset]))) return;
        var start = SentenceCapitalization.FindWordStart(text[..caret.Offset] + e.Text);
        if (start != caret.Offset) return;
        _paragraph = paragraph;
        _start = caret.Offset;
        _end = _start + e.Text.Length;
        _prefix = text[.._start];
    }
}
