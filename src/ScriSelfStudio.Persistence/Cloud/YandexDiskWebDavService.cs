namespace ScriSelfStudio.Persistence.Cloud;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using ScriSelfStudio.Contracts.Cloud;
using ScriSelfStudio.Contracts.Errors;

/// <summary>
/// Облако на Яндекс.Диске через WebDAV. Автор подключает свой аккаунт паролем
/// приложения: приложение не хранит основной пароль и не требует регистрации
/// собственного OAuth-клиента. Проекты лежат в <c>ScriSelfStudio/Projects</c>.
/// </summary>
public sealed class YandexDiskWebDavService : ICloudStorageService
{
    public const string WebDavBaseUrl = "https://webdav.yandex.ru/";

    public const string AppFolder = "ScriSelfStudio";

    public const string ProjectsFolder = "ScriSelfStudio/Projects";

    private const string PropfindBody =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<d:propfind xmlns:d=\"DAV:\"><d:prop>" +
        "<d:displayname/><d:getcontentlength/><d:getlastmodified/><d:resourcetype/>" +
        "</d:prop></d:propfind>";

    private static readonly XNamespace Dav = "DAV:";

    private readonly HttpClient _client;
    private string? _login;
    private string? _password;

    public YandexDiskWebDavService()
        : this(new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
    {
    }

    /// <summary>Клиент с подменённым обработчиком — для тестов и локальных проверок.</summary>
    public YandexDiskWebDavService(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public CloudProviderKind Provider => CloudProviderKind.YandexDisk;

    public bool RequiresInteractiveSignIn => false;

    public bool IsConfigured => _login is not null && _password is not null;

    public string? AccountLogin => _login;

    public string? SessionSecret => _password;

    public async Task<Result> ConnectAsync(string? login, string? password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
        {
            return Result.FromError(
                ErrorCode.ValidationFailed,
                "Укажите логин Яндекса и пароль приложения.");
        }

        _login = login.Trim();
        _password = password;
        try
        {
            using var response = await SendPropfindAsync(new Uri(WebDavBaseUrl), depth: "0", cancellationToken);
            if (response.StatusCode is HttpStatusCode.MultiStatus or HttpStatusCode.OK)
            {
                return Result.Success;
            }

            var error = await MapAsync(response, "проверить подключение", cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                Disconnect();
            }

            return error;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Disconnect();
            return Result.FromError(
                ErrorCode.CloudOperationFailed,
                "Не удалось связаться с Яндекс.Диском. Проверьте интернет и повторите попытку.",
                technicalDetails: exception.Message,
                exception: exception);
        }
    }

    public Task<Result> ConnectInteractiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.FromError(
            ErrorCode.ValidationFailed,
            "Для Яндекс.Диска используется логин и пароль приложения."));

    public Task<Result> RestoreAsync(
        string? login,
        string? secret,
        CancellationToken cancellationToken = default) =>
        ConnectAsync(login, secret, cancellationToken);

    public void Disconnect()
    {
        _login = null;
        _password = null;
    }

    public async Task<Result<IReadOnlyList<CloudFileInfo>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Result<IReadOnlyList<CloudFileInfo>>.FromError(
                ErrorCode.CloudAuthFailed,
                "Сначала подключите Яндекс.Диск.");
        }

        try
        {
            var ensure = await EnsureFoldersAsync(cancellationToken);
            if (!ensure.IsSuccess)
            {
                return Result<IReadOnlyList<CloudFileInfo>>.Failure(ensure.Error!);
            }

            using var response = await SendPropfindAsync(FolderUri(ProjectsFolder), depth: "1", cancellationToken);
            if (response.StatusCode != HttpStatusCode.MultiStatus)
            {
                return Result<IReadOnlyList<CloudFileInfo>>.Failure(
                    (await MapAsync(response, "получить список файлов", cancellationToken)).Error!);
            }

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return Result<IReadOnlyList<CloudFileInfo>>.Success(ParseFiles(xml));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return Result<IReadOnlyList<CloudFileInfo>>.FromError(
                ErrorCode.CloudOperationFailed,
                "Не удалось получить список файлов с Яндекс.Диска.",
                technicalDetails: exception.Message,
                exception: exception);
        }
    }

    public async Task<Result> UploadAsync(string localFilePath, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Result.FromError(ErrorCode.CloudAuthFailed, "Сначала подключите Яндекс.Диск.");
        }

        if (!File.Exists(localFilePath))
        {
            return Result.FromError(ErrorCode.ProjectNotFound, "Файл проекта не найден на диске.");
        }

        try
        {
            var ensure = await EnsureFoldersAsync(cancellationToken);
            if (!ensure.IsSuccess)
            {
                return ensure;
            }

            var name = Path.GetFileName(localFilePath);
            var uri = FileUri(ProjectsFolder, name);
            await using var stream = File.OpenRead(localFilePath);
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var request = CreateRequest(HttpMethod.Put, uri);
            request.Content = content;
            request.Headers.ExpectContinue = false;

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return Result.Success;
            }

            return await MapAsync(response, "загрузить проект", cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return Result.FromError(
                ErrorCode.CloudOperationFailed,
                "Не удалось загрузить проект на Яндекс.Диск.",
                technicalDetails: exception.Message,
                exception: exception);
        }
    }

    public async Task<Result> DownloadAsync(CloudFileInfo file, string localFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!IsConfigured)
        {
            return Result.FromError(ErrorCode.CloudAuthFailed, "Сначала подключите Яндекс.Диск.");
        }

        var temporary = localFilePath + ".download";
        try
        {
            var directory = Path.GetDirectoryName(localFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var request = CreateRequest(HttpMethod.Get, FileUri(ProjectsFolder, file.Name));
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return await MapAsync(response, "скачать проект", cancellationToken);
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(temporary))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            File.Move(temporary, localFilePath, overwrite: true);
            return Result.Success;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return Result.FromError(
                ErrorCode.CloudOperationFailed,
                "Не удалось скачать проект с Яндекс.Диска.",
                technicalDetails: exception.Message,
                exception: exception);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task<Result> EnsureFoldersAsync(CancellationToken cancellationToken)
    {
        foreach (var folder in new[] { AppFolder, ProjectsFolder })
        {
            using var request = CreateRequest(new HttpMethod("MKCOL"), FolderUri(folder));
            using var response = await _client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode
                || response.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                continue;
            }

            return await MapAsync(response, "подготовить папку на Диске", cancellationToken);
        }

        return Result.Success;
    }

    private async Task<HttpResponseMessage> SendPropfindAsync(Uri uri, string depth, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(new HttpMethod("PROPFIND"), uri);
        request.Headers.Add("Depth", depth);
        request.Content = new StringContent(PropfindBody, Encoding.UTF8, "application/xml");
        request.Headers.ExpectContinue = false;
        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_login}:{_password}")));
        request.Headers.UserAgent.ParseAdd("ScriSelfStudio/1.0");
        return request;
    }

    private static Uri FolderUri(string relativePath) => new($"{WebDavBaseUrl}{relativePath}/");

    private static Uri FileUri(string folder, string fileName) =>
        new($"{WebDavBaseUrl}{folder}/{Uri.EscapeDataString(fileName)}");

    private static IReadOnlyList<CloudFileInfo> ParseFiles(string xml)
    {
        var document = XDocument.Parse(xml);
        var files = new List<CloudFileInfo>();
        foreach (var response in document.Descendants(Dav + "response"))
        {
            var properties = response.Element(Dav + "propstat")?.Element(Dav + "prop");
            if (properties is null
                || properties.Element(Dav + "resourcetype")?.Element(Dav + "collection") is not null)
            {
                continue;
            }

            var href = response.Element(Dav + "href")?.Value;
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var path = Uri.IsWellFormedUriString(href, UriKind.Absolute)
                ? new Uri(href).AbsolutePath
                : href;
            var decoded = Uri.UnescapeDataString(path).TrimEnd('/');
            var name = decoded[(decoded.LastIndexOf('/') + 1)..];
            if (string.IsNullOrWhiteSpace(name)
                || !name.EndsWith(".scri", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _ = long.TryParse(
                properties.Element(Dav + "getcontentlength")?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var size);
            var modified = DateTimeOffset.TryParse(
                properties.Element(Dav + "getlastmodified")?.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed)
                ? parsed
                : (DateTimeOffset?)null;

            files.Add(new CloudFileInfo
            {
                Name = name,
                RemotePath = $"{ProjectsFolder}/{name}",
                SizeBytes = size,
                ModifiedAt = modified,
            });
        }

        return files
            .OrderByDescending(file => file.ModifiedAt ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    private static async Task<Result> MapAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return Result.FromError(
                ErrorCode.CloudAuthFailed,
                "Яндекс.Диск отклонил логин или пароль приложения.",
                technicalDetails: $"HTTP {(int)response.StatusCode}");
        }

        if (response.StatusCode == HttpStatusCode.InsufficientStorage)
        {
            return Result.FromError(
                ErrorCode.CloudOperationFailed,
                "На Яндекс.Диске закончилось свободное место.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var details = string.IsNullOrWhiteSpace(body)
            ? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
            : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}";
        return Result.FromError(
            ErrorCode.CloudOperationFailed,
            $"Не удалось {operation}.",
            technicalDetails: details);
    }
}
