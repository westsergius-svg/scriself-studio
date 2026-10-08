namespace ScriSelfStudio.App.Views;

using Avalonia.Controls;

/// <summary>
/// Плавающий переключатель тем в правом нижнем углу (макет редактора).
/// Модель общая с оболочкой — <c>ScriSelfStudio.App.Shell.ShellViewModel</c>.
/// </summary>
public partial class ThemeSwitcherView : UserControl
{
    /// <summary>Конструктор без параметров нужен загрузчику XAML и дизайнеру.</summary>
    public ThemeSwitcherView() => InitializeComponent();
}