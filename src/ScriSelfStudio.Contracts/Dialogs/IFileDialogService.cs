namespace ScriSelfStudio.Contracts.Dialogs;

/// <summary>
/// Диалоги файлов и подтверждений (ТЗ п. 14). Абстракция не знает про Avalonia:
/// реализация подставляется в App, тесты используют заглушку.
/// </summary>
public interface IFileDialogService
{
    Task<string?> PickProjectToOpenAsync(string? suggestedDirectory, CancellationToken cancellationToken = default);

    Task<string?> PickProjectToCreateAsync(string suggestedFileName, string? suggestedDirectory, CancellationToken cancellationToken = default);

    Task<string?> PickExportFileToSaveAsync(string suggestedFileName, string extension, string fileTypeName, string? suggestedDirectory, CancellationToken cancellationToken = default);

    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Запрашивает у пользователя одну строку (название книги, главы, сцены).
    /// Возвращает <c>null</c>, если пользователь отменил ввод.
    /// </summary>
    Task<string?> PromptAsync(string title, string message, string initialValue, CancellationToken cancellationToken = default);

    Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default);
}
