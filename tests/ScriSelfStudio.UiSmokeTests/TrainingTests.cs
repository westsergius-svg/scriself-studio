namespace ScriSelfStudio.UiSmokeTests;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation.Help;
using ScriSelfStudio.Presentation.Theme;

public sealed class TrainingTests
{
    [Fact]
    public void Lessons_cover_the_workflow_and_navigation_stays_inside_the_course()
    {
        var model = new TrainingViewModel();
        Assert.Equal(12, model.Slides.Count);
        Assert.Equal(model.Slides.Count, model.Slides.Select(s => s.Id).Distinct().Count());
        Assert.Contains(model.Slides, s => s.Id == "versions");
        Assert.Contains(model.Slides, s => s.Id == "changes");
        Assert.Contains(model.Slides, s => s.Id == "comments");
        Assert.Contains(model.Slides, s => s.Id == "grammar" && s.Body.Contains("частично", StringComparison.Ordinal));
        Assert.All(model.Slides, s => { Assert.False(string.IsNullOrWhiteSpace(s.Practice)); Assert.False(string.IsNullOrWhiteSpace(s.Screen)); });
        Assert.False(model.PreviousCommand.CanExecute(null));
        model.SelectedIndex = int.MaxValue;
        Assert.Equal("12 / 12", model.Position);
        Assert.False(model.NextCommand.CanExecute(null));
        model.RestartCommand.Execute(null);
        Assert.Equal("1 / 12", model.Position);
        model.SelectedIndex = -1;
        Assert.Equal(0, model.SelectedIndex);
    }

    [Fact]
    public void Training_is_discoverable_in_help_and_uses_a_separate_view_request()
    {
        var model = new HelpViewModel { SearchQuery = "обучение" };
        Assert.Contains(model.FilteredTopics, t => t.Id == "training");
        var requests = 0;
        model.TrainingRequested += (_, _) => requests++;
        model.OpenTrainingCommand.Execute(null);
        Assert.Equal(1, requests);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Studio)]
    [InlineData(AppTheme.Starter)]
    [InlineData(AppTheme.GreenStart)]
    [InlineData(AppTheme.Cabinet)]
    [InlineData(AppTheme.Jam)]
    public void Every_slide_renders_at_compact_size_and_navigation_works(AppTheme theme)
    {
        var themes = new AvaloniaThemeService();
        themes.Apply(theme);
        var window = new TrainingWindow { Width = 800, Height = 600 };
        var model = (TrainingViewModel)window.DataContext!;
        window.Show();
        try
        {
            var picture = window.FindControl<Image>("TrainingSlideImage")!;
            for (var i = 0; i < model.Slides.Count; i++)
            {
                model.SelectedIndex = i;
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(picture.Source);
                Assert.True(picture.Bounds.Width > 300);
                Assert.True(picture.Bounds.Height > 200);
            }
            var next = window.FindControl<Button>("TrainingNextButton")!;
            Assert.False(next.IsEffectivelyEnabled);
            model.RestartCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            next.Focus();
            window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
            Assert.Equal(1, model.SelectedIndex);
            window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
            Assert.Equal(0, model.SelectedIndex);
            var picker = window.FindControl<ComboBox>("TrainingLessonPicker")!;
            picker.SelectedIndex = 9;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("grammar", model.CurrentSlide.Id);
            window.FindControl<Expander>("TrainingTranscript")!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Русские запятые", model.CurrentSlide.Transcript, StringComparison.Ordinal);
        }
        finally { window.Close(); themes.Apply(AppTheme.Studio); }
    }
}
