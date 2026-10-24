namespace ScriSelfStudio.StoryUI;

using Avalonia.Controls;
using Avalonia.Interactivity;

/// <summary>
/// Экран персонажей. Логика — в <see cref="CharactersViewModel"/>.
/// Поля карточки сохраняются при потере фокуса: текст не теряется при
/// переключении персонажа или уходе на другую вкладку без нажатия «Сохранить».
/// </summary>
public partial class CharactersView : UserControl
{
    public CharactersView()
    {
        InitializeComponent();
        AddHandler(LostFocusEvent, OnFieldLostFocus, RoutingStrategies.Bubble);
    }

    private void OnFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox or ComboBox && DataContext is CharactersViewModel viewModel)
        {
            _ = viewModel.CommitDraftAsync();
        }
    }
}
