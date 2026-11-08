namespace ScriSelfStudio.Presentation.Theme;

using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using ScriSelfStudio.Core.Model;

/// <inheritdoc />
public sealed class AvaloniaThemeService : IThemeService
{
    // Стандартная тема использует светлые поверхности и синий акцент.
    // Это также предотвращает краткое появление тёмного Fluent-интерфейса до
    // завершения загрузки пользовательских настроек.
    public AppTheme Current { get; private set; } = AppTheme.Starter;

    public event EventHandler? Changed;

    public void Apply(AppTheme theme)
    {
        Current = theme;
        if (Application.Current is not { } application)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        application.RequestedThemeVariant = theme switch
        {
            AppTheme.Light or AppTheme.Paper or AppTheme.Starter or AppTheme.GreenStart or AppTheme.Cabinet or AppTheme.Jam => ThemeVariant.Light,
            // Остальные оформленные темы тёмные; это сохраняет
            // читаемость базовых контролов и согласует их с палитрой.
            _ => ThemeVariant.Dark,
        };

        var palette = ThemePalette.For(theme);
        application.Resources["WorkspacePanelRadius"] = new CornerRadius(theme == AppTheme.Studio ? 2 : 14);
        application.Resources["MapPanelRadius"] = new CornerRadius(theme == AppTheme.Studio ? 2 : 12);
        application.Resources["MapCardRadius"] = new CornerRadius(theme == AppTheme.Studio ? 2 : 10);
        application.Resources["WorkspaceHeadingFont"] = new FontFamily(theme == AppTheme.Studio ? "Inter, Segoe UI, sans-serif" : "Georgia");
        application.Resources["WorkspaceOuterMargin"] = new Thickness(theme == AppTheme.Studio ? 10 : 24);
        var accent = theme is AppTheme.System or AppTheme.Light or AppTheme.Dark
            ? default : Color.Parse(palette.Brushes["AccentBrush"]);
        foreach (var fluent in application.Styles.OfType<FluentTheme>())
        {
            foreach (var colors in fluent.Palettes.Values)
            {
                colors.Accent = accent;
            }
        }

        if (theme is AppTheme.System or AppTheme.Light or AppTheme.Dark)
        {
            // Возврат к системной теме: убираем подмену, чтобы снова работали
            // словари Light/Dark из Palette.axaml.
            foreach (var key in palette.Brushes.Keys)
            {
                application.Resources.Remove(key);
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        ApplyPalette(palette);
        Changed?.Invoke(this, EventArgs.Empty);
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
