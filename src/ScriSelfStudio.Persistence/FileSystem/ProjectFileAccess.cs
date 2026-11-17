namespace ScriSelfStudio.Persistence.FileSystem;

using System.Security.Cryptography;
using System.Text;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Storage;

/// <summary>Общий для экземпляров программы запрет одновременной записи и отпечаток файла.</summary>
internal static class ProjectFileAccess
{
    public static Result<FileStream> AcquireLease(string filePath)
    {
        try
        {
            var canonicalPath = Path.GetFullPath(filePath);
            if (OperatingSystem.IsWindows()) canonicalPath = canonicalPath.ToUpperInvariant();
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath)));
            // Блокировка не требует права записи в папку открываемой книги.
            var directory = Path.Combine(Path.GetTempPath(), "ScriSelfStudio", "project-locks");
            Directory.CreateDirectory(directory);
            return Result<FileStream>.Success(new FileStream(Path.Combine(directory, key + ".lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException error)
        {
            return Result<FileStream>.FromError(ErrorCode.ProjectFileConflict,
                "Другой экземпляр программы сейчас читает или сохраняет этот проект. Повторите действие после завершения записи.",
                technicalDetails: error.Message, exception: error);
        }
        catch (UnauthorizedAccessException error)
        {
            return Result<FileStream>.FromError(ErrorCode.FileOperationFailed,
                "Не удалось подготовить безопасную запись проекта. Проверьте доступ к временной папке.",
                technicalDetails: error.Message, exception: error);
        }
    }

    public static ProjectFileVersion ReadVersion(string filePath)
    {
        if (!File.Exists(filePath)) return new ProjectFileVersion(false, null);
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new ProjectFileVersion(true, Convert.ToHexString(SHA256.HashData(stream)));
    }

    public static Result CheckVersion(string filePath, ProjectFileVersion? expected)
    {
        if (expected is null) return Result.Success;
        try
        {
            return ReadVersion(filePath) == expected ? Result.Success : Result.FromError(
                ErrorCode.ProjectFileConflict,
                "Файл проекта изменён в другом окне или другой программой. Ваши правки остались открыты. Сохраните их отдельной копией через «Сохранить как», затем откройте актуальный файл.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Result.FromError(ErrorCode.StorageReadFailed,
                "Не удалось проверить, изменился ли файл проекта. Правки остались открыты; повторите сохранение или сохраните отдельную копию.",
                technicalDetails: error.Message, exception: error);
        }
    }
}
