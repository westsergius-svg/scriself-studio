namespace ScriSelfStudio.App.Views;

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ScriSelfStudio.Presentation.Help;

public sealed partial class TrainingWindow : Window
{
    private readonly TrainingViewModel _model;
    private Bitmap? _image;
    private WindowState _previousWindowState;

    public TrainingWindow()
    {
        InitializeComponent();
        _model = new TrainingViewModel();
        DataContext = _model;
        _model.PropertyChanged += OnLessonChanged;
        _model.FullScreenRequested += OnFullScreenRequested;
        Closed += (_, _) =>
        {
            _model.PropertyChanged -= OnLessonChanged;
            _model.FullScreenRequested -= OnFullScreenRequested;
            TrainingSlideImage.Source = null;
            _image?.Dispose();
        };
        LoadSlide();
    }

    private void OnLessonChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrainingViewModel.CurrentSlide)) LoadSlide();
    }

    private void LoadSlide()
    {
        using var stream = AssetLoader.Open(_model.CurrentSlide.ImageUri);
        var next = new Bitmap(stream);
        TrainingSlideImage.Source = next;
        _image?.Dispose();
        _image = next;
    }

    private void OnFullScreenRequested(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.FullScreen) WindowState = _previousWindowState;
        else { _previousWindowState = WindowState; WindowState = WindowState.FullScreen; }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Escape)
        {
            if (WindowState == WindowState.FullScreen) OnFullScreenRequested(this, EventArgs.Empty);
            else Close();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F11) { OnFullScreenRequested(this, EventArgs.Empty); e.Handled = true; return; }
        if (TrainingLessonPicker.IsDropDownOpen || ReferenceEquals(FocusManager?.GetFocusedElement(), TrainingLessonPicker)) return;
        switch (e.Key)
        {
            case Key.Left: case Key.PageUp: if (_model.PreviousCommand.CanExecute(null)) _model.PreviousCommand.Execute(null); break;
            case Key.Right: case Key.PageDown: if (_model.NextCommand.CanExecute(null)) _model.NextCommand.Execute(null); break;
            case Key.Home: _model.RestartCommand.Execute(null); break;
            case Key.End: _model.SelectedIndex = _model.Slides.Count - 1; break;
            default: return;
        }
        e.Handled = true;
    }
}
