using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.TestSupport;

namespace ScriSelfStudio.UiSmokeTests;

/// <summary>Экран настроек: тема, шрифт, автосохранение, резервные копии (ТЗ п. 27, 36).</summary>
public sealed class SettingsViewTests
{
    [AvaloniaFact]
    public void Settings_screen_opens_from_the_launcher_without_hollow_buttons()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root, new StubFileDialogService());
        var navigation = provider.GetRequiredService<INavigationService>();
        Assert.True(navigation.CanNavigateTo(AppScreen.Settings));

        var launcher = provider.GetRequiredService<LauncherViewModel>();
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        launcher.OpenSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AppScreen.Settings, navigation.Current);
        var settingsView = window.GetVisualDescendants().OfType<SettingsView>().FirstOrDefault();
        Assert.NotNull(settingsView);

        // Кнопки внутри шаблонов (раскрытие ComboBox, счётчики NumericUpDown) генерируются
        // контролами и команд не имеют: проверяем только объявленные в разметке экрана.
        var buttons = settingsView!.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.TemplatedParent is null && button is not ToggleButton)
            .ToList();
        Assert.Single(buttons);
        Assert.All(buttons, button => Assert.NotNull(button.Command));

        // Состав экрана: язык, шрифт, размер, автосохранение, резервные копии (ТЗ п. 27).
        Assert.Equal(2, settingsView.GetVisualDescendants().OfType<ComboBox>().Count());
        Assert.Equal(2, settingsView.GetVisualDescendants().OfType<NumericUpDown>().Count());
        Assert.Equal(5, settingsView.GetVisualDescendants().OfType<CheckBox>().Count());

        window.Close();
    }

    [AvaloniaFact]
    public async Task Standard_theme_is_the_only_available_option()
    {
        using var workspace = new TempWorkspace();
        using (var provider = ServiceProviderFactory.Create(workspace.Root, new StubFileDialogService()))
        {
            var launcher = provider.GetRequiredService<LauncherViewModel>();
            await launcher.InitializeAsync();

            var settings = provider.GetRequiredService<SettingsViewModel>();
            settings.Load();
            Assert.Single(settings.Themes);
            Assert.Equal(AppTheme.Starter, settings.Themes[0].Value);
            Assert.Equal("Стандартная", settings.SelectedTheme.Title);
        }

        // Новый запуск с тем же каталогом настроек: тема прочитана из файла (ТЗ п. 36).
        using var restarted = ServiceProviderFactory.Create(workspace.Root, new StubFileDialogService());
        var restartedLauncher = restarted.GetRequiredService<LauncherViewModel>();
        await restartedLauncher.InitializeAsync();
        var restartedSettings = restarted.GetRequiredService<SettingsViewModel>();
        restartedSettings.Load();

        Assert.Single(restartedSettings.Themes);
        Assert.Equal(AppTheme.Starter, restartedSettings.SelectedTheme.Value);
    }

    [AvaloniaFact]
    public async Task Changing_a_value_saves_it_without_an_explicit_apply()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var dialogs = new StubFileDialogService { CreatePath = path };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var projects = provider.GetRequiredService<IProjectService>();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Load();

        settings.FontSize = 24m;
        await settings.PendingApply!;

        Assert.False(settings.HasError, settings.ErrorMessage);
        Assert.Equal(24d, projects.Current!.Settings.FontSize);

        // Значение уже на диске: повторное открытие возвращает тот же размер.
        Assert.True((await projects.CloseAsync(discardChanges: true)).IsSuccess);
        var reopened = await projects.OpenAsync(path);
        Assert.True(reopened.IsSuccess, reopened.Error?.TechnicalDetails);
        Assert.Equal(24d, reopened.Require().Settings.FontSize);
    }

    [AvaloniaFact]
    public async Task Project_settings_are_saved_and_reach_the_editor()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var dialogs = new StubFileDialogService { CreatePath = path };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Load();
        Assert.True(settings.CanEditProjectSettings);

        settings.SelectedFont = settings.Fonts.Single(option => option.Value == "Georgia");
        settings.FontSize = 22m;
        settings.AutoSaveIntervalSeconds = 30m;
        settings.AutoSaveEnabled = false;

        var editor = provider.GetRequiredService<EditorViewModel>();
        await settings.PendingApply!;

        Assert.False(settings.HasError);
        var projects = provider.GetRequiredService<IProjectService>();
        Assert.Equal("Georgia", projects.Current!.Settings.FontFamily);
        Assert.Equal(22d, projects.Current!.Settings.FontSize);
        Assert.Equal(30, projects.Current!.Settings.AutoSaveIntervalSeconds);
        Assert.False(projects.Current!.Settings.AutoSaveEnabled);

        // Шрифт доходит до редактора сразу после сохранения настроек.
        Assert.Equal("Georgia", editor.EditorFontFamily);
        Assert.Equal(22d, editor.EditorFontSize);
    }

    [AvaloniaFact]
    public async Task Settings_screen_reports_invalid_values_instead_of_writing_them()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var dialogs = new StubFileDialogService { CreatePath = path };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Load();
        settings.FontSize = 500m;

        await settings.PendingApply!;

        Assert.True(settings.HasError);
        Assert.Single(dialogs.Errors);
        var projects = provider.GetRequiredService<IProjectService>();
        Assert.Equal(16d, projects.Current!.Settings.FontSize);
    }

    [Fact]
    public void Search_filters_settings_sections_and_recovers_after_no_matches()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root, new StubFileDialogService());
        var settings = provider.GetRequiredService<SettingsViewModel>();

        settings.SearchQuery = "тема";

        var section = Assert.Single(settings.VisibleSections);
        Assert.Equal(SettingsSection.Appearance, section.Section);
        Assert.Same(section, settings.SelectedSection);

        settings.SearchQuery = "нет такой настройки";

        Assert.Empty(settings.VisibleSections);
        Assert.False(settings.HasVisibleSections);
        Assert.Null(settings.SelectedSection);

        settings.SearchQuery = string.Empty;

        Assert.Equal(4, settings.VisibleSections.Count);
        Assert.True(settings.HasVisibleSections);
        Assert.Equal(SettingsSection.General, settings.SelectedSection!.Section);
    }

    [AvaloniaFact]
    public async Task Last_project_opens_only_when_preference_is_enabled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Последний.scri");
        var dialogs = new StubFileDialogService { CreatePath = projectPath };
        using (var provider = ServiceProviderFactory.Create(workspace.Root, dialogs))
        {
            var launcher = provider.GetRequiredService<LauncherViewModel>();
            await launcher.InitializeAsync(cancellationToken);
            await launcher.CreateProjectCommand.ExecuteAsync(null);
            Assert.NotNull(provider.GetRequiredService<IProjectService>().Current);
            Assert.True((await provider.GetRequiredService<IProjectService>()
                .CloseAsync(discardChanges: true, cancellationToken)).IsSuccess);

            var preferences = provider.GetRequiredService<IAppSettingsService>();
            Assert.True((await preferences.UpdateAsync(
                value => value.OpenLastProjectOnStartup = true,
                cancellationToken)).IsSuccess);
        }

        using var restarted = ServiceProviderFactory.Create(workspace.Root, new StubFileDialogService());
        var restartedLauncher = restarted.GetRequiredService<LauncherViewModel>();
        await restartedLauncher.InitializeAsync(cancellationToken);
        Assert.Null(restarted.GetRequiredService<IProjectService>().Current);

        await restartedLauncher.OpenLastProjectAsync(cancellationToken);

        Assert.Equal(projectPath, restarted.GetRequiredService<IProjectService>().Current!.FilePath);
    }

    [AvaloniaFact]
    public Task Recovery_prompt_is_shown_when_enabled() =>
        VerifyRecoveryPromptPreferenceAsync(checkRecovery: true, expectPrompt: true);

    [AvaloniaFact]
    public Task Recovery_prompt_is_skipped_when_disabled() =>
        VerifyRecoveryPromptPreferenceAsync(checkRecovery: false, expectPrompt: false);

    private static async Task VerifyRecoveryPromptPreferenceAsync(bool checkRecovery, bool expectPrompt)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Восстановление.scri");
        var dialogs = new StubFileDialogService { CreatePath = projectPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);

        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync(cancellationToken);
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var store = provider.GetRequiredService<IProjectStore>();
        var loaded = await store.LoadAsync(projectPath, cancellationToken);
        Assert.True(loaded.IsSuccess, loaded.Error?.TechnicalDetails);
        var recoveryStore = provider.GetRequiredService<IProjectRecoveryStore>();
        Assert.True((await recoveryStore.WriteAsync(loaded.Require().Project, projectPath, cancellationToken)).IsSuccess);

        var preferences = provider.GetRequiredService<IAppSettingsService>();
        Assert.True((await preferences.UpdateAsync(
            value => value.CheckRecoveryOnStartup = checkRecovery,
            cancellationToken)).IsSuccess);
        Assert.True((await provider.GetRequiredService<IProjectService>()
            .CloseAsync(discardChanges: true, cancellationToken)).IsSuccess);

        await launcher.OpenProjectFromCommandLineAsync(projectPath, cancellationToken);

        Assert.Equal(expectPrompt, dialogs.Confirmations.Any(message => message.StartsWith("Найдено восстановление:", StringComparison.Ordinal)));
        var recovery = await recoveryStore.FindAsync(projectPath, cancellationToken);
        Assert.True(recovery.IsSuccess);
        Assert.Equal(!expectPrompt, recovery.Require() is not null);
    }

}
