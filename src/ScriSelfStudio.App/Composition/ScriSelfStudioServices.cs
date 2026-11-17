namespace ScriSelfStudio.App.Composition;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScriSelfStudio.App.Services;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.BookArchitecture;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Cloud;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Updates;
using ScriSelfStudio.EditorEngine;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Persistence.FileSystem;
using ScriSelfStudio.Persistence.Cloud;
using ScriSelfStudio.Persistence.Formats;
using ScriSelfStudio.Persistence.Projects;
using ScriSelfStudio.Persistence.Sqlite;
using ScriSelfStudio.Persistence.Language;
using ScriSelfStudio.Presentation.Dialogs;
using ScriSelfStudio.Presentation.Export;
using ScriSelfStudio.Presentation.Localization;
using ScriSelfStudio.Presentation.Navigation;
using ScriSelfStudio.Presentation.Theme;
using ScriSelfStudio.Presentation.Statistics;
using ScriSelfStudio.StoryUI;

/// <summary>
/// Единственное место регистрации конкретных реализаций (ТЗ п. 33):
/// UI-модули не знают про Persistence, ядро не знает про UI.
/// </summary>
public static class ScriSelfStudioServices
{
    public static IServiceCollection AddScriSelfStudio(
        this IServiceCollection services,
        string? settingsDirectory = null,
        bool useAvaloniaDialogs = true)
    {
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddDebug()
            .AddConsole());

        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IEventBus, EventBus>();

        // Хранилище выбирается по формату файла: свой SQLite или старый ZIP-контейнер (ТЗ п. 5).
        services.AddSingleton<SqliteProjectStore>();
        services.AddSingleton<ScriZipProjectStore>();
        services.AddSingleton<IProjectStore, RoutingProjectStore>();
        services.AddSingleton<IProjectRecoveryStore, FileProjectRecoveryStore>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IBookArchitectureService, BookArchitectureService>();
        services.AddSingleton<IEditorEngine, SceneEditorEngine>();
        services.AddSingleton<ITextToolsService, TextToolsService>();
        services.AddSingleton<TextToolsViewModel>();
        services.AddSingleton<IGrammarService>(_ => new LanguageToolService());
        services.AddSingleton<GrammarViewModel>();
        services.AddSingleton<IAppSettingsService>(_ => new JsonAppSettingsService(settingsDirectory));

        // Облако автора: Яндекс.Диск через WebDAV и Google Drive через OAuth.
        services.AddSingleton<YandexDiskWebDavService>();
        services.AddSingleton<GoogleDriveService>();
        services.AddSingleton<ICloudStorageCatalog, CloudStorageCatalog>();

        // Обновление с официального канала Scriborium: тот же адрес, что у 1.1.3.3S.
        services.AddSingleton<IUpdateService>(_ => new ScriboriumUpdateService());
        services.AddSingleton<UsageStatisticsService>();

        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton<ICultureService, CultureService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IHelpService, AvaloniaHelpService>();
        services.AddSingleton<ISceneVersionsDialogService, AvaloniaSceneVersionsDialogService>();

        if (useAvaloniaDialogs)
        {
            services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
        }
        else
        {
            services.AddSingleton<IFileDialogService, NullFileDialogService>();
        }

        services.AddSingleton<LauncherViewModel>();
        services.AddSingleton<ArchitectureViewModel>();
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<CharactersViewModel>();
        services.AddSingleton<TimelineViewModel>();
        services.AddSingleton<ExportViewModel>();
        services.AddSingleton<BookTreeViewModel>();
        services.AddSingleton<SceneContextViewModel>();
        services.AddSingleton<ArchitectureHostViewModel>();
        services.AddSingleton<IWorkspaceSaveService, WorkspaceSaveService>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }

    /// <summary>Собирает контейнер и включает зарегистрированные модули экранов.</summary>
    public static ServiceProvider Build(string? settingsDirectory = null, bool useAvaloniaDialogs = true)
    {
        var services = new ServiceCollection();
        services.AddScriSelfStudio(settingsDirectory, useAvaloniaDialogs);
        var provider = services.BuildServiceProvider();
        RegisterModules(provider);
        return provider;
    }

    /// <summary>
    /// Включает экраны текущего профиля сборки (ТЗ п. 34). Публичный шаг нужен
    /// хостингам, которые собирают контейнер сами (например, UI-тесты).
    /// </summary>
    public static void RegisterModules(IServiceProvider provider)
    {
        // Профиль сборки: экраны, реализованные и подключённые в App (ТЗ п. 34).
        var navigation = (NavigationService)provider.GetRequiredService<INavigationService>();
        navigation.Register(AppScreen.Launcher);
        navigation.Register(AppScreen.Architecture);
        navigation.Register(AppScreen.Editor);
        navigation.Register(AppScreen.Settings);
        navigation.Register(AppScreen.Characters);
        navigation.Register(AppScreen.Timeline);
        navigation.Register(AppScreen.Export);
    }
}
