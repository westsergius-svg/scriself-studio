namespace ScriSelfStudio.ArchitectureUI;

using Avalonia.Controls;
using Avalonia.Interactivity;
using ScriSelfStudio.ArchitectureUI.Map;

/// <summary>Экран структуры книги. Логика — в ArchitectureViewModel.</summary>
public partial class ArchitectureView : UserControl
{
    private GridLength? _expandedDetailsWidth;

    public ArchitectureView()
    {
        InitializeComponent();

        MapScenePanel.PropertyChanged += (_, change) =>
        {
            if (change.Property != IsVisibleProperty) return;
            var details = MapWorkspace.ColumnDefinitions[2];
            if (!MapScenePanel.IsVisible)
            {
                _expandedDetailsWidth = details.Width;
                details.MinWidth = 0;
                details.Width = new GridLength(0);
                MapWorkspace.ColumnDefinitions[1].Width = new GridLength(0);
            }
            else if (_expandedDetailsWidth is { } width)
            {
                details.MinWidth = 270;
                details.Width = width;
                MapWorkspace.ColumnDefinitions[1].Width = new GridLength(6);
                _expandedDetailsWidth = null;
            }
        };

        // Ширина холста нужна модели карты: от неё зависит ширина столбцов (ТЗ п. 27).
        MapScroll.SizeChanged += (_, size) =>
        {
            if (DataContext is ArchitectureViewModel viewModel)
            {
                viewModel.MapViewportWidth = size.NewSize.Width;
            }
        };
    }

    private async void OnSceneDescriptionLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchitectureViewModel
            && sender is TextBox { DataContext: SceneDetailsViewModel details })
        {
            await details.SaveDescriptionCommand.ExecuteAsync(null);
        }
    }

    private async void OnSceneNotesLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchitectureViewModel
            && sender is TextBox { DataContext: SceneDetailsViewModel details })
        {
            await details.SaveNotesCommand.ExecuteAsync(null);
        }
    }
}
