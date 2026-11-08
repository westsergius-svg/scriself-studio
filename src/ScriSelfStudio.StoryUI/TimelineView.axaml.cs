namespace ScriSelfStudio.StoryUI;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

/// <summary>
/// Экран сюжетных линий. Логика — в <see cref="TimelineViewModel"/>.
/// Текстовые поля дополнительно сохраняются по потере фокуса: так правка
/// не теряется при уходе на другой экран, даже если кнопку не нажимали.
/// </summary>
public partial class TimelineView : UserControl
{
    public TimelineView()
    {
        InitializeComponent();
        AddHandler(LostFocusEvent, OnFieldLostFocus, RoutingStrategies.Bubble);
    }

    private void OnFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TextBox || DataContext is not TimelineViewModel viewModel)
        {
            return;
        }

        // Дать текущему клику выбрать вкладку до пересоздания карточек сохранением.
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(DataContext, viewModel) && viewModel.CommitLineCommand.CanExecute(null))
            {
                viewModel.CommitLineCommand.Execute(null);
            }
        }, DispatcherPriority.Background);
    }
}
