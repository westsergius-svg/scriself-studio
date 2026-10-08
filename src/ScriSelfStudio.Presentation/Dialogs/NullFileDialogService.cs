namespace ScriSelfStudio.Presentation.Dialogs;

using ScriSelfStudio.Contracts.Dialogs;

/// <summary>
/// Заглушка диалогов для тестов и профилей без окон: считает, что пользователь
/// отменил выбор. Ничего не выдумывает и не открывает скрытых путей.
/// </summary>
public sealed class NullFileDialogService : IFileDialogService
{
    public Task<string?> PickProjectToOpenAsync(string? suggestedDirectory, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> PickProjectToCreateAsync(string suggestedFileName, string? suggestedDirectory, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> PickExportFileToSaveAsync(string suggestedFileName, string extension, string fileTypeName, string? suggestedDirectory, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<string?> PromptAsync(string title, string message, string initialValue, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
