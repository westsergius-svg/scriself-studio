namespace ScriSelfStudio.App.Views;

using Avalonia.Controls;

/// <summary>Красная кнопка закрывает окно, Cmd+Q завершает приложение; состояние сохраняется.</summary>
public sealed class MacWindowBehavior : IDisposable
{
    private readonly Window _window;
    private readonly Func<Task> _flush;
    private readonly Func<bool> _hasPendingEdits;
    private readonly Func<string, Task> _reportError;
    private bool _allowShutdown;
    private bool _closing;

    public MacWindowBehavior(Window window, Func<Task> flush, Func<bool> hasPendingEdits,
        Func<string, Task> reportError)
    {
        _window = window;
        _flush = flush;
        _hasPendingEdits = hasPendingEdits;
        _reportError = reportError;
        window.Closing += OnClosing;
    }

    public void Show()
    {
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Show();
        _window.Activate();
    }

    public void AllowShutdown() => _allowShutdown = true;

    public void Dispose() => _window.Closing -= OnClosing;

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowShutdown)
        {
            return;
        }

        e.Cancel = true;
        if (_closing)
        {
            return;
        }

        _closing = true;
        try
        {
            await _flush();
            if (_hasPendingEdits())
            {
                await _reportError("Не удалось сохранить изменения. Окно оставлено открытым.");
                return;
            }

            if (_window.WindowState == WindowState.FullScreen)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Hide();
        }
        catch (Exception exception)
        {
            await _reportError(exception.Message);
        }
        finally
        {
            _closing = false;
        }
    }
}
