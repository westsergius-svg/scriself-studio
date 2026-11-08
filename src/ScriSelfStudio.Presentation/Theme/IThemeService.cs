namespace ScriSelfStudio.Presentation.Theme;

using ScriSelfStudio.Core.Model;

/// <summary>Применение выбранной темы оформления (ТЗ п. 6, 14).</summary>
public interface IThemeService
{
    AppTheme Current { get; }

    /// <summary>Палитра применена; оболочка может обновить компоновку.</summary>
    event EventHandler? Changed;

    void Apply(AppTheme theme);
}
