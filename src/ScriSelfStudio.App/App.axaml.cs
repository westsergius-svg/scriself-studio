namespace ScriSelfStudio.App;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation.Localization;
using ScriSelfStudio.Presentation.Theme;
using ScriSelfStudio.Presentation.Statistics;

/// <summary>Композиция зависимостей и запуск главного окна.</summary>
public partial class App : Application
{
    private ServiceProvider? _provider;
    private bool _shutdownFlushed;
    private DispatcherTimer? _statisticsTimer;
    private readonly CancellationTokenSource _statisticsLifetime = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Тестовый хост может запустить тот же жизненный цикл с отдельным профилем.</summary>
    protected virtual ServiceProvider CreateServices() => ScriSelfStudioServices.Build();

    public override void OnFrameworkInitializationCompleted()
    {
        // Контейнер собирается только для настоящего приложения. В headless-тестах
        // жизненного цикла нет, и сборка «боевого» контейнера читала бы настройки
        // пользователя из %APPDATA% (тему и язык) и перебивала оформление теста:
        // кадры экранов получались то светлыми, то тёмными.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _provider = CreateServices();
            // Лаунчер применяет язык после асинхронного чтения настроек;
            // живые привязки обновляют уже созданное главное окно.
            desktop.MainWindow = _provider.GetRequiredService<MainWindow>();
            desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Maximized;
            desktop.ShutdownRequested += OnShutdownRequested;
            desktop.Exit += (_, _) =>
            {
                _statisticsTimer?.Stop();
                _statisticsLifetime.Cancel();
                _provider?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();

        if (_provider is null)
        {
            return;
        }

        // Асинхронная инициализация не бросает исключений: все операции возвращают Result,
        // а ошибки показываются на стартовом экране.
        _ = InitializeAsync();
    }

    /// <summary>
    /// Перед закрытием приложения фиксирует текст и черновики, которые ещё не
    /// дошли до проекта. Закрытие откладывается до завершения записи.
    /// </summary>
    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownFlushed
            || sender is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow?.DataContext is not ShellViewModel shell
            || !shell.HasPendingEdits)
        {
            return;
        }

        e.Cancel = true;
        _shutdownFlushed = true;
        try
        {
            await shell.FlushPendingEditsAsync();
        }
        catch
        {
            // Выход не должен блокироваться ошибкой сохранения: пользователь
            // уже закрыл окно, а сообщение об ошибке некуда показать.
        }

        desktop.Shutdown();
    }

    private async Task InitializeAsync()
    {
        if (_provider is null)
        {
            return;
        }

        var launcher = _provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        var preferences = _provider.GetRequiredService<IAppSettingsService>().Current;

        if (Program.StartupProjectPath is { Length: > 0 } startupProject)
        {
            await launcher.OpenProjectFromCommandLineAsync(startupProject);
        }
        else if (preferences.OpenLastProjectOnStartup)
        {
            await launcher.OpenLastProjectAsync();
        }

        // Выбор хранится отдельно от проверки обновлений; до согласия сигналов нет.
        if (preferences.ShareUsageStatistics is null)
        {
            var allowed = await _provider.GetRequiredService<IFileDialogService>().ConfirmAsync(
                Loc.T("Статистика использования"),
                Loc.T("Помочь понять, сколько людей пользуются ScriSelf Studio? Раз в день программа отправит на scriself.com случайный номер установки, версию и платформу. Имена, проекты и текст не передаются. Отправку можно отключить в настройках."),
                Loc.T("Разрешить"), Loc.T("Не отправлять"));
            await _provider.GetRequiredService<IAppSettingsService>().UpdateAsync(p => p.ShareUsageStatistics = allowed);
            _provider.GetRequiredService<SettingsViewModel>().Load();
        }

        if (_statisticsLifetime.IsCancellationRequested) { return; }
        _statisticsTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _statisticsTimer.Tick += (_, _) => ReportActivityIfActive();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime && lifetime.MainWindow is { } window)
        {
            window.Activated += (_, _) => ReportActivityIfActive();
        }
        _statisticsTimer.Start();
        ReportActivityIfActive();

        // После выбора статистики, чтобы два диалога не открылись одновременно.
        // Сетевая проверка не задерживает открытие проекта и работу редактора.
        if (preferences.CheckForUpdatesOnStartup)
        {
            _ = launcher.CheckForUpdatesOnStartupAsync();
        }
    }

    private void ReportActivityIfActive()
    {
        if (!_statisticsLifetime.IsCancellationRequested
            && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.IsActive == true)
        {
            _ = _provider!.GetRequiredService<UsageStatisticsService>().ReportActivityAsync(_statisticsLifetime.Token);
        }
    }
}
