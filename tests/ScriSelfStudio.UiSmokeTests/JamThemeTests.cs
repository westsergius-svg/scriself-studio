namespace ScriSelfStudio.UiSmokeTests;

using Avalonia.Media;
using ScriSelfStudio.Presentation.Theme;

public sealed class JamThemeTests
{
    [Theory]
    [InlineData("PaperTextBrush", "PaperBrush")]
    [InlineData("PaperMutedBrush", "PaperBrush")]
    [InlineData("SidebarTextBrush", "PanelBrush")]
    [InlineData("SidebarSubTextBrush", "PanelBrush")]
    [InlineData("ShellNavBrush", "SurfaceChromeBrush")]
    [InlineData("ShellSubtitleBrush", "SurfaceChromeBrush")]
    [InlineData("SidebarActiveTextBrush", "SurfaceSelectedBrush")]
    [InlineData("AccentTextBrush", "AccentBrush")]
    public void Glass_preserves_text_contrast_over_light_and_dark_backdrops(string text, string surface)
    {
        var palette = ThemePalette.Jam.Brushes;
        // Include a much darker backing than the tinted acrylic fallback.
        foreach (var backdrop in new[] { "#FFFFFF", "#A0A8BC" })
        {
            var background = Composite(Color.Parse(palette[surface]), Color.Parse(backdrop));
            var foreground = Composite(Color.Parse(palette[text]), background);
            var l1 = Luminance(foreground);
            var l2 = Luminance(background);
            var ratio = (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
            Assert.True(ratio >= 4.5, $"{text} on {surface} over {backdrop}: {ratio:F2}:1");
        }
    }

    private static Color Composite(Color foreground, Color background)
    {
        var alpha = foreground.A / 255d;
        return Color.FromRgb(
            (byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
            (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
            (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte value)
    {
        var channel = value / 255d;
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
