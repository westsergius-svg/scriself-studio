namespace ScriSelfStudio.Persistence.Cloud;

using ScriSelfStudio.Contracts.Cloud;

/// <summary>Реестр облачных хранилищ, доступных в панели «Облако».</summary>
public sealed class CloudStorageCatalog : ICloudStorageCatalog
{
    private readonly IReadOnlyDictionary<CloudProviderKind, ICloudStorageService> _services;

    public CloudStorageCatalog(YandexDiskWebDavService yandex, GoogleDriveService google)
    {
        ArgumentNullException.ThrowIfNull(yandex);
        ArgumentNullException.ThrowIfNull(google);
        _services = new Dictionary<CloudProviderKind, ICloudStorageService>
        {
            [CloudProviderKind.YandexDisk] = yandex,
            [CloudProviderKind.GoogleDrive] = google,
        };
        Providers =
        [
            new CloudProviderInfo(CloudProviderKind.YandexDisk, "Яндекс.Диск", RequiresInteractiveSignIn: false),
            new CloudProviderInfo(CloudProviderKind.GoogleDrive, "Google Drive", RequiresInteractiveSignIn: true),
        ];
    }

    public IReadOnlyList<CloudProviderInfo> Providers { get; }

    public ICloudStorageService Get(CloudProviderKind kind) =>
        _services.TryGetValue(kind, out var service)
            ? service
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный провайдер облака.");
}
