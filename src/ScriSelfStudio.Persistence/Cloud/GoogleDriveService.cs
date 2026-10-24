namespace ScriSelfStudio.Persistence.Cloud;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScriSelfStudio.Contracts.Cloud;
using ScriSelfStudio.Contracts.Errors;

/// <summary>Параметры OAuth-клиента Google для установленного приложения.</summary>
public sealed record GoogleOAuthClientSettings(
    string ClientId,
    string ClientSecret,
    string AuthorizationUri,
    string TokenUri);

/// <summary>
/// Google Drive через OAuth 2.0. Пользователь входит в браузере, приложение
/// получает refresh-токен и работает только с папкой <c>ScriSelfStudio/Projects</c>.
/// </summary>
public sealed class GoogleDriveService : ICloudStorageService
{
    public const string DriveApiBaseUrl = "https://www.googleapis.com/drive/v3/files";
    public const string UploadApiBaseUrl = "https://www.googleapis.com/upload/drive/v3/files";
    public const string UserInfoUrl = "https://www.googleapis.com/oauth2/v2/userinfo";
    public const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";
    public const string OpenIdScope = "openid";
    public const string EmailScope = "email";
    public const string AuthorizationScope = DriveFileScope + " " + OpenIdScope + " " + EmailScope;
    public const string AppFolderName = "ScriSelfStudio";
    public const string ProjectsFolderName = "Projects";

    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string ProjectExtension = ".scri";

    private readonly HttpClient _client;
    private readonly GoogleOAuthClientSettings _oauth;
    private readonly Func<string, Task> _openBrowser;
    private string? _accountLogin;
    private string? _refreshToken;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;
    private string? _appFolderId;
    private string? _projectsFolderId;

    public GoogleDriveService()
        : this(new HttpClient { Timeout = TimeSpan.FromMinutes(10) }, LoadOAuthClient(), OpenBrowserAsync)
    {
    }

    /// <summary>Клиент с подменённым обработчиком — для тестов и локальных проверок.</summary>
    public GoogleDriveService(HttpClient client)
        : this(client, LoadOAuthClient(), OpenBrowserAsync)
    {
    }

    public GoogleDriveService(
        HttpClient client,
        GoogleOAuthClientSettings oauth,
        Func<string, Task>? openBrowser = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(oauth);
        _client = client;
        _oauth = oauth;
        _openBrowser = openBrowser ?? OpenBrowserAsync;
    }

    public CloudProviderKind Provider => CloudProviderKind.GoogleDrive;

    public bool RequiresInteractiveSignIn => true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_refreshToken);

    public string? AccountLogin => _accountLogin;

    public string? SessionSecret => _refreshToken;

    public async Task<Result> ConnectAsync(
        string? login,
        string? password,
        CancellationToken cancellationToken = default) =>
        await ConnectInteractiveAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Result> ConnectInteractiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var authorized = await AuthorizeAsync(cancellationToken).ConfigureAwait(false);
            if (!authorized.IsSuccess)
            {
                return Result.Failure(authorized.Error!);
            }

            var tokens = authorized.Require();
            if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
            {
                Disconnect();
                return Result.FromError(
                    ErrorCode.CloudAuthFailed,
                    "Google не выдал долгосрочный доступ. Повторите вход и разрешите доступ приложению.");
            }

            _refreshToken = tokens.RefreshToken;
            SetAccessToken(tokens.AccessToken, tokens.ExpiresIn);

            var email = await ReadAccountEmailAsync(cancellationToken).ConfigureAwait(false);
            if (!email.IsSuccess)
            {
                Disconnect();
                return Result.Failure(email.Error!);
            }

            _accountLogin = email.Require();
            return Result.Success;
        }
        catch (OperationCanceledException)
        {
            Disconnect();
            return Result.FromError(ErrorCode.CloudOperationFailed, "Вход в Google Drive отменён.");
        }
        catch (HttpRequestException exception)
        {
            Disconnect();
            return Result.FromError(
                ErrorCode.CloudOperationFailed,
                "Не удалось связаться с Google Drive. Проверьте интернет и повторите попытку.",
                technicalDetails: exception.Message,
                exception: exception);
        }
    }

    public async Task<Result> RestoreAsync(
        string? login,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Result.FromError(ErrorCode.ValidationFailed, "Нет сохранённого доступа к Google Drive.");
        }

        _accountLogin = login;
        _refreshToken = secret;
        var refreshed = await RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!refreshed.IsSuccess)
        {
            Disconnect();
            return refreshed;
        }

        var email = await ReadAccountEmailAsync(cancellationToken).ConfigureAwait(false);
        if (!email.IsSuccess)
        {
            Disconnect();
            return Result.Failure(email.Error!);
        }

        _accountLogin = email.Require();
        return Result.Success;
    }

    public void Disconnect()
    {
        _accountLogin = null;
        _refreshToken = null;
        _accessToken = null;
        _accessTokenExpiresAt = default;
        _appFolderId = null;
        _projectsFolderId = null;
    }

    public async Task<Result<IReadOnlyList<CloudFileInfo>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Result<IReadOnlyList<CloudFileInfo>>.FromError(
                ErrorCode.CloudAuthFailed,
                "Сначала подключите Google Drive.");
        }

        try
        {
            var folders = await EnsureFoldersAsync(cancellationToken).ConfigureAwait(false);
            if (!folders.IsSuccess)
            {
                return Result<IReadOnlyList<CloudFileInfo>>.Failure(folders.Error!);
            }

            var files = await FindProjectFilesAsync(folders.Require().ProjectsFolderId, cancellationToken)
                .ConfigureAwait(false);
            return files.IsSuccess
                ? Result<IReadOnlyList<CloudFileInfo>>.Success(files.Require())
                : Result<IReadOnlyList<CloudFileInfo>>.Failure(files.Error!);
        }
        catch (HttpRequestException exception)
        {
            return Result<IReadOnlyList<CloudFileInfo>>.FromError(
                ErrorCode.CloudOperationFailed,
                "Не удалось получить список файлов Google Drive.",
                technicalDetails: exception.Message,
                exception: exception);
        }
        catch (JsonException exception)
        {
            return Result<IReadOnlyList<CloudFileInfo>>.FromError(
                ErrorCode.CloudOperationFailed,
                "Google Drive вернул неожиданный ответ.",
                technicalDetails: exception.Message,
                exception: exception);
        }
    }

    public async Task<Result> UploadAsync(string localFilePath, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Result.FromError(ErrorCode.CloudAuthFailed, "Сначала подключите Google Drive.");
        }

        if (!File.Exists(localFilePath))
        {
            return Result.FromError(ErrorCode.FileOperationFailed, "Файл проекта не найден.");
        }

        try
        {
            var folders = await EnsureFoldersAsync(cancellationToken).ConfigureAwait(false);
            if (!folders.IsSuccess)
            {
                return Result.Failure(folders.Error!);
            }

            var fileName = Path.GetFileName(localFilePath);
            var existing = await FindFileAsync(fileName, folders.Require().ProjectsFolderId, cancellationToken)
                .ConfigureAwait(false);
            if (!existing.IsSuccess)
            {
                return Result.Failure(existing.Error!);
            }

            return await UploadFileAsync(
                localFilePath,
                fileName,
                folders.Require().ProjectsFolderId,
                existing.Require(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            return Result.FromError(
                ErrorCode.FileOperationFailed,
                "Не удалось прочитать файл проекта.",
                technicalDetails: exception.Message,
                exception: exception);
        }
        catch (HttpRequestException exception)
        {
            return Result.FromError(
                ErrorCode.CloudOperationFailed,
                "Не удалось загрузить проект в Google Drive.",
                technicalDetails: exception.Message,
                exception: exception);
        }
    }

    public async Task<Result> DownloadAsync(
        CloudFileInfo file,
        string localFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Result.FromError(ErrorCode.CloudAuthFailed, "Сначала подключите Google Drive.");
        }

        try
        {
            var token = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!token.IsSuccess)
            {
                return token;
            }

            var url = $"{DriveApiBaseUrl}/{Uri.EscapeDataString(file.RemotePath)}?alt=media";
            using var request = CreateAuthorizedRequest(HttpMethod.Get, url);
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await MapAsync(response, "скачать проект из Google Drive", cancellationToken)
                    .ConfigureAwait(false);
            }

            var temporary = localFilePath + ".cloud.tmp";
            try
            {
                await using (var target = File.Create(temporary))
                {
                    await response.Content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporary, localFilePath, overwrite: true);
                return Result.Success;
            }
            catch
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }

                throw;
            }
        }
        catch (IOException exception)
        {
            return Result.FromError(
                ErrorCode.FileOperationFailed,
                "Не удалось сохранить проект из Google Drive.",
                technicalDetails: exception.Message,
                exception: exception);
        }
        catch (HttpRequestException exception)
        {
            return Result.FromError(
                ErrorCode.CloudOperationFailed,
                "Не удалось скачать проект из Google Drive.",
                technicalDetails: exception.Message,
                exception: exception);
        }
    }

    private static async Task OpenBrowserAsync(string url)
    {
        await Task.Yield();
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    private async Task<Result<OAuthTokens>> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var port = GetFreeLoopbackPort();
        var redirectUri = $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}/";
        var verifier = CreateRandomUrlSafeToken(64);
        var challenge = CreateCodeChallenge(verifier);
        var state = CreateRandomUrlSafeToken(32);
        var authorizationUrl = BuildAuthorizationUrl(redirectUri, challenge, state);

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        try
        {
            await _openBrowser(authorizationUrl).ConfigureAwait(false);
            var context = await listener.GetContextAsync()
                .WaitAsync(TimeSpan.FromMinutes(5), cancellationToken)
                .ConfigureAwait(false);

            var query = context.Request.QueryString;
            var error = query["error"];
            var code = query["code"];
            var returnedState = query["state"];
            await WriteBrowserResponseAsync(context.Response, string.IsNullOrEmpty(error), cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(error))
            {
                return Result<OAuthTokens>.FromError(
                    ErrorCode.CloudAuthFailed,
                    "Google Drive отклонил доступ. Попробуйте войти снова.");
            }

            if (string.IsNullOrWhiteSpace(code) || !string.Equals(returnedState, state, StringComparison.Ordinal))
            {
                return Result<OAuthTokens>.FromError(
                    ErrorCode.CloudAuthFailed,
                    "Google вернул некорректный ответ входа. Повторите попытку.");
            }

            return await ExchangeCodeAsync(code, redirectUri, verifier, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    private string BuildAuthorizationUrl(string redirectUri, string challenge, string state)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = _oauth.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = AuthorizationScope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
        };
        var queryString = string.Join(
            "&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return _oauth.AuthorizationUri + "?" + queryString;
    }

    private async Task<Result<OAuthTokens>> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string verifier,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["client_id"] = _oauth.ClientId,
            ["client_secret"] = _oauth.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        });
        using var response = await _client.PostAsync(_oauth.TokenUri, content, cancellationToken).ConfigureAwait(false);
        return await ReadTokenResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_refreshToken))
        {
            return Result.FromError(ErrorCode.CloudAuthFailed, "Сначала подключите Google Drive.");
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = _oauth.ClientId,
            ["client_secret"] = _oauth.ClientSecret,
            ["refresh_token"] = _refreshToken,
            ["grant_type"] = "refresh_token",
        });
        using var response = await _client.PostAsync(_oauth.TokenUri, content, cancellationToken).ConfigureAwait(false);
        var tokens = await ReadTokenResponseAsync(response, cancellationToken).ConfigureAwait(false);
        if (!tokens.IsSuccess)
        {
            return Result.Failure(tokens.Error!);
        }

        var value = tokens.Require();
        SetAccessToken(value.AccessToken, value.ExpiresIn);
        return Result.Success;
    }

    private async Task<Result<OAuthTokens>> ReadTokenResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var code = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? ErrorCode.CloudAuthFailed
                : ErrorCode.CloudOperationFailed;
            return Result<OAuthTokens>.FromError(
                code,
                code == ErrorCode.CloudAuthFailed
                    ? "Google Drive отклонил доступ. Войдите снова."
                    : "Не удалось получить доступ к Google Drive.",
                technicalDetails: $"HTTP {(int)response.StatusCode}: {body}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var access) ? access.GetString() : null;
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds)
            ? seconds
            : 3600;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Result<OAuthTokens>.FromError(
                ErrorCode.CloudAuthFailed,
                "Google не выдал токен доступа. Повторите вход.");
        }

        return Result<OAuthTokens>.Success(new OAuthTokens(accessToken, refreshToken, expiresIn));
    }

    private async Task<Result<string>> ReadAccountEmailAsync(CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, UserInfoUrl);
        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Failure(
                (await MapAsync(response, "получить аккаунт Google", cancellationToken).ConfigureAwait(false)).Error!);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var email = document.RootElement.TryGetProperty("email", out var value) ? value.GetString() : null;
        return string.IsNullOrWhiteSpace(email)
            ? Result<string>.FromError(ErrorCode.CloudAuthFailed, "Google не вернул email аккаунта.")
            : Result<string>.Success(email);
    }

    private async Task<Result<DriveFolders>> EnsureFoldersAsync(CancellationToken cancellationToken)
    {
        var token = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!token.IsSuccess)
        {
            return Result<DriveFolders>.Failure(token.Error!);
        }

        if (_appFolderId is not null && _projectsFolderId is not null)
        {
            return Result<DriveFolders>.Success(new DriveFolders(_appFolderId, _projectsFolderId));
        }

        var appFolder = await FindOrCreateFolderAsync(AppFolderName, parentId: null, cancellationToken)
            .ConfigureAwait(false);
        if (!appFolder.IsSuccess)
        {
            return Result<DriveFolders>.Failure(appFolder.Error!);
        }

        var projects = await FindOrCreateFolderAsync(ProjectsFolderName, appFolder.Require(), cancellationToken)
            .ConfigureAwait(false);
        if (!projects.IsSuccess)
        {
            return Result<DriveFolders>.Failure(projects.Error!);
        }

        _appFolderId = appFolder.Require();
        _projectsFolderId = projects.Require();
        return Result<DriveFolders>.Success(new DriveFolders(_appFolderId, _projectsFolderId));
    }

    private async Task<Result<string>> FindOrCreateFolderAsync(
        string name,
        string? parentId,
        CancellationToken cancellationToken)
    {
        var found = await FindFolderAsync(name, parentId, cancellationToken).ConfigureAwait(false);
        if (!found.IsSuccess)
        {
            return Result<string>.Failure(found.Error!);
        }

        return found.Require() is { } id
            ? Result<string>.Success(id)
            : await CreateFolderAsync(name, parentId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<string?>> FindFolderAsync(
        string name,
        string? parentId,
        CancellationToken cancellationToken)
    {
        var query = $"name = '{EscapeQueryValue(name)}' and mimeType = '{FolderMimeType}' and trashed = false";
        if (parentId is not null)
        {
            query += $" and '{EscapeQueryValue(parentId)}' in parents";
        }

        var url = $"{DriveApiBaseUrl}?q={Uri.EscapeDataString(query)}&fields=files(id,name)&spaces=drive&pageSize=10";
        var files = await ReadFilesAsync(url, cancellationToken).ConfigureAwait(false);
        if (!files.IsSuccess)
        {
            return Result<string?>.Failure(files.Error!);
        }

        return Result<string?>.Success(files.Require().Select(item => item.Id).FirstOrDefault());
    }

    private async Task<Result<string>> CreateFolderAsync(
        string name,
        string? parentId,
        CancellationToken cancellationToken)
    {
        var metadata = parentId is null
            ? JsonSerializer.Serialize(new { name, mimeType = FolderMimeType })
            : JsonSerializer.Serialize(new { name, mimeType = FolderMimeType, parents = new[] { parentId } });
        using var content = new StringContent(metadata, Encoding.UTF8, "application/json");
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"{DriveApiBaseUrl}?fields=id", content);
        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Failure(
                (await MapAsync(response, "подготовить папку Google Drive", cancellationToken).ConfigureAwait(false)).Error!);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var id = document.RootElement.TryGetProperty("id", out var value) ? value.GetString() : null;
        return string.IsNullOrWhiteSpace(id)
            ? Result<string>.FromError(ErrorCode.CloudOperationFailed, "Google Drive не вернул идентификатор папки.")
            : Result<string>.Success(id);
    }

    private async Task<Result<string?>> FindFileAsync(
        string name,
        string folderId,
        CancellationToken cancellationToken)
    {
        var query = $"name = '{EscapeQueryValue(name)}' and '{EscapeQueryValue(folderId)}' in parents and trashed = false";
        var url = $"{DriveApiBaseUrl}?q={Uri.EscapeDataString(query)}&fields=files(id,name)&spaces=drive&pageSize=10";
        var files = await ReadFilesAsync(url, cancellationToken).ConfigureAwait(false);
        if (!files.IsSuccess)
        {
            return Result<string?>.Failure(files.Error!);
        }

        return Result<string?>.Success(files.Require().Select(item => item.Id).FirstOrDefault());
    }

    private async Task<Result<IReadOnlyList<CloudFileInfo>>> FindProjectFilesAsync(
        string folderId,
        CancellationToken cancellationToken)
    {
        var query = $"'{EscapeQueryValue(folderId)}' in parents and trashed = false and mimeType != '{FolderMimeType}'";
        var url = $"{DriveApiBaseUrl}?q={Uri.EscapeDataString(query)}&fields=files(id,name,size,modifiedTime)&orderBy=modifiedTime desc&spaces=drive&pageSize=1000";
        var files = await ReadFilesAsync(url, cancellationToken).ConfigureAwait(false);
        if (!files.IsSuccess)
        {
            return Result<IReadOnlyList<CloudFileInfo>>.Failure(files.Error!);
        }

        var result = files.Require()
            .Where(file => file.Name.EndsWith(ProjectExtension, StringComparison.OrdinalIgnoreCase))
            .Select(file => new CloudFileInfo
            {
                Name = file.Name,
                RemotePath = file.Id,
                SizeBytes = file.SizeBytes,
                ModifiedAt = file.ModifiedAt,
            })
            .OrderByDescending(file => file.ModifiedAt ?? DateTimeOffset.MinValue)
            .ToArray();
        return Result<IReadOnlyList<CloudFileInfo>>.Success(result);
    }

    private async Task<Result<IReadOnlyList<DriveFile>>> ReadFilesAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, url);
        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<DriveFile>>.Failure(
                (await MapAsync(response, "получить список Google Drive", cancellationToken).ConfigureAwait(false)).Error!);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var files = new List<DriveFile>();
        if (!document.RootElement.TryGetProperty("files", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Result<IReadOnlyList<DriveFile>>.Success(files);
        }

        foreach (var item in array.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            var name = item.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            _ = long.TryParse(
                item.TryGetProperty("size", out var sizeValue) ? sizeValue.GetString() : null,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var size);
            var modified = item.TryGetProperty("modifiedTime", out var modifiedValue)
                && DateTimeOffset.TryParse(
                    modifiedValue.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var parsed)
                ? parsed
                : (DateTimeOffset?)null;
            files.Add(new DriveFile(id, name, size, modified));
        }

        return Result<IReadOnlyList<DriveFile>>.Success(files);
    }

    private async Task<Result> UploadFileAsync(
        string localFilePath,
        string fileName,
        string folderId,
        string? existingFileId,
        CancellationToken cancellationToken)
    {
        var token = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!token.IsSuccess)
        {
            return token;
        }

        var metadata = existingFileId is null
            ? JsonSerializer.Serialize(new { name = fileName, parents = new[] { folderId } })
            : JsonSerializer.Serialize(new { name = fileName });
        var uploadUrl = existingFileId is null
            ? $"{UploadApiBaseUrl}?uploadType=resumable&fields=id,name,size,modifiedTime"
            : $"{UploadApiBaseUrl}/{Uri.EscapeDataString(existingFileId)}?uploadType=resumable&fields=id,name,size,modifiedTime";

        await using var stream = File.OpenRead(localFilePath);
        using var metadataContent = new StringContent(metadata, Encoding.UTF8, "application/json");
        using var initiate = CreateAuthorizedRequest(
            existingFileId is null ? HttpMethod.Post : HttpMethod.Patch,
            uploadUrl,
            metadataContent);
        initiate.Headers.Add("X-Upload-Content-Type", "application/octet-stream");
        initiate.Headers.Add("X-Upload-Content-Length", stream.Length.ToString(CultureInfo.InvariantCulture));
        using var initiated = await _client.SendAsync(initiate, cancellationToken).ConfigureAwait(false);
        if (!initiated.IsSuccessStatusCode)
        {
            return await MapAsync(initiated, "начать загрузку в Google Drive", cancellationToken).ConfigureAwait(false);
        }

        var location = initiated.Headers.Location;
        if (location is null)
        {
            return Result.FromError(ErrorCode.CloudOperationFailed, "Google Drive не выдал адрес загрузки.");
        }

        using var uploadContent = new StreamContent(stream);
        uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var upload = new HttpRequestMessage(HttpMethod.Put, location) { Content = uploadContent };
        using var uploaded = await _client.SendAsync(upload, cancellationToken).ConfigureAwait(false);
        return uploaded.IsSuccessStatusCode
            ? Result.Success
            : await MapAsync(uploaded, "загрузить проект в Google Drive", cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return Result.Success;
        }

        if (string.IsNullOrWhiteSpace(_refreshToken))
        {
            return Result.FromError(ErrorCode.CloudAuthFailed, "Сначала подключите Google Drive.");
        }

        return await RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.UserAgent.ParseAdd("ScriSelfStudio/1.0");
        return request;
    }

    private void SetAccessToken(string accessToken, int expiresIn)
    {
        _accessToken = accessToken;
        _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn));
    }

    private static async Task<Result> MapAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return Result.FromError(
                ErrorCode.CloudAuthFailed,
                "Google Drive отклонил доступ. Войдите снова.",
                technicalDetails: $"HTTP {(int)response.StatusCode}: {body}");
        }

        if (body.Contains("storageQuotaExceeded", StringComparison.OrdinalIgnoreCase))
        {
            return Result.FromError(ErrorCode.CloudOperationFailed, "В Google Drive закончилось свободное место.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return Result.FromError(
                ErrorCode.CloudOperationFailed,
                "Google Drive временно ограничил запросы. Повторите через минуту.");
        }

        return Result.FromError(
            ErrorCode.CloudOperationFailed,
            $"Не удалось {operation}.",
            technicalDetails: $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    private static async Task WriteBrowserResponseAsync(
        HttpListenerResponse response,
        bool success,
        CancellationToken cancellationToken)
    {
        var html = success
            ? "<!doctype html><html lang=\"ru\"><meta charset=\"utf-8\"><title>ScriSelf Studio</title><body style=\"font-family:Segoe UI,Arial,sans-serif;padding:40px;text-align:center\"><h2>Готово</h2><p>Можно вернуться в ScriSelf Studio и закрыть эту вкладку.</p></body></html>"
            : "<!doctype html><html lang=\"ru\"><meta charset=\"utf-8\"><title>ScriSelf Studio</title><body style=\"font-family:Segoe UI,Arial,sans-serif;padding:40px;text-align:center\"><h2>Вход отменён</h2><p>Вернитесь в ScriSelf Studio и попробуйте снова.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.Close();
    }

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string CreateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string CreateRandomUrlSafeToken(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string EscapeQueryValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);

    private static GoogleOAuthClientSettings LoadOAuthClient()
    {
        var assembly = typeof(GoogleDriveService).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("google-oauth-client.json", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new InvalidOperationException("В сборку не встроен google-oauth-client.json.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Не удалось открыть google-oauth-client.json.");
        using var document = JsonDocument.Parse(stream);
        var installed = document.RootElement.GetProperty("installed");
        return new GoogleOAuthClientSettings(
            installed.GetProperty("client_id").GetString() ?? string.Empty,
            installed.GetProperty("client_secret").GetString() ?? string.Empty,
            installed.GetProperty("auth_uri").GetString() ?? "https://accounts.google.com/o/oauth2/v2/auth",
            installed.GetProperty("token_uri").GetString() ?? "https://oauth2.googleapis.com/token");
    }

    private sealed record OAuthTokens(string AccessToken, string? RefreshToken, int ExpiresIn);

    private sealed record DriveFolders(string AppFolderId, string ProjectsFolderId);

    private sealed record DriveFile(string Id, string Name, long SizeBytes, DateTimeOffset? ModifiedAt);
}
