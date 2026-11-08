namespace ScriSelfStudio.FirstUsersQa;

using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Cloud;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Core.Updates;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation.Dialogs;
using ScriSelfStudio.TestSupport;

/// <summary>Local test driver. It renders production AXAML and sends headless input events.</summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [STAThread]
    public static void Main(string[] args)
    {
        var root = Path.GetFullPath(args.Length > 0 ? args[0] : "qa/first-users");
        var port = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 38461;
        AppBuilder.Configure<App.App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var harness = new Harness(root);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        Console.WriteLine($"FirstUsersQa ready: http://127.0.0.1:{port}/; evidence: {root}");
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync();
                try
                {
                    var request = await JsonSerializer.DeserializeAsync<DriverRequest>(context.Request.InputStream, JsonOptions)
                        ?? new DriverRequest();
                    var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Dispatcher.UIThread.Post(async () =>
                    {
                        try { completion.SetResult(await harness.ExecuteAsync(request)); }
                        catch (Exception exception) { completion.SetException(exception); }
                    });
                    var response = await completion.Task;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await JsonSerializer.SerializeAsync(context.Response.OutputStream, response, JsonOptions);
                }
                catch (Exception exception)
                {
                    context.Response.StatusCode = 500;
                    await JsonSerializer.SerializeAsync(context.Response.OutputStream,
                        new { error = exception.Message, type = exception.GetType().Name }, JsonOptions);
                }
                finally { context.Response.Close(); }
            }
        });
        Dispatcher.UIThread.MainLoop(CancellationToken.None);
    }
}

internal sealed class DriverRequest
{
    public string Op { get; set; } = "observe";
    public string Session { get; set; } = "probe";
    public double Width { get; set; } = 1440;
    public double Height { get; set; } = 768;
    public double X { get; set; }
    public double Y { get; set; }
    public double Delta { get; set; } = -3;
    public string? Target { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Key { get; set; } = "Enter";
    public string Modifiers { get; set; } = "None";
    public string Button { get; set; } = "Left";
    public int Count { get; set; } = 1;
    public int WaitMs { get; set; } = 250;
    public bool Cancel { get; set; }
    public string? FileName { get; set; }
    public string? Note { get; set; }
}

internal sealed class Harness
{
    private readonly string _root;
    private readonly List<Window> _windows = [];
    private ServiceProvider? _provider;
    private MainWindow? _main;
    private QaDialogs? _dialogs;
    private string _sessionDirectory = string.Empty;
    private readonly Dictionary<string, Control> _targets = [];
    private readonly List<string> _inputTrace = [];
    private bool _traceInput;
    private Window? _pointerWindow;
    private int _step;
    private int _clicks;
    private double _width = 1440;
    private double _height = 768;

    public Harness(string root)
    {
        _root = root;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            _windows.Add(window);
            window.AddHandler(InputElement.PointerMovedEvent, (_, args) => TraceInput("move", args), RoutingStrategies.Bubble, true);
            window.AddHandler(InputElement.PointerPressedEvent, (_, args) => TraceInput("down", args), RoutingStrategies.Bubble, true);
            window.AddHandler(InputElement.PointerReleasedEvent, (_, args) => TraceInput("up", args), RoutingStrategies.Bubble, true);
        });
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => _windows.Remove(window));
    }

    public async Task<object> ExecuteAsync(DriverRequest request)
    {
        _inputTrace.Clear();
        _traceInput = request.Note == "pointer-debug";
        string? excludedAction = null;
        switch (request.Op.ToLowerInvariant())
        {
            case "reset":
                if (string.IsNullOrWhiteSpace(request.Session)
                    || request.Session.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                    throw new ArgumentException("Session names may contain only ASCII letters, digits, '-' and '_'.");
                var nextSessionDirectory = Path.Combine(_root, request.Session);
                if (Directory.Exists(nextSessionDirectory) || File.Exists(nextSessionDirectory))
                    throw new InvalidOperationException("Session already exists. Use a new session name to preserve its evidence.");
                await CloseAsync();
                new ScriSelfStudio.Presentation.Localization.CultureService().Apply("ru-RU");
                _sessionDirectory = nextSessionDirectory;
                Directory.CreateDirectory(_sessionDirectory);
                _step = 0;
                _clicks = 0;
                _width = request.Width;
                _height = request.Height;
                await StartAsync();
                break;
            case "restart":
                await CloseAsync();
                await StartAsync();
                break;
            case "observe":
                break;
            case "closewindow":
                if (ReferenceEquals(Active, Main)) throw new InvalidOperationException("Use restart to close and reopen the main window.");
                Active.Close();
                break;
            case "resize":
                _width = request.Width;
                _height = request.Height;
                Main.Width = _width;
                Main.Height = _height;
                break;
            case "picker":
                // Native file selection is explicitly outside this test. Queue its result only.
                Dialogs.CancelNextPicker = request.Cancel;
                Dialogs.NextFileName = request.FileName;
                break;
            case "click":
            case "doubleclick":
            case "move":
            case "scroll":
            {
                var window = Active;
                var point = new Point(request.X, request.Y);
                if (request.Target is not null)
                {
                    if (!_targets.TryGetValue(request.Target, out var control))
                        throw new ArgumentException("Target is stale. Observe the active window again.");
                    point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
                        ?? throw new InvalidOperationException("Target does not belong to the active window.");
                }
                var hit = window.InputHitTest(point) as Visual;
                var button = hit?.GetVisualAncestors().OfType<Button>().FirstOrDefault()
                    ?? hit as Button;
                if (_traceInput)
                    _inputTrace.Add($"hit={hit?.GetType().Name}; button={button?.Name}:{(button is null ? null : Label(button))}; active={window.IsActive}; enabled={button?.IsEffectivelyEnabled}");
                if (button is not null && _provider is not null)
                {
                    var launcher = _provider.GetRequiredService<LauncherViewModel>();
                    if (ReferenceEquals(button.Command, launcher.OpenLinkCommand)
                        || ReferenceEquals(button.Command, launcher.OpenFolderCommand)
                        || ReferenceEquals(button.Command, launcher.OpenSelectedFolderCommand)
                        || ReferenceEquals(button.Command, _provider.GetRequiredService<ShellViewModel>().OpenProjectFolderCommand))
                    {
                        excludedAction = "External browser / Explorer action excluded from headless QA.";
                        break;
                    }
                }
                var modifiers = Enum.Parse<RawInputModifiers>(request.Modifiers, true);
                // A real pointer leaves the owner before entering a dialog. Without this,
                // a pending owner tooltip can retain an overlay and consume the next click.
                if (_pointerWindow is { IsVisible: true } previous && !ReferenceEquals(previous, window))
                    previous.MouseMove(new Point(-1, -1), modifiers);
                _pointerWindow = window;
                window.MouseMove(new Point(-1, -1), modifiers);
                window.MouseMove(point, modifiers);
                if (request.Op == "move") break;
                if (request.Op == "scroll")
                {
                    window.MouseWheel(point, new Vector(0, request.Delta), modifiers);
                    break;
                }
                var mouseButton = Enum.Parse<MouseButton>(request.Button, true);
                var count = request.Op == "doubleclick" ? 2 : Math.Clamp(request.Count, 1, 3);
                for (var i = 0; i < count; i++)
                {
                    if (!window.IsVisible)
                    {
                        excludedAction = "The first click closed the target window; no second click can be delivered to it.";
                        break;
                    }
                    window.MouseDown(point, mouseButton, modifiers);
                    window.MouseUp(point, mouseButton, modifiers);
                    _clicks++;
                }
                break;
            }
            case "type":
                Active.KeyTextInput(request.Text);
                break;
            case "key":
            {
                var key = Enum.Parse<Key>(request.Key, true);
                var modifiers = Enum.Parse<RawInputModifiers>(request.Modifiers, true);
                Enum.TryParse<PhysicalKey>(request.Key, true, out var physicalKey);
                Active.KeyPress(key, modifiers, physicalKey, null);
                Active.KeyRelease(key, modifiers, physicalKey, null);
                break;
            }
            default:
                throw new ArgumentException("Unknown operation.");
        }

        await Task.Delay(Math.Clamp(request.WaitMs, 50, 2000));
        _step++;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var active = Active;
        var imagePath = Path.Combine(_sessionDirectory, $"{_step:D3}-{request.Op}.png");
        using (var frame = active.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("No rendered frame."))
            frame.Save(imagePath, PngBitmapEncoderOptions.Default);
        string? ownerImage = null;
        if (!ReferenceEquals(active, Main))
        {
            ownerImage = Path.Combine(_sessionDirectory, $"{_step:D3}-{request.Op}-owner.png");
            using var frame = Main.CaptureRenderedFrame();
            frame?.Save(ownerImage, PngBitmapEncoderOptions.Default);
        }

        _targets.Clear();
        var visible = active.GetVisualDescendants().OfType<Control>()
            .Where(control => IsVisible(control, active)).ToArray();
        var items = new List<object>();
        foreach (var control in visible.Where(IsActionable))
        {
            var id = $"c{items.Count + 1}";
            _targets.Add(id, control);
            var point = control.TranslatePoint(new Point(0, 0), active)!.Value;
            items.Add(new
            {
                id,
                type = control.GetType().Name,
                label = Label(control),
                enabled = control.IsEffectivelyEnabled,
                x = Math.Round(point.X, 1), y = Math.Round(point.Y, 1),
                width = Math.Round(control.Bounds.Width, 1), height = Math.Round(control.Bounds.Height, 1),
                tooltip = ToolTip.GetTip(control) as string,
            });
        }
        var texts = visible.OfType<TextBlock>().Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text)).Distinct().ToArray();
        var observation = new
        {
            step = _step, clicks = _clicks, session = Path.GetFileName(_sessionDirectory),
            title = active.Title, modal = Dialogs.Modals.Contains(active), secondary = !ReferenceEquals(active, Main),
            width = active.Bounds.Width, height = active.Bounds.Height,
            screenshot = imagePath, ownerScreenshot = ownerImage,
            texts, controls = items,
            focused = active.FocusManager?.GetFocusedElement()?.GetType().Name,
            pickerEvents = Dialogs.Events.ToArray(),
            excludedAction,
            inputTrace = _inputTrace.ToArray(),
            limitations = "Avalonia headless/Skia; native file dialogs simulated; external browser/auth/network not tested; clicks count only in-app pointer clicks.",
        };
        var json = JsonSerializer.Serialize(new { request, observation }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        File.AppendAllText(Path.Combine(_sessionDirectory, "actions.jsonl"), json + Environment.NewLine, Encoding.UTF8);
        return observation;
    }

    private void TraceInput(string kind, PointerEventArgs args)
    {
        if (!_traceInput) return;
        var source = args.Source as Control;
        _inputTrace.Add($"{kind}: source={source?.GetType().Name}:{(source is null ? null : Label(source))}; captured={args.Pointer.Captured?.GetType().Name}; handled={args.Handled}; over={source?.IsPointerOver}");
    }

    private MainWindow Main => _main ?? throw new InvalidOperationException("Send reset first.");
    private QaDialogs Dialogs => _dialogs ?? throw new InvalidOperationException("Send reset first.");
    private Window Active => _windows.LastOrDefault(window => window.IsVisible) ?? Main;

    private async Task StartAsync()
    {
        var files = Path.Combine(_sessionDirectory, "files");
        Directory.CreateDirectory(files);
        _dialogs = new QaDialogs(() => Main, files);
        var services = new ServiceCollection();
        services.AddScriSelfStudio(Path.Combine(_sessionDirectory, "settings"), useAvaloniaDialogs: false);
        services.AddSingleton<IFileDialogService>(_dialogs);
        services.AddSingleton<IUpdateService>(new FakeUpdateService { CurrentVersion = typeof(MainWindow).Assembly.GetName().Version!.ToString(3) });
        services.AddSingleton<ICloudStorageCatalog>(new OfflineCloudCatalog());
        _provider = services.BuildServiceProvider();
        ScriSelfStudioServices.RegisterModules(_provider);
        _main = _provider.GetRequiredService<MainWindow>();
        _main.Width = _width;
        _main.Height = _height;
        _main.Show();
        await _provider.GetRequiredService<LauncherViewModel>().InitializeAsync();
    }

    private async Task CloseAsync()
    {
        if (_provider is null) return;
        foreach (var modal in Dialogs.Modals.ToArray()) modal.Close();
        await _provider.GetRequiredService<ShellViewModel>().FlushPendingEditsAsync();
        foreach (var window in _windows.ToArray().Reverse()) window.Close();
        _provider.Dispose();
        _provider = null;
        _main = null;
        _pointerWindow = null;
    }

    private static bool IsVisible(Control control, Window window)
    {
        if (!control.IsVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0
            || control.GetVisualAncestors().Any(ancestor => !ancestor.IsVisible)) return false;
        var point = control.TranslatePoint(new Point(0, 0), window);
        return point is { } p && new Rect(p, control.Bounds.Size).Intersects(new Rect(window.Bounds.Size));
    }

    private static bool IsActionable(Control control) =>
        control is Button or TextBox or ComboBox or MenuItem or TabItem or TreeViewItem
        or Slider or NumericUpDown or ScrollBar
        || control.GetType().Name.Contains("RichText", StringComparison.Ordinal);

    private static string Label(Control control) =>
        AutomationProperties.GetName(control)
        ?? (control as TextBox)?.PlaceholderText
        ?? (control as ContentControl)?.Content as string
        ?? string.Join(" · ", control.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsVisible).Select(text => text.Text).Where(text => !string.IsNullOrWhiteSpace(text)).Distinct());
}

internal sealed class QaDialogs(Func<Window> owner, string files) : IFileDialogService
{
    public List<Window> Modals { get; } = [];
    public List<string> Events { get; } = [];
    public bool CancelNextPicker { get; set; }
    public string? NextFileName { get; set; }
    private string? _lastProjectPath;

    private Task<string?> Pick(string kind, string suggestedFileName, bool opening = false)
    {
        if (CancelNextPicker)
        {
            CancelNextPicker = false;
            Events.Add($"{kind}: cancelled (native picker simulated)");
            return Task.FromResult<string?>(null);
        }
        var explicitlyNamed = NextFileName is not null;
        var name = NextFileName ?? suggestedFileName;
        NextFileName = null;
        if (name != Path.GetFileName(name)) throw new ArgumentException("File name must stay in the session files folder.");
        var path = opening && !explicitlyNamed && _lastProjectPath is not null ? _lastProjectPath : Path.Combine(files, name);
        _lastProjectPath = path;
        Events.Add($"{kind}: {path} (native picker simulated)");
        return Task.FromResult<string?>(path);
    }

    public Task<string?> PickProjectToOpenAsync(string? suggestedDirectory, CancellationToken cancellationToken = default) =>
        Pick("open", "Новый проект.scri", true);
    public Task<string?> PickProjectToCreateAsync(string suggestedFileName, string? suggestedDirectory, CancellationToken cancellationToken = default) =>
        Pick("create", suggestedFileName);
    public Task<string?> PickExportFileToSaveAsync(string suggestedFileName, string extension, string fileTypeName, string? suggestedDirectory, CancellationToken cancellationToken = default) =>
        Pick("export", suggestedFileName);
    public Task<string?> PickCloudDownloadPathAsync(string suggestedFileName, string? suggestedDirectory, CancellationToken cancellationToken = default) =>
        Pick("cloud download", suggestedFileName);

    public Task<string?> PromptAsync(string title, string message, string initialValue, CancellationToken cancellationToken = default) =>
        PromptAsync(title, message, initialValue, prefillInitialValue: false, cancellationToken);

    public async Task<string?> PromptAsync(
        string title, string message, string initialValue, bool prefillInitialValue,
        CancellationToken cancellationToken = default)
    {
        string? result = null;
        var dialog = new PromptDialog
        {
            DataContext = new PromptDialogViewModel(title, message, initialValue, value => result = value, prefillInitialValue),
        };
        await ShowAsync(dialog);
        return result;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, CancellationToken cancellationToken = default)
    {
        var result = false;
        var dialog = new MessageDialog
        {
            DataContext = new MessageDialogViewModel(title, message, confirmText, cancelText, true, value => result = value),
        };
        await ShowAsync(dialog);
        return result;
    }

    public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) =>
        ShowAsync(new MessageDialog
        {
            DataContext = new MessageDialogViewModel(title, message, "Понятно", string.Empty, false, _ => { }),
        });

    private async Task ShowAsync(Window dialog)
    {
        Modals.Add(dialog);
        try { await dialog.ShowDialog(owner()); }
        finally
        {
            Modals.Remove(dialog);
            owner().Activate();
        }
    }
}

internal sealed class OfflineCloudCatalog : ICloudStorageCatalog
{
    public IReadOnlyList<CloudProviderInfo> Providers { get; } =
        [new(CloudProviderKind.YandexDisk, "Яндекс.Диск", false), new(CloudProviderKind.GoogleDrive, "Google Drive", true)];
    public ICloudStorageService Get(CloudProviderKind kind) => new OfflineCloud(kind);

    private sealed class OfflineCloud(CloudProviderKind kind) : ICloudStorageService
    {
        private const string Excluded = "Сетевой вход исключён из локального QA-стенда. Это ограничение теста, а не ошибка продукта.";
        public CloudProviderKind Provider => kind;
        public bool RequiresInteractiveSignIn => kind == CloudProviderKind.GoogleDrive;
        public bool IsConfigured => false;
        public string? AccountLogin => null;
        public string? SessionSecret => null;
        public Task<Result> ConnectAsync(string? login, string? password, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.FromError(ErrorCode.UnsupportedOperation, Excluded));
        public Task<Result> ConnectInteractiveAsync(CancellationToken cancellationToken = default) => ConnectAsync(null, null, cancellationToken);
        public Task<Result> RestoreAsync(string? login, string? secret, CancellationToken cancellationToken = default) => ConnectAsync(null, null, cancellationToken);
        public void Disconnect() { }
        public Task<Result<IReadOnlyList<CloudFileInfo>>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<CloudFileInfo>>.FromError(ErrorCode.UnsupportedOperation, Excluded));
        public Task<Result> UploadAsync(string localFilePath, CancellationToken cancellationToken = default) => ConnectAsync(null, null, cancellationToken);
        public Task<Result> DownloadAsync(CloudFileInfo file, string localFilePath, CancellationToken cancellationToken = default) => ConnectAsync(null, null, cancellationToken);
    }
}
