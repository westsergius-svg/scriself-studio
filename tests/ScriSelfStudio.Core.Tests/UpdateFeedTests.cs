namespace ScriSelfStudio.Core.Tests;

using System.Net;
using System.Net.Http;
using System.Text;
using ScriSelfStudio.Core.Updates;

/// <summary>
/// Канал обновлений. Контракт снят с работающего апдейтера Scriborium 1.1.3.3S:
/// манифест <c>latest.json</c> в каталоге <c>uploads/scrisoft/</c> и листинг
/// каталога как резервный путь.
/// </summary>
public sealed class UpdateFeedTests
{
    /// <summary>Ответ сайта на <c>https://scriborium.ru/uploads/scrisoft/latest.json</c>.</summary>
    private const string ManifestJson = """
        {
          "version": "1.1.3.3S",
          "file": "Scriborium-Setup-Beta 1.1.3.3S.exe",
          "notes": ""
        }
        """;

    [Fact]
    public void Channel_address_is_the_same_as_scriborium_1_1_3_3S()
    {
        Assert.Equal("https://scriborium.ru/uploads/scrisoft/", ScriboriumUpdateService.DefaultChannelUrl);
        Assert.Equal("latest.json", ScriboriumUpdateService.ManifestName);
    }

    [Fact]
    public void Manifest_from_the_site_is_parsed()
    {
        var info = UpdateFeedParser.ParseManifest(ManifestJson, ScriboriumUpdateService.DefaultChannelUrl);

        Assert.NotNull(info);
        Assert.Equal("1.1.3.3S", info.Display);
        Assert.Equal("1.1.3.3", info.VersionText);
        Assert.Equal("manifest", info.Source);
        Assert.Equal(
            "https://scriborium.ru/uploads/scrisoft/Scriborium-Setup-Beta%201.1.3.3S.exe",
            info.Url);
    }

    [Theory]
    [InlineData("1.1.4", "1.1.3.3", 1)]
    [InlineData("1.1.3.3S", "1.1.3.3", 0)]
    [InlineData("1.1.3.3", "1.1.3.10", -1)]
    [InlineData("1.2", "1.1.9.9", 1)]
    public void Versions_are_compared_by_number(string left, string right, int expected)
    {
        var result = UpdateFeedParser.CompareVersions(
            UpdateFeedParser.ParseVersion(left),
            UpdateFeedParser.ParseVersion(right));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Empty_or_text_only_version_has_no_numbers()
    {
        Assert.Empty(UpdateFeedParser.ParseVersion(null));
        Assert.Empty(UpdateFeedParser.ParseVersion("beta"));
    }

    [Fact]
    public void Listing_skips_lite_builds_and_takes_the_newest()
    {
        const string listing = """
            <html><body>
            <a href="Scriborium-Setup-Beta%201.1.3.1.exe">1.1.3.1</a>
            <a href="Scriborium-Setup-Beta%201.1.3.3S.exe">1.1.3.3S</a>
            <a href="Scriborium-Lite-Setup%201.1.3.9.exe">lite</a>
            <a href="readme.txt">readme</a>
            </body></html>
            """;

        var newest = UpdateFeedParser.Newest(
            UpdateFeedParser.ParseListing(listing, ScriboriumUpdateService.DefaultChannelUrl));

        Assert.NotNull(newest);
        Assert.Equal("1.1.3.3", newest.VersionText);
        Assert.Equal("Scriborium-Setup-Beta 1.1.3.3S.exe", newest.FileName);
    }

    [Fact]
    public void Listing_takes_the_scriself_studio_setup_next_to_the_old_scriborium_builds()
    {
        const string listing = """
            <html><body>
            <a href="Scriborium-Setup-Beta%201.1.3.3S.exe">old</a>
            <a href="ScriSelfStudio-Setup-1.1.5.exe">new</a>
            <a href="ScriSelfStudio-Lite-Setup-1.1.6.exe">lite</a>
            </body></html>
            """;

        var newest = UpdateFeedParser.Newest(
            UpdateFeedParser.ParseListing(listing, ScriboriumUpdateService.DefaultChannelUrl));

        Assert.NotNull(newest);
        Assert.Equal("1.1.5", newest.VersionText);
        Assert.Equal("ScriSelfStudio-Setup-1.1.5.exe", newest.FileName);
    }

    [Fact]
    public async Task Check_reports_the_published_version_from_the_manifest()
    {
        var service = CreateService(ManifestJson, "1.1.3.3S");

        var result = await service.CheckAsync();

        Assert.False(result.HasError);
        Assert.NotNull(result.Latest);
        Assert.Equal("1.1.3.3S", result.Latest.Display);
        Assert.False(service.IsNewer(result.Latest));
    }

    [Fact]
    public async Task Check_does_not_offer_the_older_build_of_the_old_app()
    {
        var service = CreateService(ManifestJson, "1.1.4");

        var result = await service.CheckAsync();

        Assert.NotNull(result.Latest);
        Assert.False(service.IsNewer(result.Latest));
    }

    [Fact]
    public async Task Check_offers_a_newer_build()
    {
        var service = CreateService(ManifestJson, "1.1.3.1");

        var result = await service.CheckAsync();

        Assert.NotNull(result.Latest);
        Assert.True(service.IsNewer(result.Latest));
    }

    [Fact]
    public async Task Check_falls_back_to_the_directory_listing()
    {
        const string listing = """
            <html><body>
            <a href="Scriborium-Setup-Beta%201.1.3.5.exe">1.1.3.5</a>
            </body></html>
            """;

        var service = new ScriboriumUpdateService(
            (request, _) => Task.FromResult(
                request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : TextResponse(listing)),
            ScriboriumUpdateService.DefaultChannelUrl,
            "1.1.4");

        var result = await service.CheckAsync();

        Assert.False(result.HasError);
        Assert.NotNull(result.Latest);
        Assert.Equal("listing", result.Latest.Source);
        Assert.Equal("1.1.3.5", result.Latest.VersionText);
    }

    [Fact]
    public async Task Check_reports_an_error_when_the_channel_is_unreachable()
    {
        var service = new ScriboriumUpdateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            ScriboriumUpdateService.DefaultChannelUrl,
            "1.1.4");

        var result = await service.CheckAsync();

        Assert.True(result.HasError);
        Assert.Null(result.Latest);
    }

    [Fact]
    public async Task Download_writes_the_installer_to_the_temp_folder()
    {
        const string payload = "installer payload";
        var info = new UpdateInfo(
            [1, 1, 4],
            "1.1.4",
            "ScriSelfStudioUpdateTest.exe",
            "https://example.test/ScriSelfStudioUpdateTest.exe",
            string.Empty,
            "manifest");
        var service = new ScriboriumUpdateService(
            (_, _) => Task.FromResult(TextResponse(payload)),
            "https://example.test/",
            "1.1.4");

        var path = await service.DownloadAsync(info);

        try
        {
            Assert.Equal("ScriSelfStudioUpdateTest.exe", Path.GetFileName(path));
            Assert.Equal(payload, await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ScriboriumUpdateService CreateService(string manifest, string currentVersion) =>
        new(
            (_, _) => Task.FromResult(TextResponse(manifest)),
            ScriboriumUpdateService.DefaultChannelUrl,
            currentVersion);

    private static HttpResponseMessage TextResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain"),
        };
}
