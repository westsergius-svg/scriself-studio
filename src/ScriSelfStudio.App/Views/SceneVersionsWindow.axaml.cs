namespace ScriSelfStudio.App.Views;

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.EditorUI;

public sealed partial class SceneVersionsWindow : Window
{
    private SceneVersionsViewModel? _model;

    public SceneVersionsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        Closing += (_, args) => { if (_model?.IsBusy == true) args.Cancel = true; };
        Closed += (_, _) => { if (_model is not null) _model.PropertyChanged -= OnChanged; };
    }

    private void Observe()
    {
        if (_model is not null) _model.PropertyChanged -= OnChanged;
        _model = DataContext as SceneVersionsViewModel;
        if (_model is null) return;
        _model.PropertyChanged += OnChanged;
        RefreshDocuments();
        RefreshDiff();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SceneVersionsViewModel.CurrentDocument) or nameof(SceneVersionsViewModel.SavedDocument)) RefreshDocuments();
        if (args.PropertyName == nameof(SceneVersionsViewModel.Difference)) RefreshDiff();
    }

    private void RefreshDocuments()
    {
        if (_model is null) return;
        SavedVersionPreview.ShowDocument(_model.SavedDocument);
        CurrentVersionPreview.ShowDocument(_model.CurrentDocument);
    }

    private void RefreshDiff()
    {
        if (_model is null) return;
        VersionDiffText.Inlines!.Clear();
        foreach (var part in _model.Difference.Parts)
        {
            var run = new Run(part.Text);
            if (part.Kind == SceneDiffKind.Added)
            {
                run.Foreground = new SolidColorBrush(Color.Parse("#17673F"));
                run.TextDecorations = TextDecorations.Underline;
            }
            else if (part.Kind == SceneDiffKind.Removed)
            {
                run.Foreground = new SolidColorBrush(Color.Parse("#A02D32"));
                run.TextDecorations = TextDecorations.Strikethrough;
            }
            VersionDiffText.Inlines.Add(run);
        }
    }
}
