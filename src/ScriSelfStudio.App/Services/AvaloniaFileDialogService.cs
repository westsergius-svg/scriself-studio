namespace ScriSelfStudio.App.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Presentation.Dialogs;
using ScriSelfStudio.Presentation.Localization;

/// <summary>
/// Диалоги файлов и подтверждений на средствах Avalonia (ТЗ п. 14).
/// </summary>
public sealed class AvaloniaFileDialogService : IFileDialogService
{
    private const string ProjectExtension = "scri";

    private static readonly FilePickerFileType ProjectFileType = new(Loc.T("Проект ScriSelf Studio"))
    {
        Patterns = ["*.scri"],
        MimeTypes = ["application/x-scriself-project"],
    };

    private static readonly FilePickerFileType OpenProjectFileType = new(Loc.T("Проекты и резервные копии ScriSelf Studio"))
    {
        Patterns = ["*.scri", "*.bak", "*.backup"],
        MimeTypes = ["application/x-scriself-project"],
    };

    public async Task<string?> PickProjectToOpenAsync(string? suggestedDirectory, CancellationToken cancellationToken = default)
    {
        var window = MainWindow;
        if (window is null)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Открыть проект"),
            AllowMultiple = false,
            FileTypeFilter = [OpenProjectFileType],
            SuggestedStartLocation = await GetStartFolderAsync(suggestedDirectory),
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickProjectToCreateAsync(string suggestedFileName, string? suggestedDirectory, CancellationToken cancellationToken = default)
    {
        var window = MainWindow;
        if (window is null)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("Создать проект"),
            SuggestedFileName = suggestedFileName,
            DefaultExtension = ProjectExtension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [ProjectFileType],
            SuggestedStartLocation = await GetStartFolderAsync(suggestedDirectory),
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickExportFileToSaveAsync(string suggestedFileName, string extension, string fileTypeName, string? suggestedDirectory, CancellationToken cancellationToken = default)
    {
        var window = MainWindow;
        if (window is null)
        {
            return null;
        }

        var ext = extension.TrimStart('.');
        var filter = new FilePickerFileType(fileTypeName)
        {
            Patterns = [$"*.{ext}"],
        };

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("Экспорт рукописи"),
            SuggestedFileName = suggestedFileName,
            DefaultExtension = ext,
            ShowOverwritePrompt = true,
            FileTypeChoices = [filter],
            SuggestedStartLocation = await GetStartFolderAsync(suggestedDirectory),
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickCloudDownloadPathAsync(string suggestedFileName, string? suggestedDirectory, CancellationToken cancellationToken = default)
    {
        var window = MainWindow;
        if (window is null)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("Скачать проект из облака"),
            SuggestedFileName = suggestedFileName,
            DefaultExtension = ProjectExtension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [ProjectFileType],
            SuggestedStartLocation = await GetStartFolderAsync(suggestedDirectory),
        });

        return file?.TryGetLocalPath();
    }

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        CancellationToken cancellationToken = default)
    {
        var result = false;
        var confirmed = await ShowDialogAsync(
            title,
            message,
            confirmText,
            cancelText,
            isConfirm: true,
            value => result = value);

        return confirmed && result;
    }

    public async Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) =>
        await ShowDialogAsync(title, message, Loc.T("Понятно"), string.Empty, isConfirm: false, _ => { });

    public async Task<string?> PromptAsync(
        string title,
        string message,
        string initialValue,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var owner = MainWindow;
        if (owner is null)
        {
            return null;
        }

        string? result = null;
        var dialog = new PromptDialog
        {
            DataContext = new PromptDialogViewModel(title, message, initialValue, value => result = value),
        };

        await dialog.ShowDialog(owner);
        return result;
    }

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static async Task<bool> ShowDialogAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        bool isConfirm,
        Action<bool> onClosed)
    {
        var owner = MainWindow;
        if (owner is null)
        {
            return false;
        }

        var dialog = new MessageDialog
        {
            DataContext = new MessageDialogViewModel(title, message, confirmText, cancelText, isConfirm, onClosed),
        };

        await dialog.ShowDialog(owner);
        return true;
    }

    private static async Task<IStorageFolder?> GetStartFolderAsync(string? directory)
    {
        var window = MainWindow;
        if (window is null || string.IsNullOrEmpty(directory))
        {
            return null;
        }

        try
        {
            return await window.StorageProvider.TryGetFolderFromPathAsync(directory);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
