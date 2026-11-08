namespace ScriSelfStudio.App.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Presentation.Help;

public sealed class AvaloniaHelpService : IHelpService
{
    private HelpWindow? _window;

    public void Show(string topicId = "start")
    {
        if (_window is null)
        {
            var window = new HelpWindow { DataContext = new HelpViewModel() };
            window.Closed += (_, _) => _window = null;
            _window = window;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            {
                window.Show(owner);
            }
            else
            {
                window.Show();
            }
        }

        ((HelpViewModel)_window.DataContext!).SelectTopic(topicId);
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }
}
