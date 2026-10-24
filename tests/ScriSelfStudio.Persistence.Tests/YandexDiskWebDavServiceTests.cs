namespace ScriSelfStudio.Persistence.Tests;

using System.Net;
using System.Text;
using ScriSelfStudio.Contracts.Cloud;
using ScriSelfStudio.Persistence.Cloud;

public sealed class YandexDiskWebDavServiceTests
{
    [Fact]
    public async Task Connect_accepts_webdav_multistatus_and_sends_basic_auth()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.MultiStatus));

        var connected = await Create(handler).ConnectAsync("author@yandex.ru", "app-password");

        Assert.True(connected.IsSuccess, connected.Error?.TechnicalDetails);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("PROPFIND", request.Method.Method);
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
        Assert.Equal(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("author@yandex.ru:app-password")),
            request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Connect_reports_bad_credentials_and_disconnects()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var service = Create(handler);

        var connected = await service.ConnectAsync("author@yandex.ru", "wrong");

        Assert.Equal(ScriSelfStudio.Contracts.Errors.ErrorCode.CloudAuthFailed, connected.Error?.Code);
        Assert.False(service.IsConfigured);
    }

    [Fact]
    public async Task List_returns_only_project_files_from_app_folder()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <d:multistatus xmlns:d="DAV:">
              <d:response>
                <d:href>/ScriSelfStudio/Projects/</d:href>
                <d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop></d:propstat>
              </d:response>
              <d:response>
                <d:href>/ScriSelfStudio/Projects/%D0%A0%D0%BE%D0%BC%D0%B0%D0%BD.scri</d:href>
                <d:propstat><d:prop>
                  <d:getcontentlength>2048</d:getcontentlength>
                  <d:getlastmodified>Sat, 17 Oct 2026 10:00:00 GMT</d:getlastmodified>
                  <d:resourcetype/>
                </d:prop></d:propstat>
              </d:response>
              <d:response>
                <d:href>/ScriSelfStudio/Projects/readme.txt</d:href>
                <d:propstat><d:prop><d:getcontentlength>10</d:getcontentlength><d:resourcetype/></d:prop></d:propstat>
              </d:response>
            </d:multistatus>
            """;

        var handler = new StubHandler(request =>
            request.Method.Method switch
            {
                "MKCOL" => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed),
                _ => new HttpResponseMessage(HttpStatusCode.MultiStatus)
                {
                    Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
                },
            });

        var service = Create(handler);
        Assert.True((await service.ConnectAsync("author", "password")).IsSuccess);

        var listed = await service.ListAsync();

        Assert.True(listed.IsSuccess, listed.Error?.TechnicalDetails);
        var file = Assert.Single(listed.Require());
        Assert.Equal("Роман.scri", file.Name);
        Assert.Equal(2048, file.SizeBytes);
    }

    [Fact]
    public async Task Upload_puts_file_into_projects_folder()
    {
        using var workspace = new ScriSelfStudio.TestSupport.TempWorkspace();
        var local = workspace.FilePath("Моя книга.scri");
        await File.WriteAllTextAsync(local, "project");

        var handler = new StubHandler(request => request.Method.Method switch
        {
            "MKCOL" => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed),
            "PUT" => new HttpResponseMessage(HttpStatusCode.Created),
            _ => new HttpResponseMessage(HttpStatusCode.MultiStatus),
        });

        var service = Create(handler);
        Assert.True((await service.ConnectAsync("author", "password")).IsSuccess);

        var uploaded = await service.UploadAsync(local);

        Assert.True(uploaded.IsSuccess, uploaded.Error?.TechnicalDetails);
        var put = Assert.Single(handler.Requests, request => request.Method.Method == "PUT");
        Assert.Contains("ScriSelfStudio/Projects/", put.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("Моя книга.scri"), put.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_writes_response_stream_to_local_file()
    {
        using var workspace = new ScriSelfStudio.TestSupport.TempWorkspace();
        var local = workspace.FilePath("Скачанный.scri");
        var handler = new StubHandler(request => request.Method.Method == "GET"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("cloud-project")),
            }
            : new HttpResponseMessage(HttpStatusCode.MultiStatus));

        var service = Create(handler);
        Assert.True((await service.ConnectAsync("author", "password")).IsSuccess);

        var downloaded = await service.DownloadAsync(
            new CloudFileInfo { Name = "Скачанный.scri", RemotePath = "ScriSelfStudio/Projects/Скачанный.scri" },
            local);

        Assert.True(downloaded.IsSuccess, downloaded.Error?.TechnicalDetails);
        Assert.Equal("cloud-project", await File.ReadAllTextAsync(local));
    }

    private static YandexDiskWebDavService Create(StubHandler handler) =>
        new(new HttpClient(handler));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }
}
