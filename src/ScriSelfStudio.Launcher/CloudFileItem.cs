namespace ScriSelfStudio.Launcher;

using System.Globalization;
using System.Windows.Input;
using ScriSelfStudio.Contracts.Cloud;
using ScriSelfStudio.Presentation.Localization;

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
        ? Loc.F("изменён {0}", modified.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
        : Loc.T("дата изменения неизвестна");

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return Loc.T("размер неизвестен");
        }

        if (bytes < 1024 * 1024)
        {
            return Loc.F("{0} КБ", Math.Max(1, bytes / 1024));
        }

        return Loc.F("{0:0.#} МБ", bytes / (1024d * 1024d));
    }
}
