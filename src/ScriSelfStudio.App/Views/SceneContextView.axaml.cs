namespace ScriSelfStudio.App.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;
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

}
