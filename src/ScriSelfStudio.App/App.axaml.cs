namespace ScriSelfStudio.App;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation.Localization;
using ScriSelfStudio.Presentation.Theme;

/// <summary>Композиция зависимостей и запуск главного окна.</summary>
public partial class App : Application
{
    private ServiceProvider? _provider;
    private bool _shutdownFlushed;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Контейнер собирается только для настоящего приложения. В headless-тестах
        // жизненного цикла нет, и сборка «боевого» контейнера читала бы настройки
        // пользователя из %APPDATA% (тему и язык) и перебивала оформление теста:
        // кадры экранов получались то светлыми, то тёмными.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _provider = ScriSelfStudioServices.Build();
            // Язык применяется до создания главного окна: подписи enum и разделов
            // создаются вместе с моделями экранов и должны сразу быть на нужном языке.
            var preferences = _provider.GetRequiredService<IAppSettingsService>().Current;
            _provider.GetRequiredService<ICultureService>().Apply(preferences.Language);
            desktop.MainWindow = _provider.GetRequiredService<MainWindow>();
            desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Maximized;
            desktop.ShutdownRequested += OnShutdownRequested;
            desktop.Exit += (_, _) => _provider?.Dispose();
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

        // Проверка обновлений идёт после открытия проекта и не задерживает запуск:
        // сеть может быть недоступна, тогда приложение просто продолжает работу.
        if (preferences.CheckForUpdatesOnStartup)
        {
            _ = launcher.CheckForUpdatesOnStartupAsync();
        }
    }
}
