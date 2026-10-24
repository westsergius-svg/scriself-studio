namespace ScriSelfStudio.TestSupport;

using ScriSelfStudio.Core.Updates;

/// <summary>Канал обновлений с заранее заданным ответом — для тестов без сети.</summary>
public sealed class FakeUpdateService : IUpdateService
{
    public string CurrentVersion { get; init; } = "1.0.0";

    public string ChannelUrl => "https://example.test/updates/";

    public UpdateInfo? Latest { get; set; }

    public string? Error { get; set; }

    public int Checks { get; private set; }

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        Checks++;
        var result = Error is not null
            ? UpdateCheckResult.Failed(Error)
            : Latest is not null
                ? UpdateCheckResult.Found(Latest)
                : UpdateCheckResult.Empty();
        return Task.FromResult(result);
    }

    public bool IsNewer(UpdateInfo info) =>
        UpdateFeedParser.IsNewer(info.Version, UpdateFeedParser.ParseVersion(CurrentVersion));

    public Task<string> DownloadAsync(
        UpdateInfo latest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(latest.FileName);

    public void LaunchInstaller(string installerPath)
    {
    }
}
