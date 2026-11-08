namespace ScriSelfStudio.App.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ScriSelfStudio.App.Shell;

/// <summary>Главное окно приложения: заголовок и текущий экран.</summary>
public partial class MainWindow : Window
{
    // Отсчёт перетаскивания ведём в координатах рабочей сетки: она не двигается,
    // когда меняется ширина панелей. Если считать дельту в координатах самого
    // разделителя (как делает Thumb.DragDelta), разделитель «уезжает» вместе с
    // панелью, и дельта обнуляется — панель перестаёт двигаться.
    private bool _bookDragging;
    private Point _bookDragStart;
    private double _bookDragStartWidth;
    private bool _sceneDragging;
    private Point _sceneDragStart;
    private double _sceneDragStartWidth;

    /// <summary>Конструктор без параметров нужен загрузчику XAML и дизайнеру.</summary>
    public MainWindow()
    {
        InitializeComponent();
        // Subpixel smoothing assumes an opaque backing; glass uses grayscale
        // antialiasing so small labels remain clean over translucent surfaces.
        Classes.CollectionChanged += (_, _) => TextOptions.SetTextRenderingMode(this,
            Classes.Contains("jam") ? TextRenderingMode.Antialias : TextRenderingMode.Unspecified);
        FitToScreen();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        DataContextChanged += (_, _) => UpdateResponsiveLayout();
        // Thumb помечает PointerPressed обработанным, поэтому обычная подписка из
        // XAML события не получает. Регистрируем с handledEventsToo: true — иначе
        // перетаскивание разделителя вообще не начиналось бы.
        Subscribe(BookTreeSplitter, OnBookTreeSplitterPressed, OnBookTreeSplitterMoved, OnBookTreeSplitterReleased);
        Subscribe(SceneContextSplitter, OnSceneContextSplitterPressed, OnSceneContextSplitterMoved, OnSceneContextSplitterReleased);
    }

    public MainWindow(ShellViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        // Screen.WorkingArea is in physical pixels; window sizes are logical units.
        // Leave room for native borders and the title bar when restoring the window.
        var availableWidth = Math.Max(1, screen.WorkingArea.Width / screen.Scaling - 32);
        var availableHeight = Math.Max(1, screen.WorkingArea.Height / screen.Scaling - 64);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
    }

    private void UpdateResponsiveLayout()
    {
        if (DataContext is ShellViewModel shell) shell.UpdateWindowWidth(Bounds.Width > 0 ? Bounds.Width : Width);
    }

    private static void Subscribe(
        InputElement splitter,
        EventHandler<PointerPressedEventArgs> pressed,
        EventHandler<PointerEventArgs> moved,
        EventHandler<PointerReleasedEventArgs> released)
    {
        splitter.AddHandler(PointerPressedEvent, pressed, RoutingStrategies.Bubble, true);
        splitter.AddHandler(PointerMovedEvent, moved, RoutingStrategies.Bubble, true);
        splitter.AddHandler(PointerReleasedEvent, released, RoutingStrategies.Bubble, true);
    }

    /// <summary>
    /// Начало перетаскивания левой панели. Захватываем указатель и запоминаем
    /// точку старта, чтобы дальнейшие смещения считать от неё.
    /// </summary>
    private void OnBookTreeSplitterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell) return;
        _bookDragging = true;
        _bookDragStart = e.GetPosition(WorkspaceGrid);
        _bookDragStartWidth = shell.BookTreeWidth;
        e.Pointer.Capture(BookTreeSplitter);
    }

    private void OnBookTreeSplitterMoved(object? sender, PointerEventArgs e)
    {
        if (!_bookDragging || DataContext is not ShellViewModel shell) return;
        var delta = e.GetPosition(WorkspaceGrid).X - _bookDragStart.X;
        shell.SetBookTreeWidth(_bookDragStartWidth + delta);
    }

    private void OnBookTreeSplitterReleased(object? sender, PointerReleasedEventArgs e) => _bookDragging = false;

    /// <summary>Начало перетаскивания правой панели контекста сцены.</summary>
    private void OnSceneContextSplitterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell) return;
        _sceneDragging = true;
        _sceneDragStart = e.GetPosition(WorkspaceGrid);
        _sceneDragStartWidth = shell.SceneContextWidth;
        e.Pointer.Capture(SceneContextSplitter);
    }

    private void OnSceneContextSplitterMoved(object? sender, PointerEventArgs e)
    {
        if (!_sceneDragging || DataContext is not ShellViewModel shell) return;
        var delta = e.GetPosition(WorkspaceGrid).X - _sceneDragStart.X;
        shell.SetSceneContextWidth(_sceneDragStartWidth - delta);
    }

    private void OnSceneContextSplitterReleased(object? sender, PointerReleasedEventArgs e) => _sceneDragging = false;
}
