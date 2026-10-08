namespace ScriSelfStudio.TestSupport;

using ScriSelfStudio.Contracts.Dialogs;

/// <summary>
/// Диалоги с заранее заданными ответами: проверяет сценарии, не открывая окон.
/// </summary>
public sealed class StubFileDialogService : IFileDialogService
{
    public string? CreatePath { get; set; }

    public string? OpenPath { get; set; }

    public string? ExportPath { get; set; }

    public bool ConfirmResult { get; set; }

    /// <summary>Ответ на <see cref="PromptAsync"/>. <c>null</c> — пользователь отменил ввод.</summary>
    public string? PromptResult { get; set; }

    /// <summary>Очередь ответов на подряд идущие запросы ввода; пустая — используется <see cref="PromptResult"/>.</summary>
    public Queue<string?> PromptResponses { get; } = new();

    public List<string> Errors { get; } = [];

    public List<string> Confirmations { get; } = [];

    public List<string> Prompts { get; } = [];

    public Task<string?> PickProjectToOpenAsync(string? suggestedDirectory, CancellationToken cancellationToken = default) =>
        Task.FromResult(OpenPath);

    public Task<string?> PickProjectToCreateAsync(string suggestedFileName, string? suggestedDirectory, CancellationToken cancellationToken = default) =>
        Task.FromResult(CreatePath);

    public Task<string?> PickExportFileToSaveAsync(string suggestedFileName, string extension, string fileTypeName, string? suggestedDirectory, CancellationToken cancellationToken = default) =>
        Task.FromResult(ExportPath ?? (string.IsNullOrEmpty(CreatePath) ? null : Path.ChangeExtension(CreatePath, extension)));

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, CancellationToken cancellationToken = default)
    {
        Confirmations.Add($"{title}: {message}");
        return Task.FromResult(ConfirmResult);
    }

    public Task<string?> PromptAsync(string title, string message, string initialValue, CancellationToken cancellationToken = default)
    {
        Prompts.Add($"{title}: {message} = {initialValue}");
        return Task.FromResult(PromptResponses.Count > 0 ? PromptResponses.Dequeue() : PromptResult);
    }

    public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        Errors.Add($"{title}: {message}");
        return Task.CompletedTask;
    }
}
