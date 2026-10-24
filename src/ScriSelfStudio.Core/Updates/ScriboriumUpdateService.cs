namespace ScriSelfStudio.Core.Updates;

using System.Diagnostics;
using System.Reflection;

/// <summary>
/// Обновление с официального канала Scriborium. Адрес и формат взяты у старого
/// апдейтера Scriborium 1.1.3.3S: каталог <c>uploads/scrisoft/</c>, манифест
/// <c>latest.json</c> и листинг каталога как резервный путь.
/// </summary>
public sealed class ScriboriumUpdateService : IUpdateService
{
    /// <summary>Каталог обновлений на сайте Scriborium — тот же адрес, что и раньше.</summary>
    public const string DefaultChannelUrl = "https://scriborium.ru/uploads/scrisoft/";

    /// <summary>Имя файла-манифеста в каталоге обновлений.</summary>
    public const string ManifestName = "latest.json";

    private const string UpdateFolderName = "ScriSelfStudioUpdate";
    private const string UpdaterUserAgent = "ScriSelfStudioUpdater/1.0";

    private static readonly HttpClient SharedClient = new();

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(25);

    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
    private readonly string _currentVersion;
    private readonly Action _shutdown;

    /// <summary>Канал обновлений приложения.</summary>
    public ScriboriumUpdateService()
        : this(SharedClient.SendAsync, DefaultChannelUrl, ReadAssemblyVersion())
    {
    }

    /// <summary>Канал с подменённой транспортной частью — для тестов и локальных проверок.</summary>
    public ScriboriumUpdateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        string channelUrl,
        string currentVersion,
        Action? shutdown = null)
    {
        _send = send;
        ChannelUrl = channelUrl;
        _currentVersion = currentVersion;
        _shutdown = shutdown ?? (() => Environment.Exit(0));
    }

    /// <inheritdoc />
    public string CurrentVersion => _currentVersion;

    /// <inheritdoc />
    public string ChannelUrl { get; }

    /// <inheritdoc />
    public bool IsNewer(UpdateInfo info) =>
        UpdateFeedParser.IsNewer(info.Version, UpdateFeedParser.ParseVersion(_currentVersion));

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var manifestError = "манифест недоступен";

        try
        {
            var manifest = await FetchManifestAsync(cancellationToken).ConfigureAwait(false);
            if (manifest is not null)
            {
                return UpdateCheckResult.Found(manifest);
            }

            manifestError = "в манифесте нет файла сборки";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Манифеста нет — пробуем листинг каталога, как делал старый Scriborium.
            manifestError = exception.Message;
        }

        try
        {
            var listing = await FetchListingAsync(cancellationToken).ConfigureAwait(false);
            var newest = UpdateFeedParser.Newest(listing);
            if (newest is not null)
            {
                return UpdateCheckResult.Found(newest);
            }

            return UpdateCheckResult.Empty();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return UpdateCheckResult.Failed($"{manifestError}; листинг: {exception.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<string> DownloadAsync(
        UpdateInfo latest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), UpdateFolderName);
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, latest.FileName);
        var partial = destination + ".part";

        using var timeout = CreateTimeoutToken(cancellationToken);
        using var response = await _send(CreateRequest(latest.Url), timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        var buffer = new byte[81920];
        long received = 0;

        await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
        await using (var file = File.Create(partial))
        {
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                received += read;
                if (total > 0)
                {
                    progress?.Report(Math.Min(1d, (double)received / total));
                }
            }
        }

        File.Move(partial, destination, overwrite: true);
        return destination;
    }

    /// <inheritdoc />
    public void LaunchInstaller(string installerPath)
    {
        // Установщик заменяет файлы приложения: сначала запускаем его, затем выходим.
        using var process = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        _shutdown();
    }

    private async Task<UpdateInfo?> FetchManifestAsync(CancellationToken cancellationToken)
    {
        var url = UpdateFeedParser.JoinUrl(ChannelUrl, ManifestName);
        var body = await ReadTextAsync(url, cancellationToken).ConfigureAwait(false);
        return UpdateFeedParser.ParseManifest(body, ChannelUrl);
    }

    private async Task<IReadOnlyList<UpdateInfo>> FetchListingAsync(CancellationToken cancellationToken)
    {
        var body = await ReadTextAsync(ChannelUrl, cancellationToken).ConfigureAwait(false);
        return UpdateFeedParser.ParseListing(body, ChannelUrl);
    }

    private async Task<string> ReadTextAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeoutToken(cancellationToken);
        using var response = await _send(CreateRequest(url), timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
    }

    private static HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UpdaterUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        return request;
    }

    private static CancellationTokenSource CreateTimeoutToken(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(RequestTimeout);
        return source;
    }

    /// <summary>Версия приложения из сборки: <c>Version</c> в Directory.Build.props.</summary>
    private static string ReadAssemblyVersion()
    {
        var assembly = typeof(ScriboriumUpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informational)
            ? assembly.GetName().Version?.ToString()
            : informational;

        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.0";
        }

        // SourceLink добавляет к версии метаданные сборки (+commit) — для сравнения они лишние.
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }
}
