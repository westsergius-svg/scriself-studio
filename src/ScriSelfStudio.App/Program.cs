namespace ScriSelfStudio.App;

using Avalonia;
#if DEBUG
using AvaloniaMcp.Diagnostics;
#endif

/// <summary>Точка входа приложения.</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupProjectPath = StartupCommandLine.ResolveProjectPath(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Проект, переданный аргументом командной строки, или <c>null</c>.</summary>
    public static string? StartupProjectPath { get; private set; }

    /// <summary>Используется также headless-тестами интерфейса.</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

#if DEBUG
        // Диагностический MCP-мост к живому окну (docs/decisions/006). Только Debug:
        // в Release и в опубликованном exe сервер не запускается.
        builder = builder.UseMcpDiagnostics();
#endif

        return builder;
    }
}
