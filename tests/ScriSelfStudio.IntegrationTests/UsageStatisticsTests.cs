namespace ScriSelfStudio.IntegrationTests;

using System.Net;
using System.Text.Json;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Persistence.FileSystem;
using ScriSelfStudio.Presentation.Statistics;
using ScriSelfStudio.TestSupport;

public sealed class UsageStatisticsTests
{
    private const string Version = "1.1.12";
    private static readonly DateTimeOffset Today = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task No_identifier_or_network_request_without_consent(bool? choice)
    {
        using var workspace = new TempWorkspace();
        var settings = new JsonAppSettingsService(workspace.Root);
        settings.Current.ShareUsageStatistics = choice;
        var transport = new RecordingTransport();
        var service = Create(settings, new FakeClock(Today), transport);

        await service.ReportActivityAsync();

        Assert.Empty(transport.Payloads);
        Assert.Null(settings.Current.StatisticsInstallationId);
        Assert.False(File.Exists(settings.SettingsPath));
    }

    [Fact]
    public async Task Only_random_identifier_version_and_platform_are_sent_and_identity_survives_restart()
    {
        using var workspace = new TempWorkspace();
        var settings = Consent(workspace.Root);
        settings.Current.GoogleDriveAccountEmail = "private@example.test";
        settings.Current.YandexDiskLogin = "private-login";
        settings.Current.RecentProjects.Add(new RecentProjectEntry
        {
            FilePath = "C:/private/Manuscript.scri", Name = "Private manuscript", LastOpenedAt = Today,
        });
        var transport = new RecordingTransport();
        var clock = new FakeClock(Today);

        await Create(settings, clock, transport).ReportActivityAsync();

        var payload = Assert.Single(transport.Payloads);
        Assert.Equal(new[] { "installation_id", "platform", "version" }, payload.Keys.Order());
        var id = payload["installation_id"];
        Assert.True(Guid.TryParseExact(id, "D", out _));
        Assert.Equal('4', id[14]);
        Assert.Equal(Version, payload["version"]);
        Assert.Equal("win-x64", payload["platform"]);
        Assert.Equal(UsageStatisticsService.Endpoint, transport.Url);
        Assert.Equal("application/json", transport.ContentType);

        var restarted = new JsonAppSettingsService(workspace.Root);
        Assert.True((await restarted.LoadAsync()).IsSuccess);
        Assert.True(restarted.Current.ShareUsageStatistics);
        Assert.Equal(id, restarted.Current.StatisticsInstallationId);
        await Create(restarted, clock, transport).ReportActivityAsync();
        Assert.Single(transport.Payloads);

        clock.Advance(TimeSpan.FromDays(1));
        await Create(restarted, clock, transport).ReportActivityAsync();
        Assert.Equal(2, transport.Payloads.Count);
        Assert.All(transport.Payloads, p => Assert.Equal(id, p["installation_id"]));
    }

    [Fact]
    public async Task New_version_is_reported_same_day_without_creating_another_installation()
    {
        using var workspace = new TempWorkspace();
        var settings = Consent(workspace.Root);
        var transport = new RecordingTransport();
        var clock = new FakeClock(Today);

        await Create(settings, clock, transport, "1.1.11").ReportActivityAsync();
        await Create(settings, clock, transport).ReportActivityAsync();

        Assert.Equal(2, transport.Payloads.Count);
        Assert.Equal(transport.Payloads[0]["installation_id"], transport.Payloads[1]["installation_id"]);
        Assert.Equal(Version, settings.Current.StatisticsLastSentVersion);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Unacknowledged_activity_retries_after_fifteen_minutes(HttpStatusCode status)
    {
        using var workspace = new TempWorkspace();
        var settings = Consent(workspace.Root);
        var transport = new RecordingTransport { Status = status };
        var clock = new FakeClock(Today);
        var service = Create(settings, clock, transport);

        await service.ReportActivityAsync();
        Assert.Null(settings.Current.StatisticsLastSentDay);
        await service.ReportActivityAsync();
        clock.Advance(TimeSpan.FromMinutes(14));
        await service.ReportActivityAsync();
        Assert.Single(transport.Payloads);

        transport.Status = HttpStatusCode.NoContent;
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.ReportActivityAsync();
        Assert.Equal(2, transport.Payloads.Count);
        Assert.Equal(DateOnly.FromDateTime(Today.UtcDateTime), settings.Current.StatisticsLastSentDay);
    }

    [Fact]
    public async Task Offline_application_continues_and_retries_later()
    {
        using var workspace = new TempWorkspace();
        var settings = Consent(workspace.Root);
        var clock = new FakeClock(Today);
        var attempts = 0;
        var service = new UsageStatisticsService(settings, clock, Version, "osx-arm64", (_, _) =>
        {
            attempts++;
            if (attempts == 1) { throw new HttpRequestException("offline"); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        await service.ReportActivityAsync();
        Assert.Null(settings.Current.StatisticsLastSentDay);
        clock.Advance(TimeSpan.FromMinutes(15));
        await service.ReportActivityAsync();
        Assert.Equal(2, attempts);
        Assert.NotNull(settings.Current.StatisticsLastSentDay);
    }

    [Fact]
    public async Task Failed_identity_save_does_not_send_a_transient_installation_id()
    {
        using var workspace = new TempWorkspace();
        var blockedDirectory = workspace.FilePath("blocked");
        await File.WriteAllTextAsync(blockedDirectory, "Not a directory");
        var settings = Consent(blockedDirectory);
        var transport = new RecordingTransport();
        var clock = new FakeClock(Today);
        var service = Create(settings, clock, transport);

        await service.ReportActivityAsync();
        Assert.Empty(transport.Payloads);
        Assert.Null(settings.Current.StatisticsInstallationId);
        await service.ReportActivityAsync();
        Assert.Empty(transport.Payloads);

        File.Delete(blockedDirectory);
        clock.Advance(TimeSpan.FromMinutes(15));
        await service.ReportActivityAsync();
        Assert.Single(transport.Payloads);
        var restarted = new JsonAppSettingsService(blockedDirectory);
        Assert.True((await restarted.LoadAsync()).IsSuccess);
        Assert.Equal(transport.Payloads[0]["installation_id"], restarted.Current.StatisticsInstallationId);
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("0193c8f2-2345-1234-8234-0123456789ab")]
    public async Task Invalid_identity_is_repaired_even_if_old_daily_marker_exists(string invalidId)
    {
        using var workspace = new TempWorkspace();
        var settings = Consent(workspace.Root);
        settings.Current.StatisticsInstallationId = invalidId;
        settings.Current.StatisticsLastSentDay = DateOnly.FromDateTime(Today.UtcDateTime);
        settings.Current.StatisticsLastSentVersion = Version;
        var transport = new RecordingTransport();

        await Create(settings, new FakeClock(Today), transport).ReportActivityAsync();

        var payload = Assert.Single(transport.Payloads);
        Assert.NotEqual(invalidId, payload["installation_id"]);
        Assert.Equal('4', payload["installation_id"][14]);
    }

    [Fact]
    public async Task Concurrent_activation_events_send_only_one_request()
    {
        using var workspace = new TempWorkspace();
        var settings = Consent(workspace.Root);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var service = new UsageStatisticsService(settings, new FakeClock(Today), Version, "win-x64", (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            entered.SetResult();
            return response.Task;
        });

        var first = service.ReportActivityAsync();
        await entered.Task;
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.ReportActivityAsync()));
        Assert.Equal(1, attempts);
        response.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await first;
        Assert.NotNull(settings.Current.StatisticsLastSentDay);
    }

    [Fact]
    public async Task Revoking_consent_stops_future_reports_and_does_not_reset_the_identity()
    {
        using var workspace = new TempWorkspace();
        var settings = Consent(workspace.Root);
        var clock = new FakeClock(Today);
        var transport = new RecordingTransport();
        var service = Create(settings, clock, transport);
        await service.ReportActivityAsync();
        var id = settings.Current.StatisticsInstallationId;
        await settings.UpdateAsync(p => p.ShareUsageStatistics = false);
        clock.Advance(TimeSpan.FromDays(3));

        await service.ReportActivityAsync();

        Assert.Single(transport.Payloads);
        Assert.Equal(id, settings.Current.StatisticsInstallationId);
        await settings.UpdateAsync(p => p.ShareUsageStatistics = true);
        await service.ReportActivityAsync();
        Assert.Equal(2, transport.Payloads.Count);
        Assert.Equal(id, transport.Payloads[1]["installation_id"]);
    }

    [Fact]
    public async Task Cancellation_before_activity_does_not_create_an_identity()
    {
        using var workspace = new TempWorkspace();
        var settings = Consent(workspace.Root);
        var transport = new RecordingTransport();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Create(settings, new FakeClock(Today), transport).ReportActivityAsync(cancellation.Token);

        Assert.Empty(transport.Payloads);
        Assert.Null(settings.Current.StatisticsInstallationId);
    }

    [Fact]
    public async Task Cancellation_during_request_leaves_the_day_unreported()
    {
        using var workspace = new TempWorkspace();
        var settings = Consent(workspace.Root);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var service = new UsageStatisticsService(settings, new FakeClock(Today), Version, "win-x64", async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var pending = service.ReportActivityAsync(cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        await pending;

        Assert.Null(settings.Current.StatisticsLastSentDay);
    }

    [Fact]
    public async Task The_day_boundary_uses_UTC_and_clock_adjustment_does_not_block_retry()
    {
        using var workspace = new TempWorkspace();
        var settings = Consent(workspace.Root);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 30, 6, 30, 0, TimeSpan.FromHours(7)));
        var transport = new RecordingTransport();
        var service = Create(settings, clock, transport);
        await service.ReportActivityAsync();
        Assert.Equal(new DateOnly(2026, 10, 29), settings.Current.StatisticsLastSentDay);

        clock.Advance(TimeSpan.FromMinutes(31));
        transport.Status = HttpStatusCode.ServiceUnavailable;
        await service.ReportActivityAsync();
        clock.Advance(TimeSpan.FromMinutes(-1));
        transport.Status = HttpStatusCode.NoContent;
        await service.ReportActivityAsync();

        Assert.Equal(3, transport.Payloads.Count);
        Assert.Equal(new DateOnly(2026, 10, 30), settings.Current.StatisticsLastSentDay);
    }

    private static JsonAppSettingsService Consent(string directory)
    {
        var settings = new JsonAppSettingsService(directory);
        settings.Current.ShareUsageStatistics = true;
        return settings;
    }

    private static UsageStatisticsService Create(IAppSettingsService settings, FakeClock clock, RecordingTransport transport, string version = Version) =>
        new(settings, clock, version, "win-x64", transport.SendAsync);

    private sealed class RecordingTransport
    {
        public List<Dictionary<string, string>> Payloads { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.NoContent;
        public string? Url { get; private set; }
        public string? ContentType { get; private set; }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Url = request.RequestUri!.AbsoluteUri;
            ContentType = request.Content!.Headers.ContentType!.MediaType;
            var body = await request.Content.ReadAsStringAsync(token);
            Payloads.Add(JsonSerializer.Deserialize<Dictionary<string, string>>(body)!);
            return new HttpResponseMessage(Status);
        }
    }
}
