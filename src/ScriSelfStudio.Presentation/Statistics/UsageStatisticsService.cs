namespace ScriSelfStudio.Presentation.Statistics;

using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Updates;

/// <summary>Один сигнал активности в сутки по UTC; только после согласия пользователя.</summary>
public sealed class UsageStatisticsService
{
    public const string Endpoint = "https://scriself.com/app-activity.php";

    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private readonly IAppSettingsService _settings;
    private readonly ISystemClock _clock;
    private readonly string _version;
    private readonly string _platform;
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
    private DateTimeOffset? _lastAttempt;
    private int _sending;

    public UsageStatisticsService(IAppSettingsService settings, IUpdateService updates, ISystemClock clock)
        : this(settings, clock, updates.CurrentVersion, RuntimeIdentifier(),
            (request, token) => Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
    {
    }

    public UsageStatisticsService(
        IAppSettingsService settings, ISystemClock clock, string version, string platform,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        _settings = settings;
        _clock = clock;
        _version = version;
        _platform = platform;
        _send = send;
    }

    /// <summary>Вызывается только когда окно активно. Ошибки сети не мешают работе.</summary>
    public async Task ReportActivityAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested
            || _settings.Current.ShareUsageStatistics != true
            || Interlocked.Exchange(ref _sending, 1) != 0)
        {
            return;
        }

        try
        {
            var now = _clock.UtcNow;
            var day = DateOnly.FromDateTime(now.UtcDateTime);
            var preferences = _settings.Current;
            var hasIdentity = IsInstallationId(preferences.StatisticsInstallationId);
            if (hasIdentity && preferences.StatisticsLastSentDay == day && preferences.StatisticsLastSentVersion == _version
                || _lastAttempt is { } last && now >= last && now - last < TimeSpan.FromMinutes(15))
            {
                return;
            }

            _lastAttempt = now;
            if (!hasIdentity)
            {
                var previousId = preferences.StatisticsInstallationId;
                var previousDay = preferences.StatisticsLastSentDay;
                var previousVersion = preferences.StatisticsLastSentVersion;
                var newId = Guid.NewGuid().ToString("D");
                var saved = await _settings.UpdateAsync(p =>
                {
                    p.StatisticsInstallationId = newId;
                    p.StatisticsLastSentDay = null;
                    p.StatisticsLastSentVersion = null;
                }, cancellationToken);
                if (!saved.IsSuccess)
                {
                    // UpdateAsync меняет Current даже при ошибке записи. Не отправляем
                    // несохранённый номер, иначе после перезапуска появится лишняя установка.
                    _settings.Current.StatisticsInstallationId = previousId;
                    _settings.Current.StatisticsLastSentDay = previousDay;
                    _settings.Current.StatisticsLastSentVersion = previousVersion;
                    return;
                }
            }

            preferences = _settings.Current;
            var installationId = preferences.StatisticsInstallationId;
            if (preferences.ShareUsageStatistics != true || cancellationToken.IsCancellationRequested) { return; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.UserAgent.ParseAdd("ScriSelfStudioStatistics/1.0");
            request.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["installation_id"] = installationId!,
                ["version"] = _version,
                ["platform"] = _platform,
            }), Encoding.UTF8, "application/json");
            using var response = await _send(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NoContent
                && _settings.Current.ShareUsageStatistics == true
                && _settings.Current.StatisticsInstallationId == installationId)
            {
                await _settings.UpdateAsync(p =>
                {
                    p.StatisticsLastSentDay = day;
                    p.StatisticsLastSentVersion = _version;
                }, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            // Без очереди и фоновой отправки накопленной истории: попробуем позже.
        }
        finally
        {
            Interlocked.Exchange(ref _sending, 0);
        }
    }

    private static bool IsInstallationId(string? value) =>
        Guid.TryParseExact(value, "D", out _)
        && value![14] == '4'
        && char.ToLowerInvariant(value[19]) is '8' or '9' or 'a' or 'b';

    private static string RuntimeIdentifier()
    {
        var platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return platform + "-" + architecture;
    }
}
