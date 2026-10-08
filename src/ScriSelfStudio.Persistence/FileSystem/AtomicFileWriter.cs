namespace ScriSelfStudio.Persistence.FileSystem;

using System.IO;

/// <summary>
/// Безопасная подмена файла: сначала создаётся резервная копия, затем временный файл
/// заменяет целевой одной операцией (ТЗ п. 13). При ошибке целевой файл не меняется.
/// </summary>
internal static class AtomicFileWriter
{
    public const string TempSuffix = ".tmp";

    public const string BackupSuffix = ".bak";

    public static void Replace(string tempPath, string targetPath, bool createBackup)
    {
        if (createBackup && File.Exists(targetPath))
        {
            File.Copy(targetPath, targetPath + BackupSuffix, overwrite: true);
        }

        if (File.Exists(targetPath))
        {
            File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, targetPath);
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
