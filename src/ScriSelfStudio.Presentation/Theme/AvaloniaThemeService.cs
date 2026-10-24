namespace ScriSelfStudio.Presentation.Theme;

using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using ScriSelfStudio.Core.Model;

/// <inheritdoc />
public sealed class AvaloniaThemeService : IThemeService
{
    // Макет ScriSelf Studio использует кремовую бумажную тему как стартовую.
    // Это также предотвращает краткое появление тёмного Fluent-интерфейса до
    // завершения загрузки пользовательских настроек.
    public AppTheme Current { get; private set; } = AppTheme.Starter;

    public void Apply(AppTheme theme)
    {
        Current = theme;
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = theme switch
        {
            AppTheme.Light or AppTheme.Paper or AppTheme.Starter => ThemeVariant.Light,
            // Оформленные темы, кроме Paper/Starter, тёмные; это сохраняет
            // читаемость базовых контролов и согласует их с палитрой.
            _ => ThemeVariant.Dark,
        };

        var palette = ThemePalette.For(theme);
        if (theme is AppTheme.System or AppTheme.Light or AppTheme.Dark)
        {
            // Возврат к системной теме: убираем подмену, чтобы снова работали
            // словари Light/Dark из Palette.axaml.
            foreach (var key in palette.Brushes.Keys)
            {
                application.Resources.Remove(key);
            }

            return;
        }

        ApplyPalette(palette);
    }

    /// <summary>
    /// Подменяет кисти темы в общих ресурсах приложения. Ресурсы объявлены
    /// в <c>App.Resources</c> без <c>ThemeDictionaries</c>, поэтому смена темы
    /// видна всем экранам сразу и переживает перезапуск (тема в настройках).
    /// </summary>
    private static void ApplyPalette(ThemePalette palette)
    {
        if (Application.Current?.Resources is not { } resources)
        {
            return;
        }

        foreach (var (key, color) in palette.Brushes)
        {
            resources[key] = new SolidColorBrush(Color.Parse(color));
        }
    }
}
