namespace ScriSelfStudio.UiSmokeTests;

using ScriSelfStudio.Presentation.Theme;

public sealed class CabinetThemeTests
{
    [Theory]
    [InlineData("PaperTextBrush", "PaperBrush")]
    [InlineData("PaperMutedBrush", "PaperBrush")]
    [InlineData("SidebarSubTextBrush", "PanelBrush")]
    [InlineData("ShellNavBrush", "SurfaceChromeBrush")]
    [InlineData("ShellSubtitleBrush", "SurfaceChromeBrush")]
    [InlineData("AccentTextBrush", "AccentBrush")]
    [InlineData("SidebarActiveTextBrush", "SurfaceSelectedBrush")]
    public void Text_has_at_least_four_and_a_half_to_one_contrast(string text, string surface)
    {
        var brushes = ThemePalette.Cabinet.Brushes;
        var foreground = Luminance(brushes[text]);
        var background = Luminance(brushes[surface]);
        var contrast = (Math.Max(foreground, background) + 0.05) / (Math.Min(foreground, background) + 0.05);
        Assert.True(contrast >= 4.5, $"{text} on {surface}: {contrast:F2}:1");
    }

    private static double Luminance(string hex)
    {
        var rgb = Convert.FromHexString(hex[1..]);
        return 0.2126 * Linear(rgb[0]) + 0.7152 * Linear(rgb[1]) + 0.0722 * Linear(rgb[2]);
    }

    private static double Linear(byte channel)
    {
        var srgb = channel / 255d;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }
}
