namespace ScriSelfStudio.App.Shell;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.StoryUI;

/// <summary>Вкладки Архитектора: карта книги, персонажи и сюжетные линии (ТЗ п. 27).</summary>
public enum ArchitectureTab
{
    Map = 0,
    Characters = 1,
    Lines = 2,
}

/// <summary>
/// Архитектор — один из трёх главных экранов (Лаунчер, Редактор, Архитектор).
/// Держит три вкладки и показывает соответствующий дочерний экран.
/// Переключение вкладки идёт через навигацию, чтобы активный экран оболочки
/// (и связанные с ним панели) оставался согласованным.
/// </summary>
public sealed partial class ArchitectureHostViewModel : ViewModelBase
{
    private readonly ArchitectureViewModel _map;
    private readonly CharactersViewModel _characters;
    private readonly TimelineViewModel _lines;
    private readonly INavigationService _navigation;

    public ArchitectureHostViewModel(
        ArchitectureViewModel map,
        CharactersViewModel characters,
        TimelineViewModel lines,
        INavigationService navigation)
    {
        _map = map;
        _characters = characters;
        _lines = lines;
        _navigation = navigation;
        _current = map;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMapSelected))]
    [NotifyPropertyChangedFor(nameof(IsCharactersSelected))]
    [NotifyPropertyChangedFor(nameof(IsLinesSelected))]
    private ArchitectureTab _tab;

    [ObservableProperty]
    private object? _current;

    public bool IsMapSelected => Tab == ArchitectureTab.Map;

    public bool IsCharactersSelected => Tab == ArchitectureTab.Characters;

    public bool IsLinesSelected => Tab == ArchitectureTab.Lines;

    /// <summary>Показывает вкладку без навигации: вызывается оболочкой при переходе.</summary>
    public void Show(ArchitectureTab tab)
    {
        Tab = tab;
        Current = tab switch
        {
            ArchitectureTab.Characters => _characters,
            ArchitectureTab.Lines => _lines,
            _ => _map,
        };
    }

    [RelayCommand]
    private void OpenMap() => _navigation.NavigateTo(AppScreen.Architecture);

    [RelayCommand]
    private void OpenCharacters() => _navigation.NavigateTo(AppScreen.Characters);

    [RelayCommand]
    private void OpenLines() => _navigation.NavigateTo(AppScreen.Timeline);
}