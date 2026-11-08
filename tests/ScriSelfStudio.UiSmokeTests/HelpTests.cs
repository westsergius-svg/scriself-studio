namespace ScriSelfStudio.UiSmokeTests;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation.Help;
using ScriSelfStudio.Presentation.Theme;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.TestSupport;

public sealed class HelpTests
{
    [AvaloniaFact]
    public void F1_and_about_commands_are_available_without_a_project_in_every_theme()
    {
        using var workspace = new TempWorkspace();
        var recorder = new RecordingHelp();
        var services = new ServiceCollection();
        services.AddScriSelfStudio(workspace.Root, useAvaloniaDialogs: false);
        services.AddSingleton<IHelpService>(recorder);
        using var provider = services.BuildServiceProvider();
        ScriSelfStudioServices.RegisterModules(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        try
        {
            var screen = shell.Current;
            foreach (var theme in new[] { AppTheme.Studio, AppTheme.Starter, AppTheme.GreenStart, AppTheme.Jam, AppTheme.Cabinet })
            {
                provider.GetRequiredService<IThemeService>().Apply(theme);
                recorder.LastTopic = null;
                window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
                Assert.Equal("start", recorder.LastTopic);
                Assert.Same(screen, shell.Current);
            }
            shell.OpenAboutCommand.Execute(null);
            Assert.Equal("about", recorder.LastTopic);
            shell.OpenLicensesCommand.Execute(null);
            Assert.Equal("licenses", recorder.LastTopic);
        }
        finally { window.Close(); }
    }

    private sealed class RecordingHelp : IHelpService
    {
        public string? LastTopic { get; set; }
        public void Show(string topicId = "start") => LastTopic = topicId;
    }

    [Fact]
    public void Search_covers_article_body_and_recovers_from_no_results()
    {
        var help = new HelpViewModel { SearchQuery = "заголовки сцен" };
        Assert.Contains(help.FilteredTopics, topic => topic.Id == "export");
        help.SearchQuery = "несуществующее-слово-987654";
        Assert.Empty(help.FilteredTopics);
        Assert.False(help.HasTopic);
        help.SelectTopicCommand.Execute("about");
        Assert.True(help.HasTopic);
        Assert.Equal("about", help.SelectedTopic!.Id);
        Assert.Equal(string.Empty, help.SearchQuery);
    }

    [Fact]
    public void Licenses_are_complete_offline_resources_with_distinct_font_terms()
    {
        var help = new HelpViewModel();
        Assert.Contains(help.Licenses, item => item.Title.Contains("ScriSelf", StringComparison.Ordinal)
            && item.Text.Contains("Permission is hereby granted", StringComparison.Ordinal)
            && item.Text.Contains("THE SOFTWARE IS PROVIDED", StringComparison.Ordinal));
        Assert.Contains(help.Licenses, item => item.Title.Contains("Inter OFL", StringComparison.Ordinal)
            && item.Text.Contains("SIL OPEN FONT LICENSE", StringComparison.Ordinal));
        Assert.Contains(help.Licenses, item => item.Title.Contains("SQLitePCLRaw Apache", StringComparison.Ordinal)
            && item.Text.Contains("END OF TERMS AND CONDITIONS", StringComparison.Ordinal));
        Assert.Contains(help.Licenses, item => item.Title.Contains("NET Runtime Notices", StringComparison.Ordinal));
        Assert.Equal("Версия 1.2.0", help.VersionText);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Studio)]
    [InlineData(AppTheme.Starter)]
    [InlineData(AppTheme.GreenStart)]
    [InlineData(AppTheme.Jam)]
    [InlineData(AppTheme.Cabinet)]
    public void Help_can_search_and_read_licenses_at_compact_size(AppTheme theme)
    {
        var themeService = new AvaloniaThemeService();
        themeService.Apply(theme);
        var model = new HelpViewModel();
        var window = new HelpWindow { DataContext = model, Width = 760, Height = 560 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var search = window.FindControl<TextBox>("HelpSearchBox")!;
            search.Text = "экспорт";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("экспорт", model.SearchQuery);
            model.SelectTopicCommand.Execute("licenses");
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<ComboBox>("HelpLicensePicker")!.IsEffectivelyVisible);
            Assert.True(window.FindControl<SelectableTextBlock>("HelpLicenseText")!.Bounds.Height > 0);
            foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("helpShortcut")))
            {
                Assert.True(button.Bounds.Width > 0);
                var position = button.TranslatePoint(default, window)!.Value;
                Assert.True(position.X + button.Bounds.Width <= window.Bounds.Width + 1);
            }
        }
        finally
        {
            window.Close();
            themeService.Apply(AppTheme.Studio);
        }
    }
}
