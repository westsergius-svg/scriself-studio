namespace ScriSelfStudio.App.Views;

using Avalonia.Controls;
using ScriSelfStudio.Presentation.Dialogs;

/// <summary>Диалог ввода одной строки. Логика — в PromptDialogViewModel.</summary>
public partial class PromptDialog : Window
{
    private PromptDialogViewModel? _subscribed;

    public PromptDialog()
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

        _subscribed = DataContext as PromptDialogViewModel;
        if (_subscribed is not null)
        {
            _subscribed.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
