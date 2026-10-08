namespace ScriSelfStudio.EditorUI;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

/// <summary>
/// Экран «Редактор»: лента главы. Разметка — в EditorView.axaml, логика — в
/// <see cref="EditorViewModel"/>. Здесь только навигация по блокам: клик или
/// фокус на блоке делает его сцену активной.
/// </summary>
public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble);
        AddHandler(GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is EditorViewModel viewModel && e.Source is Control source)
        {
            Activate(viewModel, source);
        }
    }

    private void OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is EditorViewModel viewModel && e.Source is Control source)
        {
            Activate(viewModel, source);
        }
    }

    private static void Activate(EditorViewModel viewModel, Control source)
    {
        if (FindBlock(source) is { IsActive: false } block)
        {
            viewModel.ActivateBlockCommand.Execute(block);
        }
    }

    private static SceneBlockViewModel? FindBlock(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent as Control)
        {
            if (current.DataContext is SceneBlockViewModel block)
            {
                return block;
            }
        }

        return null;
    }
}