namespace ScriSelfStudio.Launcher;

using Avalonia.Controls;

/// <summary>Стартовый экран. Логика находится только в <see cref="LauncherViewModel"/>.</summary>
public partial class LauncherView : UserControl
{
    public LauncherView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            // Reserve room for project actions on a short desktop window.
            var size = Bounds.Height < 760 ? 96d : 160d;
            ThanksQr.MaxWidth = size;
            ThanksQr.MaxHeight = size;
        };
    }
}
