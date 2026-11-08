namespace ScriSelfStudio.Launcher;

using System.Collections.ObjectModel;
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

public sealed record NavigationOption(AppNavigationMode Value, string Title);

/// <summary>Вариант шрифта для списка (ТЗ п. 27).</summary>
public sealed record FontOption(string Value, string Title);

public enum SettingsSection
{
    General,
    Appearance,
    Editor,
    Saving,
}

public sealed record SettingsSectionOption(
    SettingsSection Section,
    string Title,
    string Description,
    string SearchTerms);

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
    [NotifyPropertyChangedFor(nameof(CanEditAutoSaveInterval))]
    private bool _hasProject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    private ThemeOption _selectedTheme = null!;
    private LanguageOption _selectedLanguage = null!;
    private NavigationOption _selectedNavigation = null!;
    private bool _navigationChoiceChanged;
    private FontOption _selectedFont = null!;
    private SettingsSectionOption? _selectedSection;
    private string? _searchQuery;
    private decimal _fontSize;
    private bool _autoSaveEnabled;
    private decimal _autoSaveIntervalSeconds;
    private bool _backupsEnabled;
    private bool _openLastProjectOnStartup;
    private bool _checkRecoveryOnStartup;
    private bool _checkForUpdatesOnStartup;
    private bool _shareUsageStatistics;
    private bool _statisticsChoiceChanged;
    private bool _spellCheckEnabled;
    private bool _grammarCheckEnabled;
    private LanguageOption _selectedGrammarLanguage = new("auto", Loc.T("Русский / английский автоматически"));
    private bool _autoCapitalizeSentences;

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

        // Выбор темы сохраняется в пользовательских настройках.
        Themes =
        [
            new ThemeOption(AppTheme.Starter, Loc.T("Стандартная")),
            new ThemeOption(AppTheme.GreenStart, Loc.T("Зелёный старт")),
            new ThemeOption(AppTheme.Jam, Loc.T("Джем")),
            new ThemeOption(AppTheme.Cabinet, Loc.T("Кабинет")),
            new ThemeOption(AppTheme.Studio, Loc.T("Мокрый асфальт")),
        ];

        Languages =
        [
            new LanguageOption("ru-RU", "Русский (ru-RU)"),
            new LanguageOption("en-US", "English (en-US)"),
            new LanguageOption("zh-CN", "简体中文"),
        ];

        Fonts =
        [
            new FontOption("Inter", "Inter"),
            new FontOption("Segoe UI", "Segoe UI"),
            new FontOption("Georgia", "Georgia"),
            new FontOption("Times New Roman", "Times New Roman"),
            new FontOption("Consolas", "Consolas"),
        ];

        RebuildSections();

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

    public IReadOnlyList<ThemeOption> Themes { get; private set; } = [];

    public IReadOnlyList<LanguageOption> Languages { get; }

    public IReadOnlyList<NavigationOption> NavigationOptions { get; private set; } = [];

    public IReadOnlyList<FontOption> Fonts { get; }

    public IReadOnlyList<SettingsSectionOption> Sections { get; private set; } = [];

    public ObservableCollection<SettingsSectionOption> VisibleSections { get; } = [];

    public SettingsSectionOption? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(SelectedSectionTitle));
                OnPropertyChanged(nameof(IsGeneralSection));
                OnPropertyChanged(nameof(IsAppearanceSection));
                OnPropertyChanged(nameof(IsEditorSection));
                OnPropertyChanged(nameof(IsSavingSection));
            }
        }
    }

    public string SelectedSectionTitle => SelectedSection?.Title ?? string.Empty;

    public string? SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                RefreshVisibleSections();
            }
        }
    }

    public bool IsGeneralSection => SelectedSection?.Section == SettingsSection.General;

    public bool IsAppearanceSection => SelectedSection?.Section == SettingsSection.Appearance;

    public bool IsEditorSection => SelectedSection?.Section == SettingsSection.Editor;

    public bool IsSavingSection => SelectedSection?.Section == SettingsSection.Saving;

    public bool HasVisibleSections => VisibleSections.Count > 0;

    public decimal MinFontSize => (decimal)ProjectSettings.MinFontSize;

    public decimal MaxFontSize => (decimal)ProjectSettings.MaxFontSize;

    public decimal MinAutoSaveIntervalSeconds => ProjectSettings.MinAutoSaveIntervalSeconds;

    public decimal MaxAutoSaveIntervalSeconds => ProjectSettings.MaxAutoSaveIntervalSeconds;

    public bool CanEditProjectSettings => HasProject;

    public bool CanEditAutoSaveInterval => CanEditProjectSettings && AutoSaveEnabled;

    public string FontSizeLabel => Loc.F("Размер текста: {0} pt", FontSize);

    public string AutoSaveIntervalLabel => Loc.F("Интервал: {0} секунд", AutoSaveIntervalSeconds);

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
            if (value is null) return;
            if (SetProperty(ref _selectedTheme, value))
            {
                OnPropertyChanged(nameof(ThemeDescription));
                OnPropertyChanged(nameof(IsClassicNavigationAvailable));
                OnPropertyChanged(nameof(NavigationDescription));
                if (!_loading && !_settings.Current.NavigationMode.HasValue && !_navigationChoiceChanged)
                {
                    _selectedNavigation = NavigationOptions.Single(option => option.Value == DefaultNavigation(value.Value));
                    OnPropertyChanged(nameof(SelectedNavigation));
                }
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

    public NavigationOption SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (value is not null && SetProperty(ref _selectedNavigation, value))
            {
                if (!_loading) _navigationChoiceChanged = true;
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
                OnPropertyChanged(nameof(PreviewFontFamily));
                ApplyOnChange();
            }
        }
    }

    public Avalonia.Media.FontFamily PreviewFontFamily => new(SelectedFont?.Value ?? "Inter");

    public decimal FontSize
    {
        get => _fontSize;
        set
        {
            if (SetProperty(ref _fontSize, value))
            {
                OnPropertyChanged(nameof(FontSizeLabel));
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
                OnPropertyChanged(nameof(CanEditAutoSaveInterval));
                ApplyOnChange();
            }
        }
    }

    public bool SpellCheckEnabled
    {
        get => _spellCheckEnabled;
        set { if (SetProperty(ref _spellCheckEnabled, value)) ApplyOnChange(); }
    }

    public IReadOnlyList<LanguageOption> GrammarLanguages { get; private set; } = CreateGrammarLanguages();

    private static IReadOnlyList<LanguageOption> CreateGrammarLanguages() =>
    [new("auto", Loc.T("Русский / английский автоматически")), new("ru", Loc.T("Русский")), new("en-US", Loc.T("English (US)"))];

    public bool GrammarCheckEnabled
    {
        get => _grammarCheckEnabled;
        set { if (SetProperty(ref _grammarCheckEnabled, value)) ApplyOnChange(); }
    }

    public LanguageOption SelectedGrammarLanguage
    {
        get => _selectedGrammarLanguage;
        set { if (SetProperty(ref _selectedGrammarLanguage, value)) ApplyOnChange(); }
    }

    public bool AutoCapitalizeSentences
    {
        get => _autoCapitalizeSentences;
        set { if (SetProperty(ref _autoCapitalizeSentences, value)) ApplyOnChange(); }
    }

    public decimal AutoSaveIntervalSeconds
    {
        get => _autoSaveIntervalSeconds;
        set
        {
            if (SetProperty(ref _autoSaveIntervalSeconds, value))
            {
                OnPropertyChanged(nameof(AutoSaveIntervalLabel));
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

    public bool OpenLastProjectOnStartup
    {
        get => _openLastProjectOnStartup;
        set
        {
            if (SetProperty(ref _openLastProjectOnStartup, value))
            {
                ApplyOnChange();
            }
        }
    }

    public bool CheckRecoveryOnStartup
    {
        get => _checkRecoveryOnStartup;
        set
        {
            if (SetProperty(ref _checkRecoveryOnStartup, value))
            {
                ApplyOnChange();
            }
        }
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _checkForUpdatesOnStartup;
        set
        {
            if (SetProperty(ref _checkForUpdatesOnStartup, value))
            {
                ApplyOnChange();
            }
        }
    }

    public bool ShareUsageStatistics
    {
        get => _shareUsageStatistics;
        set
        {
            if (SetProperty(ref _shareUsageStatistics, value) && !_loading)
            {
                _statisticsChoiceChanged = true;
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
        var previousNavigation = _settings.Current.NavigationMode;

        var saved = await _settings.UpdateAsync(
            preferences =>
            {
                preferences.Theme = theme;
                preferences.Language = language;
                preferences.SpellCheckEnabled = SpellCheckEnabled;
                preferences.GrammarCheckEnabled = GrammarCheckEnabled;
                preferences.GrammarLanguage = SelectedGrammarLanguage.Value;
                preferences.AutoCapitalizeSentences = AutoCapitalizeSentences;
                if (_navigationChoiceChanged)
                {
                    preferences.NavigationMode = SelectedNavigation.Value;
                }
                preferences.OpenLastProjectOnStartup = OpenLastProjectOnStartup;
                preferences.CheckRecoveryOnStartup = CheckRecoveryOnStartup;
                preferences.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;
                if (_statisticsChoiceChanged)
                {
                    preferences.ShareUsageStatistics = ShareUsageStatistics;
                }
            },
            cancellationToken);

        if (!saved.IsSuccess)
        {
            // Ошибку показываем один раз за действие пользователя, а не на каждой попытке.
            _pendingApply = false;
            await ReportAsync(saved.Error!.UserMessage);
            return;
        }

        if (_theme.Current != theme || previousNavigation != _settings.Current.NavigationMode)
        {
            _theme.Apply(theme);
        }
        var languageChanged = !string.Equals(_culture.Current, language, StringComparison.OrdinalIgnoreCase);
        _culture.Apply(language);
        if (languageChanged)
        {
            RebuildSections();
        }

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
            ? Loc.T("Настройки приложения сохранены. Откройте проект, чтобы изменить шрифт и автосохранение.")
            : Loc.T("Настройки сохранены.");
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
            SpellCheckEnabled = preferences.SpellCheckEnabled;
            GrammarCheckEnabled = preferences.GrammarCheckEnabled;
            SelectedGrammarLanguage = GrammarLanguages.FirstOrDefault(l => l.Value == preferences.GrammarLanguage) ?? GrammarLanguages[0];
            AutoCapitalizeSentences = preferences.AutoCapitalizeSentences;

            SelectedTheme = Themes.FirstOrDefault(option => option.Value == preferences.Theme) ?? Themes[0];
            SelectedLanguage = Languages.FirstOrDefault(option => option.Value == preferences.Language) ?? Languages[0];
            var mode = preferences.NavigationMode ?? DefaultNavigation(preferences.Theme);
            SelectedNavigation = NavigationOptions.FirstOrDefault(option => option.Value == mode) ?? NavigationOptions[0];
            _navigationChoiceChanged = false;

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
            OpenLastProjectOnStartup = preferences.OpenLastProjectOnStartup;
            CheckRecoveryOnStartup = preferences.CheckRecoveryOnStartup;
            CheckForUpdatesOnStartup = preferences.CheckForUpdatesOnStartup;
            ShareUsageStatistics = preferences.ShareUsageStatistics == true;
            _statisticsChoiceChanged = false;
            HasProject = projectSettings is not null;
            StatusMessage = null;
            ErrorMessage = null;
        }
        finally
        {
            _loading = false;
        }
    }

    public string ThemeDescription => SelectedTheme?.Value switch
    {
        AppTheme.Studio => Loc.T("Мокрый асфальт — графитовые панели, инструменты и светлый лист рукописи."),
        AppTheme.Jam => Loc.T("Джем — светлое стекло, полупрозрачные панели и ягодный акцент."),
        AppTheme.Cabinet => Loc.T("Кабинет — зелёная палитра и компактное рабочее пространство."),
        AppTheme.GreenStart => Loc.T("Зелёный старт — привычное рабочее пространство в зелёной палитре «Кабинета»."),
        _ => Loc.T("Стандартная — привычное рабочее пространство со светлыми поверхностями и синим акцентом."),
    };

    public bool IsClassicNavigationAvailable => SelectedTheme?.Value != AppTheme.Studio;
    public string NavigationDescription => Loc.T(IsClassicNavigationAvailable
        ? "Вкладки или меню «Рабочее место» — для классических тем."
        : "В теме «Мокрый асфальт» разделы открываются на панели инструментов слева или через меню «Вид».");

    private void RefreshVisibleSections()
    {
        var selected = SelectedSection;
        var query = SearchQuery?.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? Sections
            : Sections.Where(section =>
                section.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || section.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || section.SearchTerms.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();

        VisibleSections.Clear();
        foreach (var section in matches)
        {
            VisibleSections.Add(section);
        }

        OnPropertyChanged(nameof(HasVisibleSections));
        SelectedSection = selected is not null && VisibleSections.Contains(selected)
            ? selected : VisibleSections.FirstOrDefault();
    }

    /// <summary>Пересобирает разделы после смены языка: подписи создаются заново.</summary>
    private void RebuildSections()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            var selected = SelectedSection?.Section ?? SettingsSection.General;
            Sections =
            [
                new(SettingsSection.General, Loc.T("Основные"), Loc.T("Язык, запуск и восстановление"), "региональный формат язык даты числа запуск последний проект восстановление language region date startup recovery 语言 区域 日期 启动 恢复"),
                new(SettingsSection.Appearance, Loc.T("Внешний вид"), Loc.T("Тема и оформление"), "тема цвет оформление светлая тёмная theme color appearance light dark 主题 颜色 外观 浅色 深色"),
                new(SettingsSection.Editor, Loc.T("Редактор"), Loc.T("Шрифт и проверка текста"), "шрифт размер текста рукопись орфография грамматика заглавная буква автозамена font size text manuscript spelling grammar capitalization 字体 字号 文本 拼写 语法 大写"),
                new(SettingsSection.Saving, Loc.T("Сохранение"), Loc.T("Автосохранение и копии"), "автосохранение интервал резервная копия autosave interval backup 自动保存 间隔 备份"),
            ];

            VisibleSections.Clear();
            foreach (var section in Sections)
            {
                VisibleSections.Add(section);
            }

            OnPropertyChanged(nameof(Sections));
            OnPropertyChanged(nameof(HasVisibleSections));
            // Restoring the selection also restores the visible section fields after
            // ListBox clears SelectedItem while its items are being translated.
            SelectedSection = VisibleSections.FirstOrDefault(section => section.Section == selected) ?? Sections[0];

            var themeValue = _selectedTheme?.Value ?? _settings.Current.Theme;
            Themes =
            [
                new ThemeOption(AppTheme.Starter, Loc.T("Стандартная")),
                new ThemeOption(AppTheme.GreenStart, Loc.T("Зелёный старт")),
                new ThemeOption(AppTheme.Jam, Loc.T("Джем")),
                new ThemeOption(AppTheme.Cabinet, Loc.T("Кабинет")),
                new ThemeOption(AppTheme.Studio, Loc.T("Мокрый асфальт")),
            ];
            OnPropertyChanged(nameof(Themes));
            // Replacing ItemsSource can clear ComboBox.SelectedItem through its two-way binding.
            // Restore the captured choice after that notification, without queuing another save.
            _selectedTheme = Themes.FirstOrDefault(option => option.Value == themeValue) ?? Themes[0];
            OnPropertyChanged(nameof(SelectedTheme));
            OnPropertyChanged(nameof(ThemeDescription));
            OnPropertyChanged(nameof(IsClassicNavigationAvailable));
            OnPropertyChanged(nameof(NavigationDescription));

            var navigationValue = _selectedNavigation?.Value
                ?? _settings.Current.NavigationMode ?? DefaultNavigation(themeValue);
            NavigationOptions =
            [
                new(AppNavigationMode.Tabs, Loc.T("Вкладки")),
                new(AppNavigationMode.Menu, Loc.T("Выпадающее меню")),
            ];
            OnPropertyChanged(nameof(NavigationOptions));
            _selectedNavigation = NavigationOptions.FirstOrDefault(option => option.Value == navigationValue) ?? NavigationOptions[0];
            OnPropertyChanged(nameof(SelectedNavigation));

            var grammarLanguage = _selectedGrammarLanguage?.Value ?? _settings.Current.GrammarLanguage;
            GrammarLanguages = CreateGrammarLanguages();
            OnPropertyChanged(nameof(GrammarLanguages));
            _selectedGrammarLanguage = GrammarLanguages.FirstOrDefault(option => option.Value == grammarLanguage) ?? GrammarLanguages[0];
            OnPropertyChanged(nameof(SelectedGrammarLanguage));
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private static AppNavigationMode DefaultNavigation(AppTheme theme) =>
        theme is AppTheme.Studio or AppTheme.Cabinet or AppTheme.Jam ? AppNavigationMode.Menu : AppNavigationMode.Tabs;

    private async Task ReportAsync(string message)
    {
        ErrorMessage = Loc.T(message);
        await _dialogs.ShowErrorAsync(Loc.T("Настройки"), ErrorMessage);
    }
}
