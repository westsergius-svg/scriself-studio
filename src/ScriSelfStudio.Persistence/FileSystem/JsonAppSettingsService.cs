namespace ScriSelfStudio.Persistence.FileSystem;

using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Json;
using ScriSelfStudio.Contracts.Settings;

/// <summary>
/// Настройки приложения и список последних проектов в файле профиля пользователя
/// (ТЗ п. 27, 31). Ошибка чтения не должна мешать запуску: применяются значения по умолчанию.
/// </summary>
public sealed class JsonAppSettingsService : IAppSettingsService
{
    public const string SettingsFileName = "settings.json";

    private readonly string _settingsPath;
    private readonly ILogger<JsonAppSettingsService>? _logger;

    public JsonAppSettingsService(string? settingsDirectory = null, ILogger<JsonAppSettingsService>? logger = null)
    {
        _logger = logger;
        var directory = settingsDirectory ?? DefaultDirectory();
        _settingsPath = Path.Combine(directory, SettingsFileName);
    }

    public AppPreferences Current { get; private set; } = new();

    public string SettingsPath => _settingsPath;

    public static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScriSelfStudio");

    public async Task<Result> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            Current = new AppPreferences();
            return Result.Success;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
            var preferences = JsonSerializer.Deserialize(json, ContractsJsonContext.Default.AppPreferences);
            Current = preferences ?? new AppPreferences();
            return Result.Success;
        }
        catch (JsonException exception)
        {
            _logger?.LogError(exception, "Файл настроек {SettingsPath} повреждён.", _settingsPath);
            Current = new AppPreferences();
            return Result.FromError(ErrorCode.SettingsReadFailed, technicalDetails: exception.Message, exception: exception);
        }
        catch (IOException exception)
        {
            _logger?.LogError(exception, "Не удалось прочитать настройки {SettingsPath}.", _settingsPath);
            Current = new AppPreferences();
            return Result.FromError(ErrorCode.SettingsReadFailed, technicalDetails: exception.Message, exception: exception);
        }
    }

    public Task<Result> SaveAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrEmpty(directory))
        {
            return Task.FromResult(Result.FromError(ErrorCode.SettingsWriteFailed, technicalDetails: _settingsPath));
        }

        var tempPath = _settingsPath + AtomicFileWriter.TempSuffix;

        try
        {
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(Current, ContractsJsonContext.Default.AppPreferences);
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            AtomicFileWriter.Replace(tempPath, _settingsPath, createBackup: false);
            return Task.FromResult(Result.Success);
        }
        catch (IOException exception)
        {
            _logger?.LogError(exception, "Не удалось сохранить настройки {SettingsPath}.", _settingsPath);
            AtomicFileWriter.TryDelete(tempPath);
            return Task.FromResult(Result.FromError(ErrorCode.SettingsWriteFailed, technicalDetails: exception.Message, exception: exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger?.LogError(exception, "Нет доступа к файлу настроек {SettingsPath}.", _settingsPath);
            AtomicFileWriter.TryDelete(tempPath);
            return Task.FromResult(Result.FromError(ErrorCode.SettingsWriteFailed, technicalDetails: exception.Message, exception: exception));
        }
    }

    public Task<Result> UpdateAsync(Action<AppPreferences> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        change(Current);
        return SaveAsync(cancellationToken);
    }

    public async Task<Result> AddRecentProjectAsync(RecentProjectEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var existing = Current.RecentProjects
            .FirstOrDefault(item => string.Equals(item.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Current.RecentProjects.Remove(existing);
        }

        Current.RecentProjects.Insert(0, entry);
        while (Current.RecentProjects.Count > AppPreferences.MaxRecentProjects)
        {
            Current.RecentProjects.RemoveAt(Current.RecentProjects.Count - 1);
        }

        return await SaveAsync(cancellationToken);
    }

    public async Task<Result> RemoveRecentProjectAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var existing = Current.RecentProjects
            .FirstOrDefault(item => string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return Result.Success;
        }

        Current.RecentProjects.Remove(existing);
        return await SaveAsync(cancellationToken);
    }
}
