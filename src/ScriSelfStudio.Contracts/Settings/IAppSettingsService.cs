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
    /// Тема оформления. По умолчанию Paper: макеты экранов («Сюжетные линии»,
    /// «Персонажи») выполнены в кремовой светлой палитре, поэтому приложение
    /// должно открываться именно так.
    /// </summary>
    public AppTheme Theme { get; set; } = AppTheme.Paper;

    public string Language { get; set; } = "ru-RU";

    public bool CheckRecoveryOnStartup { get; set; } = true;

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
