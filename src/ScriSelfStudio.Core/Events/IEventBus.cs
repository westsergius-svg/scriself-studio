namespace ScriSelfStudio.Core.Events;

/// <summary>
/// Шина событий приложения (ТЗ п. 25). Подписка возвращает токен:
/// отписываться обязательно, иначе получаются утечки подписок (ТЗ п. 43).
/// Абстракция принадлежит ядру, как и сами события (ТЗ п. 4).
/// </summary>
public interface IEventBus
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);

    void Publish<TEvent>(TEvent message);
}
