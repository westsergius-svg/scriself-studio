namespace ScriSelfStudio.Presentation.Dialogs;

using CommunityToolkit.Mvvm.Input;

/// <summary>ViewModel простого диалога сообщения или подтверждения.</summary>
public sealed partial class MessageDialogViewModel : ViewModelBase
{
    private readonly Action<bool> _close;

    public MessageDialogViewModel(
        string title,
        string message,
        string confirmText,
        string cancelText,
        bool isConfirm,
        Action<bool> close)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        IsConfirm = isConfirm;
        _close = close;
    }

    public string Title { get; }

    public string Message { get; }

    public string ConfirmText { get; }

    public string CancelText { get; }

    public bool IsConfirm { get; }

    public bool HasConfirm => IsConfirm;

    public bool HasCancel => !string.IsNullOrEmpty(CancelText);

    /// <summary>Запрос на закрытие окна. Подписку выполняет View.</summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Confirm()
    {
        _close(true);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        _close(false);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
