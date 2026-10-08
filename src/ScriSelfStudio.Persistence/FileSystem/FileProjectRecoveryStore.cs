namespace ScriSelfStudio.Persistence.FileSystem;

using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core.Json;
using ScriSelfStudio.Core.Model;

/// <summary>
/// Файл восстановления рядом с проектом (ТЗ п. 13, 28). Удаляется только после
/// успешного сохранения проекта.
/// </summary>
public sealed class FileProjectRecoveryStore : IProjectRecoveryStore
{
    public const string RecoverySuffix = ".recovery";

    private readonly ILogger<FileProjectRecoveryStore>? _logger;

    public FileProjectRecoveryStore(ILogger<FileProjectRecoveryStore>? logger = null) => _logger = logger;

    public string GetRecoveryPath(string projectFilePath) => projectFilePath + RecoverySuffix;

    public async Task<Result> WriteAsync(Project project, string projectFilePath, CancellationToken cancellationToken = default)
    {
        var recoveryPath = GetRecoveryPath(projectFilePath);
        var tempPath = recoveryPath + AtomicFileWriter.TempSuffix;

        try
        {
            var json = JsonSerializer.Serialize(project, CoreJsonContext.Default.Project);
            AtomicFileWriter.TryDelete(tempPath);
            await File.WriteAllTextAsync(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
            AtomicFileWriter.Replace(tempPath, recoveryPath, createBackup: false);
            return Result.Success;
        }
        catch (IOException exception)
        {
            _logger?.LogError(exception, "Не удалось записать файл восстановления {RecoveryPath}.", recoveryPath);
            AtomicFileWriter.TryDelete(tempPath);
            return Result.FromError(ErrorCode.StorageWriteFailed, technicalDetails: exception.Message, exception: exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger?.LogError(exception, "Нет доступа к файлу восстановления {RecoveryPath}.", recoveryPath);
            AtomicFileWriter.TryDelete(tempPath);
            return Result.FromError(ErrorCode.StorageWriteFailed, technicalDetails: exception.Message, exception: exception);
        }
    }

    public async Task<Result<RecoveryInfo?>> FindAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        var recoveryPath = GetRecoveryPath(projectFilePath);
        if (!File.Exists(recoveryPath))
        {
            return Result<RecoveryInfo?>.Success(null);
        }

        try
        {
            var json = await File.ReadAllTextAsync(recoveryPath, cancellationToken);
            var project = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Project);
            if (project is null)
            {
                return Result<RecoveryInfo?>.FromError(
                    ErrorCode.RecoveryReadFailed,
                    technicalDetails: $"Файл {recoveryPath} не содержит данных проекта.");
            }

            var writtenAt = new DateTimeOffset(File.GetLastWriteTimeUtc(recoveryPath), TimeSpan.Zero);
            return Result<RecoveryInfo?>.Success(new RecoveryInfo
            {
                ProjectFilePath = projectFilePath,
                RecoveryFilePath = recoveryPath,
                WrittenAt = writtenAt,
                ProjectId = project.Id,
                ProjectName = project.Name,
            });
        }
        catch (JsonException exception)
        {
            _logger?.LogError(exception, "Файл восстановления {RecoveryPath} повреждён.", recoveryPath);
            return Result<RecoveryInfo?>.FromError(
                ErrorCode.RecoveryReadFailed,
                technicalDetails: exception.Message,
                exception: exception);
        }
        catch (IOException exception)
        {
            _logger?.LogError(exception, "Не удалось прочитать файл восстановления {RecoveryPath}.", recoveryPath);
            return Result<RecoveryInfo?>.FromError(
                ErrorCode.RecoveryReadFailed,
                technicalDetails: exception.Message,
                exception: exception);
        }
    }

    public async Task<Result<Project>> ReadAsync(RecoveryInfo recovery, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(recovery.RecoveryFilePath, cancellationToken);
            var project = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Project);
            if (project is null)
            {
                return Result<Project>.FromError(
                    ErrorCode.RecoveryReadFailed,
                    technicalDetails: $"Файл {recovery.RecoveryFilePath} не содержит данных проекта.");
            }

            return Result<Project>.Success(project);
        }
        catch (JsonException exception)
        {
            _logger?.LogError(exception, "Файл восстановления {RecoveryPath} повреждён.", recovery.RecoveryFilePath);
            return Result<Project>.FromError(
                ErrorCode.RecoveryReadFailed,
                technicalDetails: exception.Message,
                exception: exception);
        }
        catch (IOException exception)
        {
            _logger?.LogError(exception, "Не удалось прочитать файл восстановления {RecoveryPath}.", recovery.RecoveryFilePath);
            return Result<Project>.FromError(
                ErrorCode.RecoveryReadFailed,
                technicalDetails: exception.Message,
                exception: exception);
        }
    }

    public Task<Result> DeleteAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        var recoveryPath = GetRecoveryPath(projectFilePath);

        try
        {
            if (File.Exists(recoveryPath))
            {
                File.Delete(recoveryPath);
            }

            return Task.FromResult(Result.Success);
        }
        catch (IOException exception)
        {
            _logger?.LogError(exception, "Не удалось удалить файл восстановления {RecoveryPath}.", recoveryPath);
            return Task.FromResult(Result.FromError(ErrorCode.FileOperationFailed, technicalDetails: exception.Message, exception: exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger?.LogError(exception, "Нет доступа к файлу восстановления {RecoveryPath}.", recoveryPath);
            return Task.FromResult(Result.FromError(ErrorCode.FileOperationFailed, technicalDetails: exception.Message, exception: exception));
        }
    }
}
