namespace ScriSelfStudio.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation.Navigation;
using ScriSelfStudio.Presentation.Localization;
using ScriSelfStudio.Presentation.Theme;
using ScriSelfStudio.Presentation.Statistics;
using ScriSelfStudio.StoryUI;
using ScriSelfStudio.TestSupport;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void All_required_services_are_resolvable()
    {
        using var workspace = new TempWorkspace();
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);

        Assert.NotNull(provider.GetRequiredService<IProjectService>());
        Assert.NotNull(provider.GetRequiredService<IBookArchitectureService>());
        Assert.NotNull(provider.GetRequiredService<IEditorEngine>());
        Assert.NotNull(provider.GetRequiredService<IProjectStore>());
        Assert.NotNull(provider.GetRequiredService<IProjectRecoveryStore>());
        Assert.NotNull(provider.GetRequiredService<IAppSettingsService>());
        Assert.Same(provider.GetRequiredService<UsageStatisticsService>(), provider.GetRequiredService<UsageStatisticsService>());
        Assert.NotNull(provider.GetRequiredService<INavigationService>());
        Assert.NotNull(provider.GetRequiredService<IEventBus>());
        Assert.NotNull(provider.GetRequiredService<ISystemClock>());
        Assert.NotNull(provider.GetRequiredService<IThemeService>());
        Assert.NotNull(provider.GetRequiredService<IFileDialogService>());
        Assert.NotNull(provider.GetRequiredService<LauncherViewModel>());
        Assert.NotNull(provider.GetRequiredService<SettingsViewModel>());
        Assert.NotNull(provider.GetRequiredService<IThemeService>());
        Assert.NotNull(provider.GetRequiredService<ICultureService>());
        Assert.NotNull(provider.GetRequiredService<ShellViewModel>());
        Assert.NotNull(provider.GetRequiredService<CharactersViewModel>());
        Assert.NotNull(provider.GetRequiredService<TimelineViewModel>());
    }

    [Fact]
    public void Only_implemented_screens_are_registered()
    {
        using var workspace = new TempWorkspace();
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);
        var navigation = provider.GetRequiredService<INavigationService>();

        Assert.True(navigation.CanNavigateTo(AppScreen.Launcher));
        Assert.True(navigation.CanNavigateTo(AppScreen.Editor));
        Assert.True(navigation.CanNavigateTo(AppScreen.Architecture));
        Assert.True(navigation.CanNavigateTo(AppScreen.Timeline));
        Assert.True(navigation.CanNavigateTo(AppScreen.Characters));
        Assert.True(navigation.CanNavigateTo(AppScreen.Settings));
    }

    [Fact]
    public void Theme_service_applies_saved_preference()
    {
        using var workspace = new TempWorkspace();
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);

        var theme = provider.GetRequiredService<IThemeService>();
        theme.Apply(Core.Model.AppTheme.Dark);

        Assert.Equal(Core.Model.AppTheme.Dark, theme.Current);
    }

    [Fact]
    public void Navigation_service_is_registered_as_singleton_with_launcher()
    {
        using var workspace = new TempWorkspace();
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);

        var navigation = (NavigationService)provider.GetRequiredService<INavigationService>();

        Assert.Same(navigation, provider.GetRequiredService<INavigationService>());
        Assert.Equal(AppScreen.Launcher, navigation.Current);
    }
}
