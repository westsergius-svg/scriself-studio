namespace ScriSelfStudio.Presentation.Dialogs;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

/// <summary>ViewModel диалога ввода одной строки (название книги, главы, сцены).</summary>
public sealed partial class PromptDialogViewModel : ViewModelBase
{
    private readonly Action<string?> _close;
    private readonly string _initialValue;

    [ObservableProperty]
    private string _value = string.Empty;

    public PromptDialogViewModel(
        string title,
        string message,
        string initialValue,
        Action<string?> close,
        bool prefillInitialValue = false)
    {
        Title = title;
        Message = message;
        _initialValue = initialValue;
        _value = prefillInitialValue ? initialValue : string.Empty;
        _close = close;
    }

    public string Title { get; }

    public string Message { get; }

    public string InitialValue => _initialValue;

    /// <summary>Запрос на закрытие окна. Подписку выполняет View.</summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Confirm()
    {
        _close(string.IsNullOrWhiteSpace(Value) ? InitialValue : Value.Trim());
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        _close(null);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
