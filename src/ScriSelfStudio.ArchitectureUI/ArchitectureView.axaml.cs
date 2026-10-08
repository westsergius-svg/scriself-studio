namespace ScriSelfStudio.ArchitectureUI;

using Avalonia.Controls;

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
}
