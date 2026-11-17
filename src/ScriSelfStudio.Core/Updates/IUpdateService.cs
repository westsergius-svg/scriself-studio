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

    /// <summary>Проверяет манифест; недоступный старый канал допускает ручной листинг, ошибка подписи — нет.</summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Версия в канале новее установленной.</summary>
    bool IsNewer(UpdateInfo info);

    /// <summary>Проверяет подпись, скачивает и сверяет SHA-256; возвращает путь только к проверенному файлу.</summary>
    Task<string> DownloadAsync(UpdateInfo latest, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Повторно проверяет файл и запускает установщик. Завершает приложение только после успешного запуска.</summary>
    void LaunchInstaller(string installerPath);
}
