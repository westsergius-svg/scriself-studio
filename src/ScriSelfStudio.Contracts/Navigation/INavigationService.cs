namespace ScriSelfStudio.Contracts.Navigation;

using ScriSelfStudio.Contracts.Errors;

/// <summary>Экраны приложения (ТЗ п. 27, 35).</summary>
public enum AppScreen
{
    Launcher = 0,
    Architecture = 1,
    Editor = 2,
    Timeline = 3,
    Settings = 4,
    Characters = 5,
    Export = 6,
}

/// <summary>Навигация между экранами. Экраны подключаются модулями (ТЗ п. 34).</summary>
public interface INavigationService
{
    AppScreen Current { get; }

    event EventHandler<AppScreen>? Navigated;

    /// <summary>Зарегистрирован ли экран в текущем профиле сборки.</summary>
    bool CanNavigateTo(AppScreen screen);

    Result NavigateTo(AppScreen screen, object? parameter = null);

    Result GoBack();
}
