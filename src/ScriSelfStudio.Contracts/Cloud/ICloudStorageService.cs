namespace ScriSelfStudio.Contracts.Cloud;

using ScriSelfStudio.Contracts.Errors;

/// <summary>Облачное хранилище, к которому можно подключить проекты.</summary>
public enum CloudProviderKind
{
    YandexDisk,
    GoogleDrive,
}

/// <summary>Описание провайдера облака для выбора в интерфейсе.</summary>
public sealed record CloudProviderInfo(
    CloudProviderKind Kind,
    string DisplayName,
    bool RequiresInteractiveSignIn);

/// <summary>Файл проекта, который лежит в облаке.</summary>
public sealed record CloudFileInfo
{
    public required string Name { get; init; }

    public required string RemotePath { get; init; }

    public long SizeBytes { get; init; }

    public DateTimeOffset? ModifiedAt { get; init; }
}

/// <summary>
/// Хранилище проектов в облаке автора. Приложение работает с папкой
/// <c>ScriSelfStudio/Projects</c> и не показывает остальные файлы Диска.
/// </summary>
public interface ICloudStorageService
{
    /// <summary>Провайдер, который реализует это хранилище.</summary>
    CloudProviderKind Provider { get; }

    /// <summary>Вход выполняется в браузере через OAuth, а не логином и паролем.</summary>
    bool RequiresInteractiveSignIn { get; }

    /// <summary>Есть ли сохранённые учётные данные, готовые к работе.</summary>
    bool IsConfigured { get; }

    /// <summary>Логин подключённого аккаунта.</summary>
    string? AccountLogin { get; }

    /// <summary>
    /// Секрет текущего сеанса для сохранения в настройках: пароль приложения
    /// Яндекса или refresh-токен Google. В файл настроек он попадает только
    /// после DPAPI-шифрования в слое интерфейса.
    /// </summary>
    string? SessionSecret { get; }

    /// <summary>Проверяет логин и пароль приложения запросом к Диску.</summary>
    Task<Result> ConnectAsync(string? login, string? password, CancellationToken cancellationToken = default);

    /// <summary>Запускает интерактивный вход (браузер) для OAuth-провайдера.</summary>
    Task<Result> ConnectInteractiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Восстанавливает ранее сохранённый сеанс без повторного ввода данных.</summary>
    Task<Result> RestoreAsync(string? login, string? secret, CancellationToken cancellationToken = default);

    /// <summary>Забывает учётные данные в текущем сеансе.</summary>
    void Disconnect();

    /// <summary>Список файлов проектов в папке приложения на Диске.</summary>
    Task<Result<IReadOnlyList<CloudFileInfo>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Загружает локальный файл проекта в папку приложения на Диске.</summary>
    Task<Result> UploadAsync(string localFilePath, CancellationToken cancellationToken = default);

    /// <summary>Скачивает файл проекта из облака на диск.</summary>
    Task<Result> DownloadAsync(CloudFileInfo file, string localFilePath, CancellationToken cancellationToken = default);
}

/// <summary>Набор доступных провайдеров облака.</summary>
public interface ICloudStorageCatalog
{
    IReadOnlyList<CloudProviderInfo> Providers { get; }

    ICloudStorageService Get(CloudProviderKind kind);
}
