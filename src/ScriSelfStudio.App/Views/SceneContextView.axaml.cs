namespace ScriSelfStudio.App.Views;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ScriSelfStudio.App.Shell;

/// <summary>
/// Правая панель «Контекст сцены» в редакторе (ТЗ п. 27): описание, статус,
/// сюжетные линии с шагами, персонажи сцены и заметки — всё редактируется.
/// </summary>
public partial class SceneContextView : UserControl
{
    public SceneContextView()
    {
        InitializeComponent();
        AddHandler(LostFocusEvent, OnFieldLostFocus, RoutingStrategies.Bubble);
    }

    private void OnFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox
            && DataContext is SceneContextViewModel viewModel
            && viewModel.UpdateMetaCommand.CanExecute(null))
        {
            viewModel.UpdateMetaCommand.Execute(null);
        }
    }

    private void OnPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not SceneContextViewModel viewModel)
        {
            return;
        }

        for (var control = e.Source as Control; control is not null; control = control.GetVisualParent() as Control)
        {
            if (control is Button or ComboBox or ScrollBar or TextBox)
            {
                return;
            }

            if (ReferenceEquals(control, sender))
            {
                break;
            }
        }

        viewModel.CollapseDescriptions();
    }
}
