namespace ScriSelfStudio.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.TestSupport;

/// <summary>Настройки проекта и приложения (ТЗ п. 27, 36).</summary>
public sealed class SettingsContractTests
{
    [Fact]
    public async Task Project_settings_are_saved_by_the_service_and_survive_reopen()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Настройки.scri");

        using (var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var projects = provider.GetRequiredService<IProjectService>();
            var created = await projects.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Настройки" });
            Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);

            var updated = await projects.UpdateSettingsAsync(Request(
                fontFamily: "Georgia",
                fontSize: 20d,
                interval: 120,
                theme: AppTheme.Dark,
                autoSave: false));

            Assert.True(updated.IsSuccess, updated.Error?.TechnicalDetails);
            Assert.False(projects.IsDirty);
            Assert.Equal("Georgia", projects.Current!.Settings.FontFamily);
        }

        using var reopened = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var service = reopened.GetRequiredService<IProjectService>();
        var snapshot = await service.OpenAsync(path);
        Assert.True(snapshot.IsSuccess, snapshot.Error?.TechnicalDetails);

        var settings = snapshot.Require().Settings;
        Assert.Equal(AppTheme.Dark, settings.Theme);
        Assert.Equal("Georgia", settings.FontFamily);
        Assert.Equal(20d, settings.FontSize);
        Assert.False(settings.AutoSaveEnabled);
        Assert.Equal(120, settings.AutoSaveIntervalSeconds);
    }

    [Theory]
    [InlineData("Georgia", 100d, 60, "Размер шрифта")]
    [InlineData("Georgia", 16d, 1, "Интервал автосохранения")]
    [InlineData("   ", 16d, 60, "название шрифта")]
    public async Task Invalid_settings_are_reported_before_anything_is_written(string fontFamily, double fontSize, int interval, string expected)
    {
        using var workspace = new TempWorkspace();
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();
        var created = await projects.CreateAsync(new CreateProjectRequest
        {
            FilePath = workspace.FilePath("Проект.scri"),
            Name = "Проект",
        });
        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);

        var result = await projects.UpdateSettingsAsync(Request(fontFamily, fontSize, interval));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.Error!.Code);
        Assert.Contains(expected, result.Error.UserMessage, StringComparison.Ordinal);

        // Настройки не применились целиком: прежнее значение шрифта сохранилось.
        Assert.Equal(ProjectSettings.DefaultAutoSaveIntervalSeconds, projects.Current!.Settings.AutoSaveIntervalSeconds);
    }

    [Fact]
    public async Task Backups_flag_controls_backup_creation()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Копии.scri");
        var backup = path + ".bak";
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();
        var created = await projects.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Копии" });
        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);

        var withoutBackups = await projects.UpdateSettingsAsync(Request(backupsEnabled: false));
        Assert.True(withoutBackups.IsSuccess, withoutBackups.Error?.TechnicalDetails);
        Assert.True((await projects.SaveAsync()).IsSuccess);
        Assert.False(File.Exists(backup));

        var withBackups = await projects.UpdateSettingsAsync(Request(backupsEnabled: true));
        Assert.True(withBackups.IsSuccess, withBackups.Error?.TechnicalDetails);
        Assert.True((await projects.SaveAsync()).IsSuccess);
        Assert.True(File.Exists(backup));
    }

    [Fact]
    public async Task Settings_update_requires_an_open_project()
    {
        using var workspace = new TempWorkspace();
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var projects = provider.GetRequiredService<IProjectService>();

        var result = await projects.UpdateSettingsAsync(Request());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ProjectNotOpen, result.Error!.Code);
    }

    [Fact]
    public async Task App_preferences_update_is_written_to_the_settings_file()
    {
        using var workspace = new TempWorkspace();
        using (var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var settings = provider.GetRequiredService<IAppSettingsService>();
            Assert.True((await settings.LoadAsync()).IsSuccess);

            var saved = await settings.UpdateAsync(preferences =>
            {
                preferences.Theme = AppTheme.Light;
                preferences.Language = "en-US";
            });

            Assert.True(saved.IsSuccess, saved.Error?.TechnicalDetails);
        }

        using var restarted = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var reloaded = restarted.GetRequiredService<IAppSettingsService>();
        Assert.True((await reloaded.LoadAsync()).IsSuccess);
        Assert.Equal(AppTheme.Light, reloaded.Current.Theme);
        Assert.Equal("en-US", reloaded.Current.Language);
    }

    [Fact]
    public async Task Recent_projects_are_persisted_and_reloaded_with_summary()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Последний.scri");

        using (var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false))
        {
            var settings = provider.GetRequiredService<IAppSettingsService>();
            Assert.True((await settings.LoadAsync()).IsSuccess);

            var added = await settings.AddRecentProjectAsync(new RecentProjectEntry
            {
                FilePath = path,
                Name = "Последний",
                LastOpenedAt = DateTimeOffset.UtcNow,
                Description = "Роман в трёх частях.",
                BookCount = 1,
                ChapterCount = 4,
                SceneCount = 9,
                CharacterCount = 6,
                PlotLineCount = 2,
            });

            Assert.True(added.IsSuccess, added.Error?.TechnicalDetails);
        }

        // Список последних проектов — коллекция только для чтения, из-за чего
        // генератор System.Text.Json не заполнял её при загрузке. Регрессия:
        // после перезапуска карточка проекта должна восстановиться со сводкой.
        using var restarted = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var reloaded = restarted.GetRequiredService<IAppSettingsService>();
        Assert.True((await reloaded.LoadAsync()).IsSuccess);

        var entry = Assert.Single(reloaded.Current.RecentProjects);
        Assert.Equal("Последний", entry.Name);
        Assert.Equal(path, entry.FilePath);
        Assert.Equal(4, entry.ChapterCount);
        Assert.Equal(9, entry.SceneCount);
        Assert.Equal("Роман в трёх частях.", entry.Description);
    }

    private static UpdateProjectSettingsRequest Request(
        string fontFamily = "Inter",
        double fontSize = 16d,
        int interval = ProjectSettings.DefaultAutoSaveIntervalSeconds,
        AppTheme theme = AppTheme.System,
        bool autoSave = true,
        bool backupsEnabled = true) => new()
        {
            Theme = theme,
            FontFamily = fontFamily,
            FontSize = fontSize,
            AutoSaveEnabled = autoSave,
            AutoSaveIntervalSeconds = interval,
            BackupsEnabled = backupsEnabled,
            Language = "ru-RU",
        };
}
