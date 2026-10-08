namespace ScriSelfStudio.App;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation.Localization;
using ScriSelfStudio.Presentation.Theme;

/// <summary>Композиция зависимостей и запуск главного окна.</summary>
public partial class App : Application
{
    private ServiceProvider? _provider;

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
            desktop.MainWindow = _provider.GetRequiredService<MainWindow>();
            desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Maximized;
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

    private async Task InitializeAsync()
    {
        if (_provider is null)
        {
            return;
        }

        var launcher = _provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        var preferences = _provider.GetRequiredService<IAppSettingsService>().Current;
        _provider.GetRequiredService<ICultureService>().Apply(preferences.Language);

        if (Program.StartupProjectPath is { Length: > 0 } startupProject)
        {
            await launcher.OpenProjectFromCommandLineAsync(startupProject);
        }
    }
}
