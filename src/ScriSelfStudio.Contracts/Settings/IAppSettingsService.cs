namespace ScriSelfStudio.Contracts.Settings;

using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Core.Model;

/// <summary>
/// Запись в списке последних проектов (ТЗ п. 27, 31).
/// </summary>
/// <remarks>
/// Поля сводки (описание, счётчики, дата изменения) кэшируются при открытии и
/// сохранении проекта, чтобы лаунчер показывал карточки без чтения каждого файла.
/// Поля добавлены без смены версии формата: в JSON они необязательны, у старых
/// записей остаются значения по умолчанию.
/// </remarks>
public sealed record RecentProjectEntry
{
    public required string FilePath { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset LastOpenedAt { get; init; }

    /// <summary>Краткое описание проекта (из файла проекта).</summary>
    public string? Description { get; init; }

    public int BookCount { get; init; }

    public int ChapterCount { get; init; }

    public int SceneCount { get; init; }

    public int CharacterCount { get; init; }

    public int PlotLineCount { get; init; }

    /// <summary>Когда проект последний раз менялся по данным файла.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Настройки приложения, не принадлежащие конкретному проекту.</summary>
public sealed class AppPreferences
{
    public const int MaxRecentProjects = 10;

    /// <summary>
    /// Тема оформления. По умолчанию Starter: стандартная компоновка
    /// со светлыми поверхностями и синим акцентом.
    /// </summary>
    public AppTheme Theme { get; set; } = AppTheme.Studio;

    public string Language { get; set; } = "ru-RU";

    public bool SpellCheckEnabled { get; set; } = true;

    public bool GrammarCheckEnabled { get; set; }

    public string GrammarLanguage { get; set; } = "auto";

    public bool AutoCapitalizeSentences { get; set; } = true;

    public List<string> PersonalDictionary { get; set; } = [];

    /// <summary>null сохраняет прежний вид навигации темы до первого явного выбора.</summary>
    public AppNavigationMode? NavigationMode { get; set; }

    public bool OpenLastProjectOnStartup { get; set; }

    public bool CheckRecoveryOnStartup { get; set; } = true;

    /// <summary>Проверять наличие новой сборки на сайте при каждом запуске.</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>null — пользователь ещё не выбрал, отправлять ли статистику.</summary>
    public bool? ShareUsageStatistics { get; set; }

    /// <summary>Случайный номер профиля приложения, создаваемый только после согласия.</summary>
    public string? StatisticsInstallationId { get; set; }

    public DateOnly? StatisticsLastSentDay { get; set; }

    public string? StatisticsLastSentVersion { get; set; }

    /// <summary>Логин Яндекса для облачного хранилища (если автор подключил Диск).</summary>
    public string? YandexDiskLogin { get; set; }

    /// <summary>
    /// Пароль приложения Яндекса, зашифрованный средствами Windows (DPAPI).
    /// В файле настроек открытый пароль не хранится.
    /// </summary>
    public string? YandexDiskProtectedPassword { get; set; }

    /// <summary>Последний выбранный провайдер облака: YandexDisk или GoogleDrive.</summary>
    public string CloudProvider { get; set; } = "YandexDisk";

    /// <summary>Email подключённого аккаунта Google Drive.</summary>
    public string? GoogleDriveAccountEmail { get; set; }

    /// <summary>Refresh-токен Google Drive, зашифрованный средствами Windows (DPAPI).</summary>
    public string? GoogleDriveProtectedRefreshToken { get; set; }

    // Setter обязателен: генератор System.Text.Json не заполняет коллекции
    // только для чтения, и список последних проектов терялся бы при загрузке
    // настроек (лаунчер показывал пустой список после перезапуска).
    public List<RecentProjectEntry> RecentProjects { get; set; } = [];
}

/// <summary>Хранилище настроек приложения и списка последних проектов.</summary>
public interface IAppSettingsService
{
    AppPreferences Current { get; }

    Task<Result> LoadAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Применяет изменение к настройкам приложения и сохраняет файл настроек (ТЗ п. 27).
    /// </summary>
    Task<Result> UpdateAsync(Action<AppPreferences> change, CancellationToken cancellationToken = default);

    Task<Result> AddRecentProjectAsync(RecentProjectEntry entry, CancellationToken cancellationToken = default);

    Task<Result> RemoveRecentProjectAsync(string filePath, CancellationToken cancellationToken = default);
}
