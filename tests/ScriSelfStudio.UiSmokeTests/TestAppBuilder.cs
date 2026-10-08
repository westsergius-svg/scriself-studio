using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(ScriSelfStudio.UiSmokeTests.TestAppBuilder))]

namespace ScriSelfStudio.UiSmokeTests;

/// <summary>
/// Сборка приложения для headless-тестов интерфейса. Рисующий headless-режим
/// (Skia) нужен, чтобы можно было снимать кадры окна и проверять раскладку (ТЗ п. 58).
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<ScriSelfStudio.App.App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
