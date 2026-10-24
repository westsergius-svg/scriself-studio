using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation.Localization;
using ScriSelfStudio.StoryUI;
using ScriSelfStudio.TestSupport;
using ScriSelfStudio.Core.Model;

namespace ScriSelfStudio.UiSmokeTests;

/// <summary>Переключение языка меняет подписи интерфейса, а не только форматы дат.</summary>
public sealed class LocalizationTests
{
    [AvaloniaFact]
    public void Switching_to_English_translates_the_launcher_immediately()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var culture = provider.GetRequiredService<ICultureService>();
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.Contains(
                "Мои проекты",
                Texts(window));

            culture.Apply("en-US");
            Dispatcher.UIThread.RunJobs();
            var english = Texts(window);
            Assert.Contains("My projects", english);
            Assert.DoesNotContain("Мои проекты", english);

            culture.Apply("ru-RU");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Мои проекты", Texts(window));
        }
        finally
        {
            culture.Apply("ru-RU");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void English_applies_to_character_labels_and_card_fields()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var culture = provider.GetRequiredService<ICultureService>();
        culture.Apply("en-US");
        try
        {
            Assert.Equal("Protagonist", StoryLabels.RoleLabel(CharacterRole.Protagonist));
            var characters = provider.GetRequiredService<CharactersViewModel>();
            Assert.Equal("2. Appearance", characters.Sections[0].Title);
            Assert.Equal("Appearance", characters.Sections[0].Fields[0].Label);
        }
        finally
        {
            culture.Apply("ru-RU");
        }
    }

    [AvaloniaFact]
    public async Task Settings_sections_are_rebuilt_in_English_after_the_switch()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var culture = provider.GetRequiredService<ICultureService>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        launcher.OpenSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        try
        {
            var settings = provider.GetRequiredService<SettingsViewModel>();
            Assert.Equal("Основные", settings.Sections[0].Title);

            settings.SelectedLanguage = settings.Languages.Single(option => option.Value == "en-US");
            if (settings.PendingApply is { } pending)
            {
                await pending;
            }

            Dispatcher.UIThread.RunJobs();
            Assert.Equal("General", settings.Sections[0].Title);
            Assert.Equal("Standard", settings.Themes[0].Title);
        }
        finally
        {
            culture.Apply("ru-RU");
            window.Close();
        }
    }

    private static List<string?> Texts(Window window) =>
        window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .ToList();
}
