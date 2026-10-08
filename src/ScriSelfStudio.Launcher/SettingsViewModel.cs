namespace ScriSelfStudio.Launcher;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Localization;
using ScriSelfStudio.Presentation.Theme;

/// <summary>Вариант темы оформления для списка (ТЗ п. 27).</summary>
public sealed record ThemeOption(AppTheme Value, string Title);

/// <summary>Вариант языка приложения для списка (ТЗ п. 27).</summary>
public sealed record LanguageOption(string Value, string Title);

/// <summary>Вариант шрифта для списка (ТЗ п. 27).</summary>
public sealed record FontOption(string Value, string Title);

/// <summary>
/// Экран настроек (ТЗ п. 27, 35): тема, язык, шрифт и размер, автосохранение,
/// резервные копии. Настройки применяются и сохраняются сразу — отдельной кнопки
/// сохранения нет, поэтому изменение темы переживает перезапуск (ТЗ п. 36).
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settings;
    private readonly IProjectService _projects;
    private readonly IThemeService _theme;
    private readonly INavigationService _navigation;
    private readonly IFileDialogService _dialogs;
    private readonly ICultureService _culture;

    private bool _applying;
    private bool _pendingApply;
    private bool _loading;
    private TaskCompletionSource? _applied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProjectSettings))]
    private bool _hasProject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    private ThemeOption _selectedTheme = null!;
    private LanguageOption _selectedLanguage = null!;
    private FontOption _selectedFont = null!;
    private decimal _fontSize;
    private bool _autoSaveEnabled;
    private decimal _autoSaveIntervalSeconds;
    private bool _backupsEnabled;

    public SettingsViewModel(
        IAppSettingsService settings,
        IProjectService projects,
        IThemeService theme,
        INavigationService navigation,
        IFileDialogService dialogs,
        ICultureService culture)
    {
        _settings = settings;
        _projects = projects;
        _theme = theme;
        _navigation = navigation;
        _dialogs = dialogs;
        _culture = culture;

        // Список тем синхронизирован с переключателем в углу редактора (макет).
        Themes =
        [
            new ThemeOption(AppTheme.Studio, "Studio — тёмная"),
            new ThemeOption(AppTheme.Paper, "Paper — основная (кремовая)"),
            new ThemeOption(AppTheme.Graphite, "Graphite — тёмная"),
            new ThemeOption(AppTheme.Ocean, "Ocean — холодная"),
            new ThemeOption(AppTheme.Forest, "Forest — глубокая"),
        ];

        Languages =
        [
            new LanguageOption("ru-RU", "Русский (ru-RU)"),
            new LanguageOption("en-US", "English (en-US)"),
        ];

        Fonts =
        [
            new FontOption("Inter", "Inter"),
            new FontOption("Segoe UI", "Segoe UI"),
            new FontOption("Georgia", "Georgia"),
            new FontOption("Times New Roman", "Times New Roman"),
            new FontOption("Consolas", "Consolas"),
        ];

        // Экран живёт как singleton: список значений перечитывается при каждом входе,
        // потому что проект мог быть открыт или закрыт между посещениями.
        _navigation.Navigated += (_, screen) =>
        {
            if (screen == AppScreen.Settings)
            {
                Load();
            }
        };

        Load();
    }

    public IReadOnlyList<ThemeOption> Themes { get; }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public IReadOnlyList<FontOption> Fonts { get; }

    public decimal MinFontSize => (decimal)ProjectSettings.MinFontSize;

    public decimal MaxFontSize => (decimal)ProjectSettings.MaxFontSize;

    public decimal MinAutoSaveIntervalSeconds => ProjectSettings.MinAutoSaveIntervalSeconds;

    public decimal MaxAutoSaveIntervalSeconds => ProjectSettings.MaxAutoSaveIntervalSeconds;

    public bool CanEditProjectSettings => HasProject;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>
    /// Применение, запущенное последним изменением на экране. Нужен хостам и тестам,
    /// которым важно дождаться записи; в UI изменение применяется без ожидания.
    /// </summary>
    public Task? PendingApply { get; private set; }

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                ApplyOnChange();
            }
        }
    }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value))
            {
                ApplyOnChange();
            }
        }
    }

    public FontOption SelectedFont
    {
        get => _selectedFont;
        set
        {
            if (SetProperty(ref _selectedFont, value))
            {
                ApplyOnChange();
            }
        }
    }

    public decimal FontSize
    {
        get => _fontSize;
        set
        {
            if (SetProperty(ref _fontSize, value))
            {
                ApplyOnChange();
            }
        }
    }

    public bool AutoSaveEnabled
    {
        get => _autoSaveEnabled;
        set
        {
            if (SetProperty(ref _autoSaveEnabled, value))
            {
                ApplyOnChange();
            }
        }
    }

    public decimal AutoSaveIntervalSeconds
    {
        get => _autoSaveIntervalSeconds;
        set
        {
            if (SetProperty(ref _autoSaveIntervalSeconds, value))
            {
                ApplyOnChange();
            }
        }
    }

    public bool BackupsEnabled
    {
        get => _backupsEnabled;
        set
        {
            if (SetProperty(ref _backupsEnabled, value))
            {
                ApplyOnChange();
            }
        }
    }

    /// <summary>Возврат на предыдущий экран (ТЗ п. 27).</summary>
    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    /// <summary>Применить и сохранить текущие значения (вызывается при любом изменении).</summary>
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (_applying)
        {
            // Изменение пришло во время записи: после неё значения применяются ещё раз,
            // иначе последний выбор пользователя потерялся бы. Ждём завершения активного
            // цикла, чтобы вызывающий (хост или тест) увидел уже применённое состояние.
            _pendingApply = true;
            if (_applied is { } active)
            {
                await active.Task;
            }

            return;
        }

        _applying = true;
        _applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            do
            {
                _pendingApply = false;
                await ApplyCoreAsync(cancellationToken);
            }
            while (_pendingApply);
        }
        finally
        {
            _applying = false;
            _applied.SetResult();
            _applied = null;
        }
    }

    private async Task ApplyCoreAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;

        var theme = SelectedTheme?.Value ?? AppTheme.System;
        var language = SelectedLanguage?.Value ?? "ru-RU";

        var saved = await _settings.UpdateAsync(
            preferences =>
            {
                preferences.Theme = theme;
                preferences.Language = language;
            },
            cancellationToken);

        if (!saved.IsSuccess)
        {
            // Ошибку показываем один раз за действие пользователя, а не на каждой попытке.
            _pendingApply = false;
            await ReportAsync(saved.Error!.UserMessage);
            return;
        }

        _theme.Apply(theme);
        _culture.Apply(language);

        var project = _projects.Current;
        if (project is not null)
        {
            var updated = await _projects.UpdateSettingsAsync(
                new UpdateProjectSettingsRequest
                {
                    Theme = theme,
                    Language = language,
                    FontFamily = SelectedFont.Value,
                    FontSize = (double)FontSize,
                    AutoSaveEnabled = AutoSaveEnabled,
                    AutoSaveIntervalSeconds = (int)AutoSaveIntervalSeconds,
                    BackupsEnabled = BackupsEnabled,
                },
                cancellationToken);

            if (!updated.IsSuccess)
            {
                _pendingApply = false;
                await ReportAsync(updated.Error!.UserMessage);
                return;
            }
        }

        StatusMessage = project is null
            ? "Настройки приложения сохранены. Откройте проект, чтобы изменить шрифт и автосохранение."
            : "Настройки сохранены.";
    }

    /// <summary>Изменение на экране применяется и сохраняется сразу (ТЗ п. 27, 36).</summary>
    private void ApplyOnChange()
    {
        if (_loading)
        {
            return;
        }

        PendingApply = ApplySafelyAsync();
    }

    /// <summary>
    /// Запуск применения без ожидания: экран не должен падать из-за фоновой записи,
    /// поэтому ожидаемые сбои ввода-вывода и состояния показываются как ошибка экрана.
    /// </summary>
    private async Task ApplySafelyAsync()
    {
        try
        {
            await ApplyAsync();
        }
        catch (IOException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (UnauthorizedAccessException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    /// <summary>Значения из модели: экран не хранит собственную копию настроек.</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            var preferences = _settings.Current;
            var projectSettings = _projects.Current?.Settings;

            SelectedTheme = Themes.FirstOrDefault(option => option.Value == preferences.Theme) ?? Themes[0];
            SelectedLanguage = Languages.FirstOrDefault(option => option.Value == preferences.Language) ?? Languages[0];

            var fontFamily = projectSettings?.FontFamily ?? "Inter";
            SelectedFont = Fonts.FirstOrDefault(option => string.Equals(option.Value, fontFamily, StringComparison.OrdinalIgnoreCase))
                ?? new FontOption(fontFamily, fontFamily);

            FontSize = Math.Clamp(
                (decimal)(projectSettings?.FontSize ?? 16d),
                MinFontSize,
                MaxFontSize);

            AutoSaveEnabled = projectSettings?.AutoSaveEnabled ?? true;
            AutoSaveIntervalSeconds = Math.Clamp(
                projectSettings?.AutoSaveIntervalSeconds ?? ProjectSettings.DefaultAutoSaveIntervalSeconds,
                ProjectSettings.MinAutoSaveIntervalSeconds,
                ProjectSettings.MaxAutoSaveIntervalSeconds);

            BackupsEnabled = projectSettings?.BackupsEnabled ?? true;
            HasProject = projectSettings is not null;
            StatusMessage = null;
            ErrorMessage = null;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ReportAsync(string message)
    {
        ErrorMessage = message;
        await _dialogs.ShowErrorAsync("Настройки", message);
    }
}
