namespace ScriSelfStudio.Core.Tests;

using System.Net;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScriSelfStudio.Core.Updates;

public sealed class UpdateIntegrityTests : IDisposable
{
    private readonly RSA _signer = RSA.Create(2048);
    private readonly List<string> _downloads = [];

    [Theory]
    [InlineData("signed-update.json", "win-x64")]
    [InlineData("signed-update-macos.json", "osx-arm64")]
    public async Task Release_manifest_from_the_packager_is_verified_by_the_pinned_application_key(string fileName, string runtime)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
        var service = new ScriboriumUpdateService((_, _) => Task.FromResult(Response(json)),
            ScriboriumUpdateService.DefaultChannelUrl, "1.2.0", runtimeIdentifier: runtime);
        var result = await service.CheckAsync();
        Assert.False(result.HasError);
        Assert.NotNull(result.Latest);
        Assert.True(result.Latest.HasIntegrityInformation);
        SignedUpdateManifest.Verify(result.Latest, SignedUpdateManifest.PublicKey);
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(
            result.Latest with { Notes = "Changed after signing" }, SignedUpdateManifest.PublicKey));
    }

    [Fact]
    public async Task A_checksum_only_legacy_manifest_is_offered_for_manual_download()
    {
        var legacy = Sign("legacy") with { Signature = null };
        var service = Service((_, _) => Task.FromResult(Response(Manifest(legacy))));
        var result = await service.CheckAsync();
        Assert.False(result.HasError);
        Assert.NotNull(result.Latest);
        Assert.Equal(legacy.Sha256, result.Latest.Sha256);
        Assert.False(result.Latest.HasIntegrityInformation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_process_start_does_not_shut_down_and_the_verified_file_can_be_retried(bool throws)
    {
        var shutdown = false;
        var attempts = 0;
        var service = Service((_, _) => Task.FromResult(Response("verified synthetic installer")),
            shutdown: () => shutdown = true, startProcess: _ =>
            {
                attempts++;
                if (throws) { throw new Win32Exception(193); }
                return null;
            });
        var path = await service.DownloadAsync(Sign("verified synthetic installer"));
        _downloads.Add(path);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (throws) { Assert.Throws<Win32Exception>(() => service.LaunchInstaller(path)); }
            else { Assert.Throws<InvalidOperationException>(() => service.LaunchInstaller(path)); }
        }
        Assert.Equal(2, attempts);
        Assert.False(shutdown);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Windows_keeps_the_verified_installer_locked_during_the_process_start_decision()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        string? path = null;
        var service = Service((_, _) => Task.FromResult(Response("verified synthetic installer")), startProcess: start =>
        {
            Assert.Equal(path, start.FileName);
            Assert.Throws<IOException>(() => File.WriteAllText(path!, "replacement"));
            Assert.Throws<IOException>(() => File.Delete(path!));
            return null;
        });
        path = await service.DownloadAsync(Sign("verified synthetic installer"));
        _downloads.Add(path);
        Assert.Throws<InvalidOperationException>(() => service.LaunchInstaller(path));
        Assert.Equal("verified synthetic installer", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Signed_manifest_keeps_its_hash_and_is_verified_before_offering_installation()
    {
        var info = Sign("valid installer");
        var service = Service((_, _) => Task.FromResult(Response(Manifest(info))));
        var result = await service.CheckAsync();
        Assert.False(result.HasError);
        Assert.Equal(info.Sha256, result.Latest!.Sha256);
        Assert.Equal(info.Signature, result.Latest.Signature);
        Assert.True(result.Latest.HasIntegrityInformation);
    }

    [Fact]
    public async Task Changed_manifest_cannot_fall_back_to_an_unsigned_listing()
    {
        var signed = Sign("valid installer");
        var changed = signed with { Notes = "Changed release instructions" };
        var requests = 0;
        var service = Service((_, _) =>
        {
            requests++;
            return Task.FromResult(Response(requests == 1 ? Manifest(changed)
                : $"<a href='{signed.FileName}'>{signed.Display}</a>"));
        });
        var result = await service.CheckAsync();
        Assert.True(result.HasError);
        Assert.Null(result.Latest);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task Installer_with_a_different_hash_is_rejected_and_removed()
    {
        var info = Sign("expected installer");
        var service = Service((_, _) => Task.FromResult(Response("different installer")));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(info));
        AssertNoDownload(info.FileName);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("00", "invalid")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000", "invalid")]
    public async Task Missing_or_invalid_proof_is_rejected_before_the_installer_request(string? hash, string? signature)
    {
        var info = Sign("expected") with { Sha256 = hash, Signature = signature };
        var requests = 0;
        var service = Service((_, _) => { requests++; return Task.FromResult(Response("never downloaded")); });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(info));
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData("http://example.test/updates/test.exe")]
    [InlineData("https://other.test/updates/test.exe")]
    [InlineData("https://example.test/another/test.exe")]
    public async Task Installer_address_cannot_leave_the_https_channel(string url)
    {
        var requests = 0;
        var service = Service((_, _) => { requests++; return Task.FromResult(Response("never downloaded")); });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Sign("expected") with { Url = url }));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Concurrent_downloads_use_separate_files()
    {
        var info = Sign("same installer");
        var service = Service((_, _) => Task.FromResult(Response("same installer")));
        var paths = await Task.WhenAll(service.DownloadAsync(info), service.DownloadAsync(info));
        _downloads.AddRange(paths);
        Assert.NotEqual(paths[0], paths[1]);
        Assert.All(paths, path => Assert.Equal("same installer", File.ReadAllText(path)));
    }

    [Fact]
    public async Task Changing_the_downloaded_file_prevents_launch_and_shutdown()
    {
        var shutdown = false;
        var service = Service((_, _) => Task.FromResult(Response("valid installer")), shutdown: () => shutdown = true);
        var path = await service.DownloadAsync(Sign("valid installer"));
        _downloads.Add(path);
        await File.WriteAllTextAsync(path, "changed after download");
        Assert.Throws<InvalidDataException>(() => service.LaunchInstaller(path));
        Assert.False(shutdown);
    }

    [Fact]
    public void A_file_that_was_not_verified_by_this_service_cannot_launch()
    {
        var shutdown = false;
        var service = Service((_, _) => Task.FromResult(Response("unused")), shutdown: () => shutdown = true);
        Assert.Throws<InvalidDataException>(() => service.LaunchInstaller(Path.Combine(Path.GetTempPath(), "unverified.exe")));
        Assert.False(shutdown);
    }

    [Fact]
    public async Task An_active_stream_can_take_longer_than_the_manifest_request_timeout()
    {
        var body = new string('a', 400);
        var options = new UpdateTransferOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(50),
            IdleTimeout = TimeSpan.FromSeconds(2),
            DownloadTimeout = TimeSpan.FromSeconds(10),
        };
        var service = Service((_, _) => Task.FromResult(StreamResponse(body, TimeSpan.FromMilliseconds(25))), options);
        var progress = new ProgressRecorder();
        var path = await service.DownloadAsync(Sign(body), progress);
        _downloads.Add(path);
        Assert.Equal(body, File.ReadAllText(path));
        Assert.Contains(progress.Values, value => value > 0 && value < 1);
        Assert.Equal(1, progress.Values[^1]);
    }

    [Fact]
    public async Task A_stalled_stream_times_out_and_removes_its_partial_file()
    {
        var info = Sign("expected");
        var options = new UpdateTransferOptions { IdleTimeout = TimeSpan.FromMilliseconds(50) };
        var service = Service((_, _) => Task.FromResult(StreamResponse("expected", Timeout.InfiniteTimeSpan)), options);
        await Assert.ThrowsAsync<TimeoutException>(() => service.DownloadAsync(info));
        AssertNoDownload(info.FileName);
    }

    [Fact]
    public async Task User_cancellation_is_preserved_and_removes_its_partial_file()
    {
        var info = Sign("expected");
        var service = Service((_, _) => Task.FromResult(StreamResponse("expected", Timeout.InfiniteTimeSpan)));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(info, cancellationToken: cancellation.Token));
        AssertNoDownload(info.FileName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Oversized_downloads_are_rejected_with_and_without_content_length(bool includeLength)
    {
        var body = new string('a', 100);
        var info = Sign(body);
        var options = new UpdateTransferOptions { MaxDownloadBytes = 60 };
        var service = Service((_, _) => Task.FromResult(StreamResponse(body, TimeSpan.Zero, includeLength)), options);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(info));
        AssertNoDownload(info.FileName);
    }

    [Fact]
    public async Task Content_length_must_match_the_downloaded_body()
    {
        var info = Sign("expected");
        var response = Response("expected");
        response.Content.Headers.ContentLength = 100;
        var service = Service((_, _) => Task.FromResult(response));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(info));
        AssertNoDownload(info.FileName);
    }

    [Fact]
    public void A_listing_cannot_offer_a_foreign_installer_address()
    {
        Assert.Empty(UpdateFeedParser.ParseListing(
            "<a href='https://other.test/ScriSelfStudio-Setup-9.0.exe'>setup</a>", "https://example.test/updates/"));
        Assert.Empty(UpdateFeedParser.ParseVersion("999999999999999999999.1"));
    }

    private UpdateInfo Sign(string body)
    {
        var fileName = $"ScriSelfStudio-Setup-1.2.1-{Guid.NewGuid():N}.exe";
        var info = new UpdateInfo([1, 2, 1], "1.2.1", fileName,
            "https://example.test/updates/" + fileName, "Release notes", "manifest")
        { Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))) };
        return info with { Signature = Convert.ToBase64String(_signer.SignData(SignedUpdateManifest.Payload(info),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) };
    }

    private ScriboriumUpdateService Service(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        UpdateTransferOptions? options = null, Action? shutdown = null, Func<ProcessStartInfo, Process?>? startProcess = null) => new(send,
        "https://example.test/updates/", "1.2.0", shutdown, "win-x64", _signer.ExportSubjectPublicKeyInfoPem(), options, startProcess);

    private static string Manifest(UpdateInfo info) => JsonSerializer.Serialize(new
    { version = info.Display, file = info.FileName, notes = info.Notes, sha256 = info.Sha256, signature = info.Signature });

    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private static HttpResponseMessage StreamResponse(string body, TimeSpan delay, bool includeLength = true)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new StreamContent(new PacedStream(bytes, delay));
        if (includeLength) { content.Headers.ContentLength = bytes.Length; }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static void AssertNoDownload(string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), "ScriSelfStudioUpdate");
        Assert.Empty(Directory.EnumerateFiles(root, fileName + "*", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        _signer.Dispose();
        foreach (var path in _downloads) { File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!); }
    }

    private sealed class ProgressRecorder : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) => Values.Add(value);
    }

    private sealed class PacedStream(byte[] bytes, TimeSpan delay) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position == bytes.Length) { return 0; }
            await Task.Delay(delay, cancellationToken);
            var count = Math.Min(Math.Min(40, buffer.Length), bytes.Length - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
