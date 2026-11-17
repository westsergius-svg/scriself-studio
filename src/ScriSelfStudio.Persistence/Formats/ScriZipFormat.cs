namespace ScriSelfStudio.Persistence.Formats;

using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Persistence.FileSystem;

/// <summary>Содержимое старого формата `.scri`: манифест и документ проекта.</summary>
public sealed record ScriZipContent(JsonObject Manifest, JsonObject Content);

/// <summary>
/// Контейнер старого формата `.scri`: ZIP с <c>manifest.json</c> и <c>content.json</c>.
/// Формат читается и записывается: проект, открытый из такого файла, сохраняется в него же,
/// чтобы файл по-прежнему открывался старыми версиями приложения (ТЗ п. 5, 13).
/// </summary>
public static class ScriZipFormat
{
    /// <summary>Имя формата в нашей модели (см. <c>Project.SourceFormat</c>).</summary>
    public const string FormatId = ProjectFileFormats.ZipJson;

    public const string ManifestEntry = "manifest.json";

    public const string ContentEntry = "content.json";

    /// <summary>Максимальная поддерживаемая версия контейнера.</summary>
    public const int SupportedVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Похож ли файл на ZIP-контейнер: проверяются только первые байты.</summary>
    public static bool HasZipSignature(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var header = new byte[2];
            return stream.Read(header, 0, 2) == 2 && header[0] == (byte)'P' && header[1] == (byte)'K';
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static Result<ScriZipContent> Read(string filePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            var manifestText = ReadEntry(archive, ManifestEntry);
            var contentText = ReadEntry(archive, ContentEntry);
            if (manifestText is null || contentText is null)
            {
                return Invalid(
                    "В файле проекта нет manifest.json или content.json.",
                    "Восстановите файл из резервной копии.");
            }

            if (JsonNode.Parse(manifestText) is not JsonObject manifest ||
                JsonNode.Parse(contentText) is not JsonObject content)
            {
                return Invalid("Манифест или документ проекта не является объектом JSON.");
            }

            var format = manifest["format"]?.GetValue<string>();
            if (!string.Equals(format, "scri", StringComparison.OrdinalIgnoreCase))
            {
                return Invalid(
                    $"Неизвестный формат проекта: «{format}».",
                    "Ожидается формат scri.");
            }

            var version = manifest["version"]?.GetValue<int>() ?? 0;
            if (version > SupportedVersion)
            {
                return Result<ScriZipContent>.Failure(AppError.Create(
                    ErrorCode.UnsupportedDocumentVersion,
                    $"Версия формата {version} новее поддерживаемой ({SupportedVersion}).",
                    "Обновите приложение, чтобы открыть этот файл."));
            }

            return Result<ScriZipContent>.Success(new ScriZipContent(manifest, content));
        }
        catch (InvalidDataException exception)
        {
            return Invalid(
                "Архив проекта повреждён.",
                "Откройте резервную копию файла.",
                exception.Message,
                exception);
        }
        catch (JsonException exception)
        {
            return Invalid(
                "Данные проекта повреждены.",
                "Откройте резервную копию файла.",
                exception.Message,
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return Invalid(
                "Не удалось прочитать файл проекта.",
                "Проверьте доступ к файлу и повторите открытие.",
                exception.Message,
                exception);
        }
    }

    public static Result Write(string filePath, JsonObject manifest, JsonObject content, bool createBackup,
        ScriSelfStudio.Contracts.Storage.ProjectFileVersion? expectedFileVersion = null)
    {
        var tempPath = filePath + AtomicFileWriter.TempSuffix;
        try
        {
            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, ManifestEntry, manifest);
                WriteEntry(archive, ContentEntry, content);
            }

            // Старый файл не перезаписывается без резервной копии (ТЗ п. 13).
            var unchanged = ProjectFileAccess.CheckVersion(filePath, expectedFileVersion);
            if (!unchanged.IsSuccess)
            {
                AtomicFileWriter.TryDelete(tempPath);
                return unchanged;
            }
            AtomicFileWriter.Replace(tempPath, filePath, createBackup);
            return Result.Success;
        }
        catch (IOException exception)
        {
            AtomicFileWriter.TryDelete(tempPath);
            return Result.FromError(ErrorCode.StorageWriteFailed, technicalDetails: exception.Message, exception: exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            AtomicFileWriter.TryDelete(tempPath);
            return Result.FromError(ErrorCode.StorageWriteFailed, technicalDetails: exception.Message, exception: exception);
        }
    }

    private static Result<ScriZipContent> Invalid(
        string userMessage,
        string? suggestedAction = null,
        string? technicalDetails = null,
        Exception? exception = null) =>
        Result<ScriZipContent>.Failure(AppError.Create(
            ErrorCode.ProjectFormatInvalid,
            userMessage,
            suggestedAction,
            technicalDetails,
            exception));

    private static string? ReadEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void WriteEntry(ZipArchive archive, string entryName, JsonObject payload)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(payload.ToJsonString(WriteOptions));
    }
}
