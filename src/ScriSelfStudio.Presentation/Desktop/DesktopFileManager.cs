namespace ScriSelfStudio.Presentation.Desktop;

using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>Показ файла проекта в системном файловом менеджере.</summary>
public static class DesktopFileManager
{
    public static void RevealFile(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var platform = OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
        using var process = Process.Start(CreateRevealStartInfo(filePath, platform));
    }

    /// <summary>Аргументы передаются отдельно: пробелы и кавычки в пути не становятся командами.</summary>
    public static ProcessStartInfo CreateRevealStartInfo(string filePath, OSPlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var startInfo = new ProcessStartInfo { UseShellExecute = false };
        if (platform == OSPlatform.Windows)
        {
            startInfo.FileName = "explorer.exe";
            startInfo.ArgumentList.Add("/select,");
            startInfo.ArgumentList.Add(filePath);
        }
        else if (platform == OSPlatform.OSX)
        {
            startInfo.FileName = "/usr/bin/open";
            startInfo.ArgumentList.Add("-R");
            startInfo.ArgumentList.Add(filePath);
        }
        else
        {
            startInfo.FileName = "xdg-open";
            startInfo.ArgumentList.Add(Path.GetDirectoryName(filePath) ?? filePath);
        }

        return startInfo;
    }
}
