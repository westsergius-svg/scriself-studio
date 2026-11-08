namespace ScriSelfStudio.Persistence.Language;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Errors;

/// <summary>Owns one local server. Never uses proxies, redirects, public binding, or a cloud endpoint.</summary>
public sealed class LanguageToolService : IGrammarService, IDisposable
{
    private readonly string _root;
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _state = new();
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 2 * 1024 * 1024 };
    private CancellationTokenSource _stop = new();
    private Process? _process;
    private TaskCompletionSource? _ready;
    private Uri? _endpoint;
    private bool _disposed;

    public LanguageToolService(string? runtimeDirectory = null, TimeSpan? startupTimeout = null, TimeSpan? requestTimeout = null)
    {
        _root = Path.GetFullPath(runtimeDirectory ?? Path.Combine(AppContext.BaseDirectory, "Grammar"));
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(30);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
        if (_startupTimeout <= TimeSpan.Zero || _requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(startupTimeout));
    }

    public async Task<Result<IReadOnlyList<GrammarIssue>>> CheckAsync(string text, string language, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (language is not ("auto" or "ru" or "en-US"))
            return Result<IReadOnlyList<GrammarIssue>>.FromError(ErrorCode.ValidationFailed, "Выберите русский или английский язык проверки.");
        if (string.IsNullOrWhiteSpace(text)) return Result<IReadOnlyList<GrammarIssue>>.Success([]);
        CancellationTokenSource request;
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        }
        using (request)
        {
            await _gate.WaitAsync(request.Token).ConfigureAwait(false);
            try
            {
                await EnsureStartedAsync(request.Token).ConfigureAwait(false);
                var results = new List<GrammarIssue>();
                var selected = language == "auto" ? GrammarResponse.DetectLanguage(text) : language;
                foreach (var chunk in GrammarResponse.Chunks(text))
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
                    timeout.CancelAfter(_requestTimeout);
                    using var body = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["text"] = chunk.Text, ["language"] = selected,
                        ["disabledCategories"] = "TYPOS,CASING,STYLE,REDUNDANCY",
                    });
                    using var response = await _http.PostAsync(_endpoint, body, timeout.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                    results.AddRange(GrammarResponse.Parse(json, chunk.Text).Select(i => i with { Start = i.Start + chunk.Start }));
                }
                request.Token.ThrowIfCancellationRequested();
                return Result<IReadOnlyList<GrammarIssue>>.Success(results);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException or HttpRequestException or JsonException or TimeoutException
                or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception
                or KeyNotFoundException or FormatException)
            {
                lock (_state) StopProcess();
                return Result<IReadOnlyList<GrammarIssue>>.FromError(ErrorCode.EditorOperationFailed,
                    error is FileNotFoundException ? "Локальная грамматика недоступна в этой сборке: отсутствует модуль LanguageTool."
                    : "LanguageTool не ответил. Выключите и снова включите проверку, чтобы повторить.",
                    technicalDetails: error.GetType().Name);
            }
            finally { _gate.Release(); }
        }
    }

    private async Task EnsureStartedAsync(CancellationToken token)
    {
        await Task.Run(() =>
        {
            lock (_state)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                token.ThrowIfCancellationRequested();
                if (_process is { HasExited: false }) return;
                StopProcess();
                var java = Path.Combine(_root, "jdk-21.0.13+9-jre", "bin", "java.exe");
                var jar = Path.Combine(_root, "LanguageTool-6.6", "languagetool-server.jar");
                var config = Path.Combine(_root, "server.properties");
                if (!OperatingSystem.IsWindows() || !File.Exists(java) || !File.Exists(jar) || !File.Exists(config))
                    throw new FileNotFoundException("Local grammar runtime is missing.");
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                _endpoint = new Uri($"http://127.0.0.1:{port}/v2/check");
                var start = new ProcessStartInfo(java)
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true,
                };
                foreach (var arg in new[] { "-Xms32m", "-Xmx512m", "-Djava.net.preferIPv4Stack=true", "-cp", jar,
                    "org.languagetool.server.HTTPServer", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--config", config })
                    start.ArgumentList.Add(arg);
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _ready = ready;
                _process = new Process { StartInfo = start, EnableRaisingEvents = true };
                // Drain logs without persisting text or keeping an unbounded buffer.
                _process.OutputDataReceived += (_, e) => { if (e.Data?.EndsWith("Server started", StringComparison.Ordinal) == true) ready.TrySetResult(); };
                _process.ErrorDataReceived += (_, _) => { };
                _process.Exited += (_, _) => ready.TrySetException(new InvalidOperationException("LanguageTool stopped."));
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
        }, token).ConfigureAwait(false);
        Task readyTask;
        lock (_state) readyTask = _ready?.Task ?? throw new ObjectDisposedException(nameof(LanguageToolService));
        await readyTask.WaitAsync(_startupTimeout, token).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_state) { if (_disposed) return; _stop.Cancel(); }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_state)
            {
                StopProcess();
                if (!_disposed) { _stop.Dispose(); _stop = new(); }
            }
        }
        finally { _gate.Release(); }
    }

    private void StopProcess()
    {
        _ready?.TrySetCanceled();
        _ready = null;
        if (_process is not { } process) return;
        _process = null;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }

    public void Dispose()
    {
        lock (_state)
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            StopProcess();
            _http.Dispose();
        }
    }
}
