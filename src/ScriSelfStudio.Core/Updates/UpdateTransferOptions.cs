namespace ScriSelfStudio.Core.Updates;

/// <summary>Сроки получения манифеста и большого установщика разделены.</summary>
public sealed record UpdateTransferOptions
{
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(25);
    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromHours(2);
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(1);
    public long MaxDownloadBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public int MaxManifestBytes { get; init; } = 1024 * 1024;

    internal void Validate()
    {
        if (RequestTimeout <= TimeSpan.Zero || DownloadTimeout <= TimeSpan.Zero
            || IdleTimeout <= TimeSpan.Zero || MaxDownloadBytes <= 0 || MaxManifestBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(UpdateTransferOptions), "Ограничения обновления должны быть положительными.");
        }
    }
}
