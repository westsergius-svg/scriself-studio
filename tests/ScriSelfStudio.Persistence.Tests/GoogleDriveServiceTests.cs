namespace ScriSelfStudio.Persistence.Tests;

using System.Net;
using System.Text;
using ScriSelfStudio.Contracts.Cloud;
using ScriSelfStudio.Persistence.Cloud;
using ScriSelfStudio.TestSupport;

public sealed class GoogleDriveServiceTests
{
    private static readonly GoogleOAuthClientSettings OAuth = new(
        "client-id",
        "client-secret",
        "https://accounts.google.com/o/oauth2/v2/auth",
        "https://oauth2.googleapis.com/token");

    [Fact]
    public void Authorization_scope_allows_reading_the_account_email()
    {
        var scopes = GoogleDriveService.AuthorizationScope.Split(' ');

        Assert.Contains(GoogleDriveService.DriveFileScope, scopes);
        Assert.Contains("openid", scopes);
        Assert.Contains("email", scopes);
    }

    [Fact]
    public async Task Restore_refreshes_token_and_reads_account_email()
    {
        string? tokenBody = null;
        var handler = new StubHandler(request => request.RequestUri!.Host switch
        {
            "oauth2.googleapis.com" => ReadTokenRequest(request),
            "www.googleapis.com" => Json("""{"email":"author@gmail.com"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var service = new GoogleDriveService(new HttpClient(handler), OAuth);

        var restored = await service.RestoreAsync("old@gmail.com", "refresh-token");

        Assert.True(restored.IsSuccess, restored.Error?.TechnicalDetails);
        Assert.True(service.IsConfigured);
        Assert.Equal("author@gmail.com", service.AccountLogin);
        Assert.Equal("refresh-token", service.SessionSecret);
        Assert.Contains("grant_type=refresh_token", tokenBody, StringComparison.Ordinal);
        Assert.Contains("refresh_token=refresh-token", tokenBody, StringComparison.Ordinal);

        HttpResponseMessage ReadTokenRequest(HttpRequestMessage request)
        {
            tokenBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""{"access_token":"access-token","expires_in":3600}""");
        }
    }

    [Fact]
    public async Task List_creates_app_folders_and_returns_only_project_files()
    {
        var createdFolders = 0;
        var handler = new StubHandler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.Host == "oauth2.googleapis.com")
            {
                return Json("""{"access_token":"access-token","expires_in":3600}""");
            }

            if (uri.AbsolutePath.EndsWith("/userinfo", StringComparison.Ordinal))
            {
                return Json("""{"email":"author@gmail.com"}""");
            }

            var query = Uri.UnescapeDataString(uri.Query);
            if (request.Method == HttpMethod.Get && query.Contains("name = 'ScriSelfStudio'", StringComparison.Ordinal))
            {
                return Json("""{"files":[]}""");
            }

            if (request.Method == HttpMethod.Get && query.Contains("name = 'Projects'", StringComparison.Ordinal))
            {
                return Json("""{"files":[]}""");
            }

            if (request.Method == HttpMethod.Post && uri.AbsolutePath.EndsWith("/files", StringComparison.Ordinal))
            {
                createdFolders++;
                return Json(createdFolders == 1 ? """{"id":"app-folder"}""" : """{"id":"projects-folder"}""");
            }

            if (request.Method == HttpMethod.Get && query.Contains("mimeType !=", StringComparison.Ordinal))
            {
                return Json("""
                    {"files":[
                      {"id":"file-1","name":"Роман.scri","size":"2048","modifiedTime":"2026-10-18T10:00:00Z"},
                      {"id":"file-2","name":"readme.txt","size":"10"}
                    ]}
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = new GoogleDriveService(new HttpClient(handler), OAuth);
        Assert.True((await service.RestoreAsync("author@gmail.com", "refresh-token")).IsSuccess);

        var listed = await service.ListAsync();

        Assert.True(listed.IsSuccess, listed.Error?.TechnicalDetails);
        var file = Assert.Single(listed.Require());
        Assert.Equal("Роман.scri", file.Name);
        Assert.Equal("file-1", file.RemotePath);
        Assert.Equal(2048, file.SizeBytes);
        Assert.Equal(2, createdFolders);
    }

    [Fact]
    public async Task Upload_sends_file_to_existing_projects_folder()
    {
        using var workspace = new TempWorkspace();
        var local = workspace.FilePath("Моя книга.scri");
        await File.WriteAllTextAsync(local, "project");

        var handler = new StubHandler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.Host == "oauth2.googleapis.com")
            {
                return Json("""{"access_token":"access-token","expires_in":3600}""");
            }

            if (uri.AbsolutePath.EndsWith("/userinfo", StringComparison.Ordinal))
            {
                return Json("""{"email":"author@gmail.com"}""");
            }

            var query = Uri.UnescapeDataString(uri.Query);
            if (request.Method == HttpMethod.Get && query.Contains("name = 'ScriSelfStudio'", StringComparison.Ordinal))
            {
                return Json("""{"files":[{"id":"app-folder","name":"ScriSelfStudio"}]}""");
            }

            if (request.Method == HttpMethod.Get && query.Contains("name = 'Projects'", StringComparison.Ordinal))
            {
                return Json("""{"files":[{"id":"projects-folder","name":"Projects"}]}""");
            }

            if (request.Method == HttpMethod.Get && query.Contains("name = 'Моя книга.scri'", StringComparison.Ordinal))
            {
                return Json("""{"files":[]}""");
            }

            if (request.Method == HttpMethod.Post && uri.Host == "www.googleapis.com")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.Location = new Uri("https://upload.example/session");
                return response;
            }

            if (request.Method == HttpMethod.Put && uri.Host == "upload.example")
            {
                return Json("""{"id":"uploaded-file"}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = new GoogleDriveService(new HttpClient(handler), OAuth);
        Assert.True((await service.RestoreAsync("author@gmail.com", "refresh-token")).IsSuccess);

        var uploaded = await service.UploadAsync(local);

        Assert.True(uploaded.IsSuccess, uploaded.Error?.TechnicalDetails);
        var initiate = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Post && request.RequestUri!.Host == "www.googleapis.com");
        Assert.Contains("uploadType=resumable", initiate.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.NotNull(initiate.Headers.GetValues("X-Upload-Content-Length").SingleOrDefault());
        var put = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Put);
        Assert.Equal("https://upload.example/session", put.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Download_writes_response_stream_to_local_file()
    {
        using var workspace = new TempWorkspace();
        var local = workspace.FilePath("Скачанный.scri");
        var handler = new StubHandler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.Host == "oauth2.googleapis.com")
            {
                return Json("""{"access_token":"access-token","expires_in":3600}""");
            }

            if (uri.AbsolutePath.EndsWith("/userinfo", StringComparison.Ordinal))
            {
                return Json("""{"email":"author@gmail.com"}""");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("cloud-project")),
            };
        });
        var service = new GoogleDriveService(new HttpClient(handler), OAuth);
        Assert.True((await service.RestoreAsync("author@gmail.com", "refresh-token")).IsSuccess);

        var downloaded = await service.DownloadAsync(
            new CloudFileInfo { Name = "Скачанный.scri", RemotePath = "file-id" },
            local);

        Assert.True(downloaded.IsSuccess, downloaded.Error?.TechnicalDetails);
        Assert.Equal("cloud-project", await File.ReadAllTextAsync(local));
        Assert.Contains("alt=media", handler.Requests.Last().RequestUri!.Query, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

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
