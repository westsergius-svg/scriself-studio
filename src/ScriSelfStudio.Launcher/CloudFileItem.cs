namespace ScriSelfStudio.Launcher;

using System.Globalization;
using System.Windows.Input;
using ScriSelfStudio.Contracts.Cloud;

/// <summary>Строка списка проектов на Яндекс.Диске.</summary>
public sealed class CloudFileItem
{
    public CloudFileItem(CloudFileInfo file, ICommand downloadCommand)
    {
        ArgumentNullException.ThrowIfNull(file);
        File = file;
        DownloadCommand = downloadCommand;
    }

    public CloudFileInfo File { get; }

    public string Name => Path.GetFileNameWithoutExtension(File.Name);

    public string FileName => File.Name;

    public ICommand DownloadCommand { get; }

    public string SizeText => FormatSize(File.SizeBytes);

    public string UpdatedText => File.ModifiedAt is { } modified
        ? $"изменён {modified.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture)}"
        : "дата изменения неизвестна";

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "размер неизвестен";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{Math.Max(1, bytes / 1024)} КБ";
        }

        return $"{bytes / (1024d * 1024d):0.#} МБ";
    }
}
