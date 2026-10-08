namespace ScriSelfStudio.Presentation.Navigation;

using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Navigation;

/// <summary>
/// Навигация по зарегистрированным экранам (ТЗ п. 27, 34). Незарегистрированный
/// экран считается выключенным модулем, и переход в него запрещён.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly HashSet<AppScreen> _registered = [];
    private readonly Stack<AppScreen> _history = new();

    public AppScreen Current { get; private set; } = AppScreen.Launcher;

    public event EventHandler<AppScreen>? Navigated;

    /// <summary>Регистрация экрана модулем, который его реализует.</summary>
    public void Register(AppScreen screen) => _registered.Add(screen);

    public bool CanNavigateTo(AppScreen screen) => _registered.Contains(screen);

    public Result NavigateTo(AppScreen screen, object? parameter = null)
    {
        if (!CanNavigateTo(screen))
        {
            return Result.FromError(
                ErrorCode.NavigationFailed,
                "Экран недоступен в этой сборке.",
                "Выберите доступный экран.");
        }

        if (Current != screen)
        {
            _history.Push(Current);
        }

        Current = screen;
        Navigated?.Invoke(this, screen);
        return Result.Success;
    }

    public Result GoBack()
    {
        if (_history.Count == 0)
        {
            return Result.FromError(ErrorCode.NavigationFailed, "Возвращаться некуда.");
        }

        Current = _history.Pop();
        Navigated?.Invoke(this, Current);
        return Result.Success;
    }
}
