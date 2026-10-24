namespace ScriSelfStudio.Core.Updates;

/// <summary>
/// Проверка, загрузка и запуск обновления. Канал — сайт Scriborium, тот же адрес,
/// что использовал Scriborium 1.1.3.3S.
/// </summary>
public interface IUpdateService
{
    /// <summary>Версия запущенного приложения, например <c>1.1.4</c>.</summary>
    string CurrentVersion { get; }

    /// <summary>Адрес канала обновлений: каталог со сборками и манифестом <c>latest.json</c>.</summary>
    string ChannelUrl { get; }

    /// <summary>Проверяет канал: манифест, при неудаче — листинг каталога.</summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Версия в канале новее установленной.</summary>
    bool IsNewer(UpdateInfo info);

    /// <summary>Скачивает установщик во временную папку и возвращает путь к файлу.</summary>
    Task<string> DownloadAsync(UpdateInfo latest, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Запускает установщик и завершает процесс, чтобы файлы можно было заменить.</summary>
    void LaunchInstaller(string installerPath);
}
