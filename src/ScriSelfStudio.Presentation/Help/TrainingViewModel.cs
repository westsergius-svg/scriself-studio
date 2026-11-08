namespace ScriSelfStudio.Presentation.Help;

using System.Text.Json;
using CommunityToolkit.Mvvm.Input;

public sealed record TrainingSlide(int Number, string Id, string Title, string Body, string Practice, string Screen, string AssetFile)
{
    public string Caption => $"{Number:00}  {Title}";
    public Uri ImageUri => new($"avares://ScriSelfStudio.Presentation/Help/Training/Slides/{AssetFile}");
    public string Transcript => $"{Title}\n\n{Body}\n\nПопробуйте сами\n{Practice}";
}

/// <summary>Offline lessons describe the app without accessing or changing an open book.</summary>
public sealed partial class TrainingViewModel : ViewModelBase
{
    private int _selectedIndex;

    public TrainingViewModel()
    {
        using var stream = typeof(TrainingViewModel).Assembly.GetManifestResourceStream("ScriSelfStudio.Help.training.json")
            ?? throw new InvalidOperationException("Не найдено встроенное обучение.");
        Slides = JsonSerializer.Deserialize<TrainingSlide[]>(stream)
            ?? throw new InvalidOperationException("Обучение не содержит уроков.");
        if (Slides.Count == 0) throw new InvalidOperationException("Обучение не содержит уроков.");
    }

    public IReadOnlyList<TrainingSlide> Slides { get; }
    public TrainingSlide CurrentSlide => Slides[SelectedIndex];
    public string Position => $"{SelectedIndex + 1} / {Slides.Count}";
    public bool CanGoBack => SelectedIndex > 0;
    public bool CanGoForward => SelectedIndex < Slides.Count - 1;
    public event EventHandler? FullScreenRequested;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (!SetProperty(ref _selectedIndex, Math.Clamp(value, 0, Slides.Count - 1))) return;
            OnPropertyChanged(nameof(CurrentSlide));
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            PreviousCommand.NotifyCanExecuteChanged();
            NextCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Previous() => SelectedIndex--;
    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Next() => SelectedIndex++;
    [RelayCommand]
    private void Restart() => SelectedIndex = 0;
    [RelayCommand]
    private void ToggleFullScreen() => FullScreenRequested?.Invoke(this, EventArgs.Empty);
}
