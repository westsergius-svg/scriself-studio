namespace ScriSelfStudio.Core.Model;

/// <summary>Настройки проекта (ТЗ п. 5, 27).</summary>
public sealed class ProjectSettings
{
    public const int DefaultAutoSaveIntervalSeconds = 60;

    public const double MinFontSize = 8d;

    public const double MaxFontSize = 72d;

    public const int MinAutoSaveIntervalSeconds = 5;

    public const int MaxAutoSaveIntervalSeconds = 3600;

    /// <summary>Тема оформления; по умолчанию базовая Studio (макет редактора).</summary>
    public AppTheme Theme { get; set; } = AppTheme.Paper;

    public string FontFamily { get; set; } = "Inter";

    public double FontSize { get; set; } = 16d;

    public bool AutoSaveEnabled { get; set; } = true;

    public int AutoSaveIntervalSeconds { get; set; } = DefaultAutoSaveIntervalSeconds;

    public bool BackupsEnabled { get; set; } = true;

    public string Language { get; set; } = "ru-RU";

    /// <summary>Копия для снимка UI: снимок не должен менять настройки проекта напрямую.</summary>
    public ProjectSettings Clone() => new()
    {
        Theme = Theme,
        FontFamily = FontFamily,
        FontSize = FontSize,
        AutoSaveEnabled = AutoSaveEnabled,
        AutoSaveIntervalSeconds = AutoSaveIntervalSeconds,
        BackupsEnabled = BackupsEnabled,
        Language = Language,
    };
}
