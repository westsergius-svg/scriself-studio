namespace ScriSelfStudio.App.Views;

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using ScriSelfStudio.Presentation.Help;

public sealed partial class HelpWindow : Window
{
    private HelpViewModel? _viewModel;
    private TrainingWindow? _trainingWindow;

    public HelpWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveViewModel();
        Closed += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnTopicChanged;
                _viewModel.TrainingRequested -= OnTrainingRequested;
            }
            _trainingWindow?.Close();
        };
    }

    private void ObserveViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnTopicChanged;
            _viewModel.TrainingRequested -= OnTrainingRequested;
        }

        _viewModel = DataContext as HelpViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnTopicChanged;
            _viewModel.TrainingRequested += OnTrainingRequested;
        }
    }

    private void OnTrainingRequested(object? sender, EventArgs e)
    {
        if (_trainingWindow is null)
        {
            _trainingWindow = new TrainingWindow();
            _trainingWindow.Closed += (_, _) => _trainingWindow = null;
            _trainingWindow.Show(this);
        }
        if (_trainingWindow.WindowState == WindowState.Minimized) _trainingWindow.WindowState = WindowState.Normal;
        _trainingWindow.Activate();
    }

    private void OnTopicChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HelpViewModel.SelectedTopic) or nameof(HelpViewModel.SelectedLicense))
        {
            HelpArticleScroll.Offset = new Vector(0, 0);
        }
    }
}
