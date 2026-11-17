namespace ScriSelfStudio.Core.Updates;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

/// <summary>
/// Обновление с официального канала Scriborium. Адрес и формат взяты у старого
/// апдейтера Scriborium 1.1.3.3S: каталог <c>uploads/scrisoft/</c>, манифест
/// <c>latest.json</c> и листинг каталога как резервный путь.
/// </summary>
public sealed class ScriboriumUpdateService : IUpdateService
{
    /// <summary>Каталог обновлений на сайте Scriborium — тот же адрес, что и раньше.</summary>
    public const string DefaultChannelUrl = "https://scriself.com/uploads/scrisoft/";

    /// <summary>Имя файла-манифеста в каталоге обновлений.</summary>
    public const string ManifestName = "latest.json";
    /// <summary>Страница ручного скачивания для прежних неподписанных выпусков.</summary>
    public const string DownloadPageUrl = "https://scriself.com/download";

    private const string UpdateFolderName = "ScriSelfStudioUpdate";
    private const string UpdaterUserAgent = "ScriSelfStudioUpdater/1.0";

    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };

    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
    private readonly string _currentVersion;
    private readonly Action _shutdown;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly string _runtimeIdentifier;
    private readonly string _manifestPublicKey;
    private readonly UpdateTransferOptions _transferOptions;
    private readonly ConcurrentDictionary<string, string> _verifiedDownloads = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Канал обновлений приложения.</summary>
    public ScriboriumUpdateService()
        : this((request, token) => SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token), DefaultChannelUrl, ReadAssemblyVersion())
    {
    }

    /// <summary>Канал с подменённой транспортной частью — для тестов и локальных проверок.</summary>
    public ScriboriumUpdateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        string channelUrl,
        string currentVersion,
        Action? shutdown = null,
        string? runtimeIdentifier = null,
        string? manifestPublicKey = null,
        UpdateTransferOptions? transferOptions = null,
        Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        _send = send;
        ChannelUrl = channelUrl;
        _currentVersion = currentVersion;
        _shutdown = shutdown ?? (() => Environment.Exit(0));
        _startProcess = startProcess ?? Process.Start;
        _runtimeIdentifier = runtimeIdentifier ?? CurrentRuntimeIdentifier();
        _manifestPublicKey = manifestPublicKey ?? SignedUpdateManifest.PublicKey;
        _transferOptions = transferOptions ?? new UpdateTransferOptions();
        _transferOptions.Validate();
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
        catch (InvalidDataException exception)
        {
            // Ошибка проверки не даёт перейти на неподписанный резервный источник.
            return UpdateCheckResult.Failed(exception.Message);
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
        if (!UpdateFeedParser.SupportsRuntime(latest.FileName, _runtimeIdentifier))
        {
            throw new InvalidOperationException("Сборка обновления не подходит для этой платформы.");
        }

        if (latest.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || Path.GetFileName(latest.FileName) != latest.FileName
            || !Uri.TryCreate(latest.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
            || url.UserInfo.Length != 0
            || !Uri.TryCreate(UpdateFeedParser.JoinUrl(ChannelUrl, latest.FileName), UriKind.Absolute, out var expectedUrl)
            || url != expectedUrl)
        {
            throw new InvalidDataException("Некорректный адрес или имя файла обновления.");
        }
        SignedUpdateManifest.Verify(latest, _manifestPublicKey);

        var directory = Path.Combine(Path.GetTempPath(), UpdateFolderName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, latest.FileName);
        var partial = destination + ".part";
        var completed = false;

        try
        {
            using var request = CreateRequest(latest.Url);
            HttpResponseMessage response;
            using (var headersTimeout = CreateTimeoutToken(cancellationToken, _transferOptions.RequestTimeout))
            {
                response = await _send(request, headersTimeout.Token).ConfigureAwait(false);
            }
            using var responseLifetime = response;
            response.EnsureSuccessStatusCode();
            using var transfer = CreateTimeoutToken(cancellationToken, _transferOptions.DownloadTimeout);
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(transfer.Token);
            var total = response.Content.Headers.ContentLength ?? -1;
            if (total > _transferOptions.MaxDownloadBytes)
            {
                throw new InvalidDataException("Файл обновления превышает допустимый размер.");
            }
            var buffer = new byte[81920];
            long received = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            idle.CancelAfter(_transferOptions.IdleTimeout);
            await using (var source = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false))
            await using (var file = File.Create(partial))
            {
                while (true)
                {
                    idle.CancelAfter(_transferOptions.IdleTimeout);
                    var read = await source.ReadAsync(buffer.AsMemory(), idle.Token).ConfigureAwait(false);
                    idle.CancelAfter(Timeout.InfiniteTimeSpan);
                    if (read == 0) { break; }
                    if (received > _transferOptions.MaxDownloadBytes - read)
                    {
                        throw new InvalidDataException("Файл обновления превышает допустимый размер.");
                    }
                    await file.WriteAsync(buffer.AsMemory(0, read), transfer.Token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    if (total > 0) { progress?.Report(Math.Min(0.99, (double)received / total)); }
                }
                await file.FlushAsync(transfer.Token).ConfigureAwait(false);
            }
            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (received == 0 || (total >= 0 && received != total)
                || !string.Equals(actual, latest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Файл обновления повреждён. Повторите скачивание.");
            }
            File.Move(partial, destination, overwrite: false);
            _verifiedDownloads[Path.GetFullPath(destination)] = actual;
            completed = true;
            progress?.Report(1);
            return destination;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Скачивание обновления прервано: сервер долго не отвечает. Повторите попытку.", exception);
        }
        finally
        {
            if (!completed)
            {
                TryDelete(partial);
                TryDelete(destination);
                try { Directory.Delete(directory); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <inheritdoc />
    public void LaunchInstaller(string installerPath)
    {
        var fullPath = Path.GetFullPath(installerPath);
        if (!_verifiedDownloads.TryGetValue(fullPath, out var expected))
        {
            throw new InvalidDataException("Установщик не прошёл проверку обновления.");
        }
        // Windows не разрешает запись/замену файла до завершения запуска процесса.
        using var launchLock = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(launchLock)), expected, StringComparison.Ordinal))
        {
            _verifiedDownloads.TryRemove(fullPath, out _);
            throw new InvalidDataException("Установщик изменён после скачивания. Повторите скачивание.");
        }
        installerPath = fullPath;
        // Установщик заменяет файлы приложения: сначала запускаем его, затем выходим.
        ProcessStartInfo startInfo;
        if (OperatingSystem.IsMacOS())
        {
            startInfo = new ProcessStartInfo("/usr/bin/open");
            startInfo.ArgumentList.Add(installerPath);
        }
        else
        {
            startInfo = new ProcessStartInfo(installerPath) { UseShellExecute = true };
        }

        using var process = _startProcess(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Не удалось открыть установщик. Приложение остаётся открытым.");
        }
        _shutdown();
    }

    private async Task<UpdateInfo?> FetchManifestAsync(CancellationToken cancellationToken)
    {
        var manifestName = _runtimeIdentifier.StartsWith("osx-", StringComparison.Ordinal)
            ? "latest-" + _runtimeIdentifier + ".json" : ManifestName;
        var url = UpdateFeedParser.JoinUrl(ChannelUrl, manifestName);
        var body = await ReadTextAsync(url, cancellationToken).ConfigureAwait(false);
        var manifest = UpdateFeedParser.ParseManifest(body, ChannelUrl, _runtimeIdentifier);
        if (manifest?.Signature is not null)
        {
            SignedUpdateManifest.Verify(manifest, _manifestPublicKey);
        }
        return manifest;
    }

    private async Task<IReadOnlyList<UpdateInfo>> FetchListingAsync(CancellationToken cancellationToken)
    {
        var body = await ReadTextAsync(ChannelUrl, cancellationToken).ConfigureAwait(false);
        return UpdateFeedParser.ParseListing(body, ChannelUrl, _runtimeIdentifier);
    }

    private async Task<string> ReadTextAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeoutToken(cancellationToken, _transferOptions.RequestTimeout);
        using var request = CreateRequest(url);
        try
        {
            using var response = await _send(request, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (body.Length > _transferOptions.MaxManifestBytes - read)
                {
                    throw new InvalidDataException("Ответ сервера обновлений слишком большой.");
                }
                body.Write(buffer, 0, read);
            }
            body.Position = 0;
            using var reader = new StreamReader(body, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Сервер обновлений не ответил вовремя.", exception);
        }
    }

    private static HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UpdaterUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        return request;
    }

    private static CancellationTokenSource CreateTimeoutToken(CancellationToken cancellationToken, TimeSpan duration)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(duration);
        return source;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
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

    private static string CurrentRuntimeIdentifier() => OperatingSystem.IsMacOS()
        ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64"
        : OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
}
