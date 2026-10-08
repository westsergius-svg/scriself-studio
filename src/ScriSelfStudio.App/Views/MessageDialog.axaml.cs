namespace ScriSelfStudio.App.Views;

using Avalonia.Controls;
using ScriSelfStudio.Presentation.Dialogs;

/// <summary>Диалог сообщения или подтверждения. Логика — в MessageDialogViewModel.</summary>
public partial class MessageDialog : Window
{
    private MessageDialogViewModel? _subscribed;

    public MessageDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        Subscribe();
    }

    private void Subscribe()
    {
        if (_subscribed is not null)
        {
            _subscribed.CloseRequested -= OnCloseRequested;
        }

        _subscribed = DataContext as MessageDialogViewModel;
        if (_subscribed is not null)
        {
            _subscribed.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
