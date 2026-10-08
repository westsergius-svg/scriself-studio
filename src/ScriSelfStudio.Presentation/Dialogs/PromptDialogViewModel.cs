namespace ScriSelfStudio.Presentation.Dialogs;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

/// <summary>ViewModel диалога ввода одной строки (название книги, главы, сцены).</summary>
public sealed partial class PromptDialogViewModel : ViewModelBase
{
    private readonly Action<string?> _close;

    [ObservableProperty]
    private string _value;

    public PromptDialogViewModel(string title, string message, string initialValue, Action<string?> close)
    {
        Title = title;
        Message = message;
        _value = initialValue;
        _close = close;
    }

    public string Title { get; }

    public string Message { get; }

    /// <summary>Запрос на закрытие окна. Подписку выполняет View.</summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Confirm()
    {
        _close(string.IsNullOrWhiteSpace(Value) ? null : Value.Trim());
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        _close(null);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
