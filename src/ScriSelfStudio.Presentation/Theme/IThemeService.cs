namespace ScriSelfStudio.Presentation.Theme;

using ScriSelfStudio.Core.Model;

/// <summary>Применение выбранной темы оформления (ТЗ п. 6, 14).</summary>
public interface IThemeService
{
    AppTheme Current { get; }

    void Apply(AppTheme theme);
}
