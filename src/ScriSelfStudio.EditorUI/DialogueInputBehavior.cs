namespace ScriSelfStudio.EditorUI;

using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaRichEditor.Controls;

/// <summary>Адаптация smart-list ввода компонента к литературному диалогу.</summary>
internal sealed class DialogueInputBehavior
{
    private readonly RichEditor _editor;
    private int? _prefixLine;

    public DialogueInputBehavior(RichEditor editor)
    {
        _editor = editor;
        editor.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.PointerPressedEvent, (_, _) => _prefixLine = null, RoutingStrategies.Tunnel);
        editor.LostFocus += (_, _) => _prefixLine = null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) _prefixLine = null;
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        var prefixLine = _prefixLine;
        _prefixLine = null;
        if (_editor.IsReadOnly || e.Handled || _editor.GetCaretFormat().List != AvaloniaRichEditor.Documents.ListKind.None) return;
        var status = _editor.GetStatus();
        if (e.Text == "-" && status.col == 1)
        {
            _prefixLine = status.line;
            return;
        }
        if (e.Text != " " || prefixLine != status.line || status.col != 2) return;
        var line = _editor.GetPlainText().Split('\n').ElementAtOrDefault(status.line - 1);
        if (line is null || !line.StartsWith("-", StringComparison.Ordinal)) return;

        // Оба изменения проходят через обычный ввод компонента: сохраняются курсор,
        // оформление и его история отмены. Вставка "— " не запускает TryAutoList.
        _editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back });
        e.Text = "— ";
    }
}
