namespace ScriSelfStudio.ArchitectureUI;

using Avalonia.Controls;
using Avalonia.Interactivity;
using ScriSelfStudio.ArchitectureUI.Map;

/// <summary>Экран структуры книги. Логика — в ArchitectureViewModel.</summary>
public partial class ArchitectureView : UserControl
{
    public ArchitectureView()
    {
        InitializeComponent();

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
