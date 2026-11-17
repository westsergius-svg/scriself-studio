namespace ScriSelfStudio.Launcher;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Cloud;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Events;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Security;
using ScriSelfStudio.Core.Updates;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Desktop;
using ScriSelfStudio.Presentation.Localization;
using ScriSelfStudio.Presentation.Theme;

/// <summary>
/// Стартовый экран: создание, открытие, последние проекты и состояние текущего
/// проекта (ТЗ п. 27, 28, 31).
/// </summary>
public sealed partial class LauncherViewModel : ViewModelBase, IDisposable
{
    /// <summary>Страница благодарности автора — цель QR-квадрата в карточке проекта.</summary>
    public const string ThanksUrl = "https://scriborium.ru/spasibo.html";

    /// <summary>Блог автора: раздел «Блог» открывает сайт, экрана блога в приложении нет.</summary>
    public const string BlogUrl = "https://scriborium.ru/blog";

    /// <summary>Страница создания паролей приложений Яндекса.</summary>
    public const string YandexAppPasswordsUrl = "https://id.yandex.ru/security/app-passwords";

    private readonly IProjectService _projects;
    private readonly IAppSettingsService _settings;
    private readonly IFileDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly ICultureService _culture;
    private readonly IEditorEngine _editor;
    private readonly IUpdateService _updates;
    private readonly IWorkspaceSaveService _workspace;
    private readonly ICloudStorageCatalog _cloudCatalog;
    private ICloudStorageService _cloud;
    private readonly List<IDisposable> _subscriptions = [];
    private bool _suppressCloudProviderRestore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectOpen))]
    [NotifyPropertyChangedFor(nameof(CurrentProjectTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentProjectPath))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private ProjectDto? _currentProject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _isDirty;

    public LauncherViewModel(
        IProjectService projects,
        IAppSettingsService settings,
        IFileDialogService dialogs,
        IThemeService theme,
        INavigationService navigation,
        IEventBus events,
        IEditorEngine editor,
        IUpdateService updates,
        ICloudStorageCatalog cloudCatalog,
        ICultureService culture,
        IWorkspaceSaveService workspace)
    {
        _projects = projects;
        _settings = settings;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;
        _culture = culture;
        _editor = editor;
        _updates = updates;
        _workspace = workspace;
        _cloudCatalog = cloudCatalog;
        _cloud = cloudCatalog.Get(CloudProviderKind.YandexDisk);
        _suppressCloudProviderRestore = true;
        SelectedCloudProvider = CloudProviders[0];
        _suppressCloudProviderRestore = false;

        // Экран сам следит за состоянием проекта: карточка открытого проекта и
        // панель «Контекст проекта» должны обновляться, даже если лаунчер
        // инициализирован без оболочки (например, в тестах или при открытии
        // проекта из командной строки).
        _subscriptions.Add(events.Subscribe<ProjectOpened>(_ => SyncProjectState()));
        _subscriptions.Add(events.Subscribe<ProjectClosed>(_ => SyncProjectState()));
        _subscriptions.Add(events.Subscribe<ProjectChanged>(_ => SyncProjectState()));
    }

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    /// <summary>Отфильтрованный список центральной панели: подстрока поиска.</summary>
    public ObservableCollection<RecentProjectItem> VisibleProjects { get; } = [];

    /// <summary>Проекты в папке ScriSelfStudio/Projects на Яндекс.Диске.</summary>
    public ObservableCollection<CloudFileItem> CloudFiles { get; } = [];

    /// <summary>Открыта ли панель «Облако» поверх лаунчера.</summary>
    [ObservableProperty]
    private bool _isCloudOpen;

    /// <summary>Логин Яндекса для подключения к Диску.</summary>
    [ObservableProperty]
    private string? _cloudLogin;

    /// <summary>Пароль приложения Яндекса. В настройках хранится только зашифрованным.</summary>
    [ObservableProperty]
    private string? _cloudPassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowYandexConnectForm))]
    [NotifyPropertyChangedFor(nameof(ShowGoogleConnectForm))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCloudFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UploadToCloudCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCloudFileCommand))]
    private bool _isCloudConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCloudCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCloudFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UploadToCloudCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCloudFileCommand))]
    private bool _isCloudBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudStatus))]
    private string? _cloudStatusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudError))]
    private string? _cloudErrorMessage;

    public bool HasCloudStatus => !string.IsNullOrEmpty(CloudStatusMessage);

    public bool HasCloudError => !string.IsNullOrEmpty(CloudErrorMessage);

    public bool HasCloudFiles => CloudFiles.Count > 0;

    /// <summary>Провайдеры облака, между которыми переключается панель.</summary>
    public IReadOnlyList<CloudProviderInfo> CloudProviders => _cloudCatalog.Providers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYandexCloud))]
    [NotifyPropertyChangedFor(nameof(IsGoogleCloud))]
    [NotifyPropertyChangedFor(nameof(ShowYandexConnectForm))]
    [NotifyPropertyChangedFor(nameof(ShowGoogleConnectForm))]
    [NotifyPropertyChangedFor(nameof(CloudProviderTitle))]
    [NotifyPropertyChangedFor(nameof(CloudProviderDescription))]
    private CloudProviderInfo? _selectedCloudProvider;

    public bool IsYandexCloud => SelectedCloudProvider?.Kind == CloudProviderKind.YandexDisk;

    public bool IsGoogleCloud => SelectedCloudProvider?.Kind == CloudProviderKind.GoogleDrive;

    public bool ShowYandexConnectForm => IsYandexCloud && !IsCloudConnected;

    public bool ShowGoogleConnectForm => IsGoogleCloud && !IsCloudConnected;

    public string CloudProviderTitle => IsGoogleCloud
        ? Loc.T("Облако · Google Drive")
        : Loc.T("Облако · Яндекс.Диск");

    public string CloudProviderDescription => IsGoogleCloud
        ? Loc.T("Резервная копия проектов в вашем Google Drive. Приложение работает только с папкой ScriSelfStudio/Projects.")
        : Loc.T("Резервная копия проектов в вашем Диске. Приложение работает только с папкой ScriSelfStudio/Projects.");

    /// <summary>Выбранная карточка: на неё смотрит правая панель «Контекст проекта».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProject))]
    private RecentProjectItem? _selectedProject;

    /// <summary>Строка поиска по названию и пути проекта.</summary>
    [ObservableProperty]
    private string? _projectSearchQuery;

    /// <summary>Сколько проектов в списке (значок раздела «Мои проекты»).</summary>
    public int ProjectCount => RecentProjects.Count;

    public bool HasProjects => RecentProjects.Count > 0;

    public bool HasSelectedProject => SelectedProject is not null;

    public bool HasVisibleProjects => VisibleProjects.Count > 0;

    partial void OnProjectSearchQueryChanged(string? value) => RefreshVisibleProjects();

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool IsProjectOpen => CurrentProject is not null;

    public bool HasUnsavedChanges => IsProjectOpen && IsDirty;

    public string CurrentProjectTitle => CurrentProject?.Name ?? Loc.T("Проект не открыт");

    public string CurrentProjectPath => CurrentProject?.FilePath ?? string.Empty;

    /// <summary>
    /// Пояснение для проекта в старом формате: файл остаётся в нём, а предыдущая
    /// версия сохраняется в резервную копию (ТЗ п. 5, 13).
    /// </summary>
    public string? FormatNotice => _projects.CurrentSourceFormat is null
        ? null
        : Loc.T("Проект открыт в старом формате (ZIP: manifest.json + content.json). Сохранение запишет файл в этом же формате, предыдущая версия останется в .bak.");

    public bool HasFormatNotice => FormatNotice is not null;

    public bool HasRecentProjects => RecentProjects.Count > 0;

    /// <summary>Структура открывается только при открытом проекте: кнопка и команда совпадают.</summary>
    public bool CanOpenArchitectureScreen => IsProjectOpen && _navigation.CanNavigateTo(AppScreen.Architecture);

    public bool CanOpenEditorScreen => _navigation.CanNavigateTo(AppScreen.Editor);

    public bool CanOpenSettingsScreen => _navigation.CanNavigateTo(AppScreen.Settings);

    /// <summary>Экспорт книги доступен из лаунчера, даже если проект ещё не открыт.</summary>
    public bool CanOpenExportScreen => _navigation.CanNavigateTo(AppScreen.Export);

    /// <summary>Персонажи и линии относятся к проекту: без него экран пуст (ТЗ п. 27).</summary>
    public bool CanOpenCharactersScreen => IsProjectOpen && _navigation.CanNavigateTo(AppScreen.Characters);

    public bool CanOpenTimelineScreen => IsProjectOpen && _navigation.CanNavigateTo(AppScreen.Timeline);

    [RelayCommand(CanExecute = nameof(CanOpenArchitectureScreen))]
    private void OpenArchitecture() => _navigation.NavigateTo(AppScreen.Architecture);

    /// <summary>Переход на экран персонажей (ТЗ п. 27).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenCharactersScreen))]
    private void OpenCharacters() => _navigation.NavigateTo(AppScreen.Characters);

    /// <summary>Переход на экран сюжетных линий (ТЗ п. 27).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenTimelineScreen))]
    private void OpenTimeline() => _navigation.NavigateTo(AppScreen.Timeline);

    /// <summary>Переход на экран настроек (ТЗ п. 27).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenSettingsScreen))]
    private void OpenSettings() => _navigation.NavigateTo(AppScreen.Settings);

    [RelayCommand(CanExecute = nameof(CanOpenExportScreen))]
    private void OpenExport() => _navigation.NavigateTo(AppScreen.Export);

    /// <summary>Раздел «Мои проекты»: обновляет список из настроек.</summary>
    [RelayCommand]
    private void ShowProjects() => RefreshRecentProjects();

    /// <summary>Версия приложения в подвале лаунчера.</summary>
    public string AppVersionText => Loc.F("Версия {0}", _updates.CurrentVersion);

    /// <summary>Идёт проверка или загрузка обновления: кнопка блокируется.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    private bool _isCheckingForUpdates;

    private bool CanCheckForUpdates() => !IsCheckingForUpdates;

    /// <summary>
    /// Проверяет обновления на официальном канале Scriborium: тот же адрес и тот
    /// же формат, что использовал Scriborium 1.1.3.3S (<c>latest.json</c> в каталоге
    /// <c>uploads/scrisoft/</c>). Найденный установщик кладётся во временную папку.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        ClearMessages();
        IsCheckingForUpdates = true;
        StatusMessage = Loc.T("Проверка обновлений…");

        try
        {
            var check = await _updates.CheckAsync(cancellationToken);
            if (check.HasError)
            {
                await ReportUpdateErrorAsync(Loc.F("Не удалось проверить обновления:\n{0}", check.Error));
                return;
            }

            if (check.Latest is not { } latest)
            {
                StatusMessage = Loc.F("Установлена версия {0}. Сборок в канале нет.", _updates.CurrentVersion);
                return;
            }

            if (!_updates.IsNewer(latest))
            {
                StatusMessage = Loc.F("Установлена версия {0}. Обновлений нет.", _updates.CurrentVersion);
                return;
            }

            await OfferUpdateAsync(latest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = null;
        }
        catch (Exception exception)
        {
            await ReportUpdateErrorAsync(Loc.F("Не удалось обновить приложение:\n{0}", exception.Message));
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>
    /// Тихая проверка при запуске: если в канале есть новая сборка, предлагаем её
    /// скачать и установить. Ошибки сети и пустой канал не показываются, чтобы
    /// запуск приложения не зависел от доступности сайта.
    /// </summary>
    public async Task CheckForUpdatesOnStartupAsync(CancellationToken cancellationToken = default)
    {
        if (IsCheckingForUpdates)
        {
            return;
        }

        IsCheckingForUpdates = true;
        try
        {
            var check = await _updates.CheckAsync(cancellationToken);
            if (check.HasError || check.Latest is not { } latest || !_updates.IsNewer(latest))
            {
                return;
            }

            StatusMessage = Loc.F("Доступна новая версия {0}.", latest.Display);
            await OfferUpdateAsync(latest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Окно закрывается — проверка больше не нужна.
        }
        catch
        {
            // Тихая проверка не должна мешать запуску приложения.
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>Предлагает скачать и установить найденную сборку.</summary>
    private async Task OfferUpdateAsync(UpdateInfo latest, CancellationToken cancellationToken)
    {
        if (!latest.HasIntegrityInformation)
        {
            var openPage = await _dialogs.ConfirmAsync(UpdateTitle,
                Loc.F("Доступна новая версия {0}.\n\nЭтот выпуск можно скачать с официального сайта. Открыть страницу скачивания?", latest.Display),
                Loc.T("Открыть сайт"), Loc.T("Отмена"), cancellationToken);
            if (openPage)
            {
                using var browser = Process.Start(new ProcessStartInfo(ScriboriumUpdateService.DownloadPageUrl) { UseShellExecute = true });
            }
            StatusMessage = null;
            return;
        }
        var download = await _dialogs.ConfirmAsync(
            UpdateTitle,
            Loc.F("Доступна новая версия {0} (установлена: {1}).\n{2}\n\nСкачать и установить?", latest.Display, _updates.CurrentVersion, latest.FileName),
            Loc.T("Скачать"),
            Loc.T("Отмена"),
            cancellationToken);
        if (!download)
        {
            StatusMessage = null;
            return;
        }

        StatusMessage = Loc.F("Скачивание обновления {0}…", latest.Display);
        var installer = await _updates.DownloadAsync(latest, null, cancellationToken);
        StatusMessage = null;

        var install = await _dialogs.ConfirmAsync(
            UpdateTitle,
            Loc.T(OperatingSystem.IsMacOS()
                ? "Обновление скачано и проверено.\n\nПрограмма сохранит книгу и завершится. Перенесите новую версию ScriSelf Studio в папку «Программы», заменив прежнюю."
                : "Обновление скачано и проверено.\n\nУстановить сейчас? Программа сначала сохранит все правки книги."),
            Loc.T("Установить"),
            Loc.T("Позже"),
            cancellationToken);
        if (!install)
        {
            StatusMessage = Loc.F("Обновление сохранено: {0}", installer);
            return;
        }

        // Приложение закрывается: установщик должен получить файлы свободными.
        var saved = await _workspace.SaveAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            await ReportAsync(saved.Error!);
            return;
        }
        try
        {
            _updates.LaunchInstaller(installer);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException
            or UnauthorizedAccessException or Win32Exception)
        {
            await ReportUpdateErrorAsync(Loc.F(
                "Не удалось запустить обновление. Книга сохранена и остаётся открытой. Повторите попытку.\n\n{0}",
                error.Message));
        }
    }

    private static string UpdateTitle => Loc.T("Обновление ScriSelf Studio");

    private async Task ReportUpdateErrorAsync(string message)
    {
        StatusMessage = null;
        ErrorMessage = message;
        await _dialogs.ShowErrorAsync(UpdateTitle, message);
    }

    /// <summary>Выбор карточки проекта (щелчок по карточке в списке).</summary>
    [RelayCommand]
    private void SelectProject(RecentProjectItem? item) => SelectedProject = item;

    /// <summary>«Продолжить работу»: открыть выбранный проект.</summary>
    [RelayCommand]
    private async Task OpenSelectedProjectAsync()
    {
        if (SelectedProject is { } item)
        {
            await OpenRecentAsync(item);
        }
    }

    /// <summary>Показать файл выбранного проекта в проводнике.</summary>
    [RelayCommand]
    private void OpenSelectedFolder()
    {
        if (SelectedProject is { } item)
        {
            DesktopFileManager.RevealFile(item.FilePath);
        }
    }

    /// <summary>Показать файл проекта в проводнике (меню карточки).</summary>
    [RelayCommand]
    private void OpenFolder(RecentProjectItem? item)
    {
        if (item is not null)
        {
            DesktopFileManager.RevealFile(item.FilePath);
        }
    }

    /// <summary>Убрать выбранный проект из списка последних (файл на диске не трогаем).</summary>
    [RelayCommand]
    private async Task RemoveSelectedProjectAsync()
    {
        if (SelectedProject is { } item)
        {
            await RemoveRecentAsync(item);
        }
    }

    /// <summary>
    /// Раздел «Облако» реализован на Яндекс.Диске через WebDAV; «Шаблоны»
    /// пока остаются заглушкой и честно сообщают об этом.
    /// </summary>
    [RelayCommand]
    private void ShowComingSoon(string? title) =>
        StatusMessage = Loc.F("Раздел «{0}» появится в следующих версиях.", title);

    [RelayCommand]
    private async Task OpenCloudAsync()
    {
        IsCloudOpen = true;
        ClearCloudMessages();
        var savedKind = ParseCloudProvider(_settings.Current.CloudProvider);
        _suppressCloudProviderRestore = true;
        SelectedCloudProvider = CloudProviders.FirstOrDefault(provider => provider.Kind == savedKind)
            ?? CloudProviders[0];
        _suppressCloudProviderRestore = false;
        await ApplySavedCloudSessionAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void CloseCloud()
    {
        IsCloudOpen = false;
        CloudPassword = null;
    }

    private bool CanConnectCloud() => !IsCloudBusy;

    [RelayCommand(CanExecute = nameof(CanConnectCloud))]
    private async Task ConnectCloudAsync(CancellationToken cancellationToken)
    {
        ClearCloudMessages();
        if (IsGoogleCloud)
        {
            await ConnectGoogleAsync(cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(CloudLogin) || string.IsNullOrEmpty(CloudPassword))
        {
            CloudErrorMessage = Loc.T("Укажите логин Яндекса и пароль приложения.");
            return;
        }

        IsCloudBusy = true;
        CloudStatusMessage = Loc.T("Подключение к Яндекс.Диску…");
        try
        {
            var connected = await _cloud.ConnectAsync(CloudLogin.Trim(), CloudPassword, cancellationToken);
            if (!connected.IsSuccess)
            {
                IsCloudConnected = false;
                CloudStatusMessage = null;
                CloudErrorMessage = Loc.T(connected.Error!.UserMessage);
                return;
            }

            IsCloudConnected = true;
            CloudLogin = _cloud.AccountLogin ?? CloudLogin.Trim();
            var saved = await _settings.UpdateAsync(preferences =>
            {
                preferences.YandexDiskLogin = CloudLogin.Trim();
                preferences.YandexDiskProtectedPassword = SecretProtector.Protect(CloudPassword);
                preferences.CloudProvider = nameof(CloudProviderKind.YandexDisk);
            }, cancellationToken);
            if (!saved.IsSuccess)
            {
                CloudErrorMessage = Loc.T("Подключение выполнено, но сохранить логин не удалось. ")
                    + saved.Error!.UserMessage;
            }

            await RefreshCloudFilesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            CloudStatusMessage = null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            CloudStatusMessage = null;
            CloudErrorMessage = Loc.F("Не удалось подключиться к Яндекс.Диску: {0}", exception.Message);
        }
        finally
        {
            IsCloudBusy = false;
        }
    }

    private async Task ConnectGoogleAsync(CancellationToken cancellationToken)
    {
        IsCloudBusy = true;
        CloudStatusMessage = Loc.T("Подключение к Google Drive…");
        try
        {
            var connected = await _cloud.ConnectInteractiveAsync(cancellationToken);
            if (!connected.IsSuccess)
            {
                IsCloudConnected = false;
                CloudStatusMessage = null;
                CloudErrorMessage = Loc.T(connected.Error!.UserMessage);
                return;
            }

            IsCloudConnected = true;
            CloudLogin = _cloud.AccountLogin;
            var secret = _cloud.SessionSecret;
            var saved = await _settings.UpdateAsync(preferences =>
            {
                preferences.CloudProvider = nameof(CloudProviderKind.GoogleDrive);
                preferences.GoogleDriveAccountEmail = CloudLogin;
                preferences.GoogleDriveProtectedRefreshToken =
                    string.IsNullOrEmpty(secret) ? null : SecretProtector.Protect(secret);
            }, cancellationToken);
            if (!saved.IsSuccess)
            {
                CloudErrorMessage = Loc.T("Подключение выполнено, но сохранить доступ не удалось. ")
                    + saved.Error!.UserMessage;
            }

            await RefreshCloudFilesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            CloudStatusMessage = null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or Win32Exception or InvalidOperationException)
        {
            CloudStatusMessage = null;
            CloudErrorMessage = Loc.F("Не удалось подключиться к Google Drive: {0}", exception.Message);
        }
        finally
        {
            IsCloudBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectCloudAsync()
    {
        var wasGoogle = IsGoogleCloud;
        _cloud.Disconnect();
        IsCloudConnected = false;
        CloudPassword = null;
        CloudFiles.Clear();
        OnPropertyChanged(nameof(HasCloudFiles));
        ClearCloudMessages();
        CloudStatusMessage = Loc.T(wasGoogle ? "Google Drive отключён." : "Яндекс.Диск отключён.");
        await _settings.UpdateAsync(preferences =>
        {
            if (wasGoogle)
            {
                preferences.GoogleDriveAccountEmail = null;
                preferences.GoogleDriveProtectedRefreshToken = null;
            }
            else
            {
                preferences.YandexDiskLogin = null;
                preferences.YandexDiskProtectedPassword = null;
            }
        });
    }

    partial void OnSelectedCloudProviderChanged(CloudProviderInfo? value)
    {
        if (value is null)
        {
            return;
        }

        _cloud = _cloudCatalog.Get(value.Kind);
        CloudFiles.Clear();
        OnPropertyChanged(nameof(HasCloudFiles));
        IsCloudConnected = _cloud.IsConfigured;
        CloudLogin = value.Kind == CloudProviderKind.GoogleDrive
            ? _settings.Current.GoogleDriveAccountEmail
            : _settings.Current.YandexDiskLogin;
        CloudPassword = null;

        if (_suppressCloudProviderRestore)
        {
            return;
        }

        _ = _settings.UpdateAsync(preferences => preferences.CloudProvider = value.Kind.ToString());
        if (IsCloudOpen)
        {
            _ = ApplySavedCloudSessionAsync(CancellationToken.None);
        }
    }

    private async Task ApplySavedCloudSessionAsync(CancellationToken cancellationToken)
    {
        ClearCloudMessages();
        if (_cloud.IsConfigured)
        {
            IsCloudConnected = true;
            await RefreshCloudFilesAsync(cancellationToken);
            return;
        }

        IsCloudConnected = false;
        if (IsGoogleCloud)
        {
            CloudLogin = _settings.Current.GoogleDriveAccountEmail;
            var refreshToken = SecretProtector.Unprotect(_settings.Current.GoogleDriveProtectedRefreshToken);
            if (string.IsNullOrEmpty(refreshToken))
            {
                return;
            }

            await RestoreCloudSessionAsync(CloudLogin, refreshToken, cancellationToken);
            return;
        }

        CloudLogin = _settings.Current.YandexDiskLogin;
        var savedPassword = SecretProtector.Unprotect(_settings.Current.YandexDiskProtectedPassword);
        if (string.IsNullOrEmpty(savedPassword))
        {
            return;
        }

        CloudPassword = savedPassword;
        await RestoreCloudSessionAsync(CloudLogin, savedPassword, cancellationToken);
    }

    private async Task RestoreCloudSessionAsync(
        string? login,
        string secret,
        CancellationToken cancellationToken)
    {
        IsCloudBusy = true;
        CloudStatusMessage = Loc.T(IsGoogleCloud
            ? "Подключение к Google Drive…"
            : "Подключение к Яндекс.Диску…");
        try
        {
            var restored = await _cloud.RestoreAsync(login, secret, cancellationToken);
            if (!restored.IsSuccess)
            {
                IsCloudConnected = false;
                CloudStatusMessage = null;
                CloudErrorMessage = Loc.T(restored.Error!.UserMessage);
                return;
            }

            IsCloudConnected = true;
            CloudLogin = _cloud.AccountLogin ?? login;
            await RefreshCloudFilesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            CloudStatusMessage = null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or Win32Exception or InvalidOperationException)
        {
            CloudStatusMessage = null;
            CloudErrorMessage = Loc.F(
                IsGoogleCloud ? "Не удалось подключиться к Google Drive: {0}" : "Не удалось подключиться к Яндекс.Диску: {0}",
                exception.Message);
        }
        finally
        {
            IsCloudBusy = false;
        }
    }

    private static CloudProviderKind ParseCloudProvider(string? value) =>
        Enum.TryParse<CloudProviderKind>(value, ignoreCase: true, out var kind)
            ? kind
            : CloudProviderKind.YandexDisk;

    private bool CanUseCloud() => IsCloudConnected && !IsCloudBusy;

    [RelayCommand(CanExecute = nameof(CanUseCloud))]
    private async Task RefreshCloudFilesAsync(CancellationToken cancellationToken)
    {
        if (!_cloud.IsConfigured)
        {
            return;
        }

        CloudStatusMessage = Loc.T("Обновление списка облака…");
        CloudErrorMessage = null;
        try
        {
            var listed = await _cloud.ListAsync(cancellationToken);
            if (!listed.IsSuccess)
            {
                CloudStatusMessage = null;
                CloudErrorMessage = Loc.T(listed.Error!.UserMessage);
                return;
            }

            CloudFiles.Clear();
            foreach (var file in listed.Require())
            {
                CloudFiles.Add(new CloudFileItem(file, DownloadCloudFileCommand));
            }

            OnPropertyChanged(nameof(HasCloudFiles));
            CloudStatusMessage = CloudFiles.Count == 0
                ? Loc.T("В облаке пока нет проектов. Загрузите первый.")
                : Loc.F("Проектов в облаке: {0}.", CloudFiles.Count);
        }
        catch (OperationCanceledException)
        {
            CloudStatusMessage = null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            CloudStatusMessage = null;
            CloudErrorMessage = Loc.F("Не удалось получить список облака: {0}", exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseCloud))]
    private async Task UploadToCloudAsync(CancellationToken cancellationToken)
    {
        ClearCloudMessages();
        var path = await ResolveCloudUploadPathAsync(cancellationToken);
        if (path is null)
        {
            return;
        }

        IsCloudBusy = true;
        CloudStatusMessage = Loc.F("Загрузка «{0}»…", Path.GetFileName(path));
        try
        {
            var uploaded = await _cloud.UploadAsync(path, cancellationToken);
            if (!uploaded.IsSuccess)
            {
                CloudStatusMessage = null;
                CloudErrorMessage = Loc.T(uploaded.Error!.UserMessage);
                return;
            }

            await RefreshCloudFilesAsync(cancellationToken);
            CloudStatusMessage = Loc.F(
                "«{0}» загружен в {1}.",
                Path.GetFileName(path),
                SelectedCloudProvider?.DisplayName ?? "облако");
        }
        catch (OperationCanceledException)
        {
            CloudStatusMessage = null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            CloudStatusMessage = null;
            CloudErrorMessage = Loc.F("Не удалось загрузить проект: {0}", exception.Message);
        }
        finally
        {
            IsCloudBusy = false;
        }
    }

    /// <summary>
    /// Что загружать: открытый проект (сначала сохраняем правки) или выбранную
    /// карточку из списка последних.
    /// </summary>
    private async Task<string?> ResolveCloudUploadPathAsync(CancellationToken cancellationToken)
    {
        if (_projects.Current is { } openProject)
        {
            if (_editor.SceneId is not null && _editor.IsDirty)
            {
                var committed = await _editor.CommitAsync(cancellationToken);
                if (!committed.IsSuccess)
                {
                    CloudErrorMessage = Loc.T(committed.Error!.UserMessage);
                    return null;
                }
            }

            if (_projects.IsDirty)
            {
                var saved = await _projects.SaveAsync(cancellationToken);
                if (!saved.IsSuccess)
                {
                    CloudErrorMessage = Loc.T(saved.Error!.UserMessage);
                    return null;
                }
            }

            return openProject.FilePath;
        }

        if (SelectedProject is { } selected && File.Exists(selected.FilePath))
        {
            return selected.FilePath;
        }

        CloudErrorMessage = Loc.T("Откройте проект или выберите его карточку в списке.");
        return null;
    }

    [RelayCommand(CanExecute = nameof(CanUseCloud))]
    private async Task DownloadCloudFileAsync(CloudFileItem? item, CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        ClearCloudMessages();
        var path = await _dialogs.PickCloudDownloadPathAsync(
            item.FileName,
            SuggestedDirectory(),
            cancellationToken);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        IsCloudBusy = true;
        CloudStatusMessage = Loc.F("Скачивание «{0}»…", item.FileName);
        try
        {
            var downloaded = await _cloud.DownloadAsync(item.File, path, cancellationToken);
            if (!downloaded.IsSuccess)
            {
                CloudStatusMessage = null;
                CloudErrorMessage = Loc.T(downloaded.Error!.UserMessage);
                return;
            }

            await _settings.AddRecentProjectAsync(new RecentProjectEntry
            {
                FilePath = path,
                Name = item.Name,
                LastOpenedAt = DateTimeOffset.Now,
                UpdatedAt = item.File.ModifiedAt ?? DateTimeOffset.Now,
            }, cancellationToken);
            RefreshRecentProjects();
            CloudStatusMessage = Loc.F("«{0}» скачан: {1}", item.FileName, path);
        }
        catch (OperationCanceledException)
        {
            CloudStatusMessage = null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            CloudStatusMessage = null;
            CloudErrorMessage = Loc.F("Не удалось скачать проект: {0}", exception.Message);
        }
        finally
        {
            IsCloudBusy = false;
        }
    }

    private void ClearCloudMessages()
    {
        CloudStatusMessage = null;
        CloudErrorMessage = null;
    }

    /// <summary>Раздел «Блог» открывает сайт автора: экрана блога внутри приложения нет.</summary>
    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            ErrorMessage = Loc.F("Не удалось открыть ссылку: {0}", exception.Message);
        }
    }

    /// <summary>Загрузка настроек и списка последних проектов при старте приложения.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _settings.LoadAsync(cancellationToken);
        if (!loaded.IsSuccess)
        {
            ErrorMessage = Loc.T(loaded.Error!.UserMessage);
        }

        _culture.Apply(_settings.Current.Language);
        _theme.Apply(_settings.Current.Theme is AppTheme.GreenStart or AppTheme.Cabinet or AppTheme.Jam or AppTheme.Studio
            ? _settings.Current.Theme : AppTheme.Starter);

        RefreshRecentProjects();
        CurrentProject = _projects.Current;
        IsDirty = _projects.IsDirty;
        if (CurrentProject is { } open)
        {
            SelectedProject = RecentProjects.FirstOrDefault(
                item => string.Equals(item.FilePath, open.FilePath, StringComparison.OrdinalIgnoreCase))
                ?? SelectedProject;
        }

        OpenArchitectureCommand.NotifyCanExecuteChanged();
        OpenCharactersCommand.NotifyCanExecuteChanged();
        OpenTimelineCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CreateProjectAsync(CancellationToken cancellationToken)
    {
        ClearMessages();
        var path = await _dialogs.PickProjectToCreateAsync(
            Loc.T("Новый проект") + ".scri",
            SuggestedDirectory(),
            cancellationToken);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // Создание нового проекта при уже открытом не должно упираться в
        // «Сначала закройте текущий проект»: текущий сначала сохраняется.
        if (!await PrepareProjectSwitchAsync(cancellationToken))
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        StatusMessage = Loc.T("Создание проекта…");
        var created = await _projects.CreateAsync(
            new CreateProjectRequest { FilePath = path, Name = name },
            cancellationToken);

        StatusMessage = null;
        await HandleLoadedProjectAsync(created, path, cancellationToken);
    }

    [RelayCommand]
    private async Task OpenProjectAsync(CancellationToken cancellationToken)
    {
        ClearMessages();
        var path = await _dialogs.PickProjectToOpenAsync(SuggestedDirectory(), cancellationToken);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await OpenPathAsync(path, cancellationToken);
    }

    [RelayCommand]
    private async Task OpenRecentAsync(RecentProjectItem? item)
    {
        if (item is null)
        {
            return;
        }

        ClearMessages();
        await OpenPathAsync(item.FilePath, CancellationToken.None);
    }

    [RelayCommand]
    private async Task RemoveRecentAsync(RecentProjectItem? item)
    {
        if (item is null)
        {
            return;
        }

        var removed = await _settings.RemoveRecentProjectAsync(item.FilePath);
        if (!removed.IsSuccess)
        {
            await ReportAsync(removed.Error!);
            return;
        }

        RefreshRecentProjects();
        StatusMessage = Loc.F("Проект «{0}» убран из списка последних.", item.Name);
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        ClearMessages();
        var saved = await _projects.SaveAsync();
        if (!saved.IsSuccess)
        {
            await ReportAsync(saved.Error!);
            return;
        }

        CurrentProject = _projects.Current;
        IsDirty = _projects.IsDirty;
        StatusMessage = Loc.T("Проект сохранён.");
        if (CurrentProject is { } project)
        {
            await RememberProjectAsync(project, project.FilePath, CancellationToken.None, updateLastOpenedAt: false);
        }
    }

    [RelayCommand]
    private async Task CloseProjectAsync()
    {
        if (!IsProjectOpen)
        {
            return;
        }

        ClearMessages();

        // Кнопка «Закрыть проект» сохраняет правки, а не выбрасывает их:
        // тот же порядок, что при смене проекта.
        if (!await PrepareProjectSwitchAsync(CancellationToken.None))
        {
            return;
        }

        StatusMessage = Loc.T("Проект закрыт.");
    }

    /// <summary>
    /// Открыть проект, путь к которому передан аргументом командной строки (ТЗ п. 27, 31).
    /// </summary>
    public async Task OpenProjectFromCommandLineAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        ClearMessages();
        await OpenPathAsync(path, cancellationToken);
    }

    /// <summary>Открывает последний проект, если пользователь включил эту опцию.</summary>
    public async Task OpenLastProjectAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Current.OpenLastProjectOnStartup
            || _settings.Current.RecentProjects.FirstOrDefault() is not { } recent)
        {
            return;
        }

        ClearMessages();
        await OpenPathAsync(recent.FilePath, cancellationToken);
    }

    public void RefreshRecentProjects()
    {
        // Выделение переживает обновление списка: ищем ту же карточку по пути.
        var selectedPath = SelectedProject?.FilePath;
        var current = _projects.Current;
        RecentProjects.Clear();
        foreach (var entry in _settings.Current.RecentProjects)
        {
            RecentProjects.Add(new RecentProjectItem(
                MergeWithOpenProject(entry, current),
                OpenRecentCommand,
                RemoveRecentCommand,
                SelectProjectCommand,
                OpenFolderCommand));
        }

        SelectedProject = selectedPath is null
            ? RecentProjects.FirstOrDefault()
            : RecentProjects.FirstOrDefault(
                  item => string.Equals(item.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
              ?? RecentProjects.FirstOrDefault();

        OnPropertyChanged(nameof(HasRecentProjects));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(ProjectCount));
        RefreshVisibleProjects();
    }

    public Task RefreshRecentProjectSummaryAsync(CancellationToken cancellationToken = default)
    {
        return _projects.Current is { } project
            ? RememberProjectAsync(project, project.FilePath, cancellationToken, updateLastOpenedAt: false)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Для открытого проекта карточка показывает живую сводку, а не кэш: автор
    /// добавляет главы и сцены, и карточка должна меняться сразу, не дожидаясь
    /// повторного открытия проекта.
    /// </summary>
    private static RecentProjectEntry MergeWithOpenProject(RecentProjectEntry entry, ProjectDto? current)
    {
        if (current is null
            || !string.Equals(current.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return entry;
        }

        return entry with
        {
            Name = current.Name,
            Description = current.Description,
            BookCount = current.Series.Sum(series => series.Books.Count),
            ChapterCount = current.Series
                .SelectMany(series => series.Books)
                .Sum(book => book.Chapters.Count),
            SceneCount = current.Series
                .SelectMany(series => series.Books)
                .SelectMany(book => book.Chapters)
                .Sum(chapter => chapter.Scenes.Count),
            CharacterCount = current.Characters.Count,
            PlotLineCount = current.PlotLines.Count,
            UpdatedAt = current.UpdatedAt,
        };
    }

    /// <summary>Применяет строку поиска к списку карточек.</summary>
    private void RefreshVisibleProjects()
    {
        var query = ProjectSearchQuery?.Trim();
        VisibleProjects.Clear();
        foreach (var item in RecentProjects)
        {
            if (string.IsNullOrEmpty(query)
                || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                VisibleProjects.Add(item);
            }
        }

        OnPropertyChanged(nameof(HasVisibleProjects));
    }

    /// <summary>Состояние проекта изменилось вне этого экрана (например, редактором).</summary>
    public void SyncProjectState()
    {
        CurrentProject = _projects.Current;
        IsDirty = _projects.IsDirty;
        OpenArchitectureCommand.NotifyCanExecuteChanged();
        OpenSettingsCommand.NotifyCanExecuteChanged();
        OpenExportCommand.NotifyCanExecuteChanged();
        OpenCharactersCommand.NotifyCanExecuteChanged();
        OpenTimelineCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(FormatNotice));
        OnPropertyChanged(nameof(HasFormatNotice));

        // Структура проекта могла измениться: карточка открытого проекта должна
        // показывать свежие счётчики.
        RefreshRecentProjects();
    }

    /// <summary>
    /// Готовит смену проекта: правки открытой сцены коммитятся в проект, проект
    /// сохраняется на диск, и только после этого он закрывается. Так «Открыть
    /// проект» и «Новый проект» не упираются в «Сначала закройте текущий
    /// проект», а работа автора не теряется.
    /// </summary>
    /// <returns><c>false</c>, если сохранить не удалось и смену отменили.</returns>
    private async Task<bool> PrepareProjectSwitchAsync(CancellationToken cancellationToken)
    {
        if (!IsProjectOpen)
        {
            return true;
        }

        var saved = await _workspace.SaveAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            await ReportAsync(saved.Error!);
            return false;
        }

        var closed = await _projects.CloseAsync(discardChanges: false, cancellationToken);
        if (!closed.IsSuccess)
        {
            await ReportAsync(closed.Error!);
            return false;
        }

        CurrentProject = null;
        IsDirty = false;
        return true;
    }

    private async Task OpenPathAsync(string path, CancellationToken cancellationToken)
    {
        ClearMessages();

        // Открытие другого проекта при уже открытом: текущий проект и правки
        // редактора сохраняются, и только потом освобождается состояние.
        if (!await PrepareProjectSwitchAsync(cancellationToken))
        {
            return;
        }

        if (_settings.Current.CheckRecoveryOnStartup)
        {
            var recovery = await _projects.DetectRecoveryAsync(path, cancellationToken);
            if (!recovery.IsSuccess)
            {
                await ReportAsync(recovery.Error!);
                return;
            }

            if (recovery.Value is { } info)
            {
                var restore = await _dialogs.ConfirmAsync(
                    "Найдено восстановление",
                    Loc.F("Найден файл восстановления от {0:g}. Открыть восстановленные данные?", info.WrittenAt.ToLocalTime()),
                    "Открыть восстановление",
                    "Открыть файл проекта");

                if (restore)
                {
                    var recovered = await _projects.RecoverAsync(info, cancellationToken);
                    await HandleLoadedProjectAsync(recovered, path, cancellationToken);
                    return;
                }

                await _projects.DiscardRecoveryAsync(info, cancellationToken);
            }
        }

        var opened = await _projects.OpenAsync(path, cancellationToken);
        await HandleLoadedProjectAsync(opened, path, cancellationToken);
    }

    private async Task HandleLoadedProjectAsync(
        Result<ProjectDto> result,
        string path,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            await ReportAsync(result.Error!);
            return;
        }

        var project = result.Require();
        CurrentProject = project;
        IsDirty = _projects.IsDirty;
        StatusMessage = Loc.F("Проект «{0}» открыт. Книг: {1}.", project.Name, project.Series.Count);

        await RememberProjectAsync(project, path, cancellationToken);

        // Только что открытый проект сразу показываем в «Контексте проекта».
        SelectedProject = RecentProjects.FirstOrDefault(
            item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase))
            ?? SelectedProject;

        // Кнопка «Открыть» сразу ведёт в рабочее пространство проекта: автор
        // продолжает работу, а не остаётся на лаунчере.
        await OpenProjectWorkspaceAsync(project, cancellationToken);
    }

    /// <summary>
    /// Переводит оболочку в рабочее пространство только что открытого проекта.
    /// Если в проекте есть сцена, открываем её в редакторе; у только что созданного
    /// проекта сцен нет, поэтому показываем карту книги — иначе проект «не
    /// открывается» с точки зрения автора: экран оставался бы лаунчером.
    /// </summary>
    private async Task OpenProjectWorkspaceAsync(ProjectDto project, CancellationToken cancellationToken)
    {
        var scene = project.Series
            .SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Scenes)
            .FirstOrDefault();

        if (scene is null)
        {
            // Пустой проект: открывать в редакторе нечего, ведём на карту книги.
            // Там автор создаёт книгу, главу и первую сцену.
            _navigation.NavigateTo(AppScreen.Architecture);
            return;
        }

        var opened = await _editor.OpenSceneAsync(scene.Id, cancellationToken);
        if (!opened.IsSuccess)
        {
            await ReportAsync(opened.Error!);
            return;
        }

        _navigation.NavigateTo(AppScreen.Editor);
    }

    /// <summary>
    /// Кэширует сводку проекта в списке последних, чтобы лаунчер показывал карточку
    /// без чтения файла проекта (макет «Мои проекты»).
    /// </summary>
    private async Task RememberProjectAsync(
        ProjectDto project,
        string path,
        CancellationToken cancellationToken,
        bool updateLastOpenedAt = true)
    {
        var books = project.Series.Sum(series => series.Books.Count);
        var chapters = project.Series
            .SelectMany(series => series.Books)
            .Sum(book => book.Chapters.Count);
        var scenes = project.Series
            .SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .Sum(chapter => chapter.Scenes.Count);
        var existing = _settings.Current.RecentProjects.FirstOrDefault(
            item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase));

        var saved = await _settings.AddRecentProjectAsync(new RecentProjectEntry
        {
            FilePath = path,
            Name = project.Name,
            LastOpenedAt = updateLastOpenedAt ? DateTimeOffset.UtcNow : existing?.LastOpenedAt ?? DateTimeOffset.UtcNow,
            Description = project.Description,
            BookCount = books,
            ChapterCount = chapters,
            SceneCount = scenes,
            CharacterCount = project.Characters.Count,
            PlotLineCount = project.PlotLines.Count,
            UpdatedAt = project.UpdatedAt,
        }, cancellationToken);

        if (!saved.IsSuccess)
        {
            await ReportAsync(saved.Error!);
        }

        RefreshRecentProjects();
    }

    private async Task ReportAsync(AppError error)
    {
        ErrorMessage = error.SuggestedAction is null
            ? Loc.T(error.UserMessage)
            : $"{Loc.T(error.UserMessage)} {Loc.T(error.SuggestedAction)}";

        await _dialogs.ShowErrorAsync(Loc.T("Ошибка"), ErrorMessage);
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        StatusMessage = null;
    }

    /// <summary>Отписка от шины событий (ТЗ п. 43).</summary>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private string? SuggestedDirectory()
    {
        var recent = _settings.Current.RecentProjects.FirstOrDefault();
        if (recent is not null)
        {
            var directory = Path.GetDirectoryName(recent.FilePath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
