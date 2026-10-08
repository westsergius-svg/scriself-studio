namespace ScriSelfStudio.Core.Events;

/// <summary>
/// Синхронная шина событий в памяти (ТЗ п. 25). Подписка возвращает токен:
/// отписка обязательна, иначе подписчик не освобождается (ТЗ п. 43).
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Subscription>> _subscriptions = [];

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new Subscription(message => handler((TEvent)message), typeof(TEvent), Unsubscribe);
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(typeof(TEvent), out var list))
            {
                list = [];
                _subscriptions[typeof(TEvent)] = list;
            }

            list.Add(subscription);
        }

        return subscription;
    }

    public void Publish<TEvent>(TEvent message)
    {
        Subscription[] snapshot;
        lock (_gate)
        {
            snapshot = _subscriptions.TryGetValue(typeof(TEvent), out var list)
                ? [.. list]
                : [];
        }

        foreach (var subscription in snapshot)
        {
            subscription.Invoke(message!);
        }
    }

    private void Unsubscribe(Type eventType, Subscription subscription)
    {
        lock (_gate)
        {
            if (_subscriptions.TryGetValue(eventType, out var list))
            {
                list.Remove(subscription);
                if (list.Count == 0)
                {
                    _subscriptions.Remove(eventType);
                }
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action<object> _invoke;
        private readonly Type _eventType;
        private readonly Action<Type, Subscription> _unsubscribe;
        private bool _disposed;

        public Subscription(Action<object> invoke, Type eventType, Action<Type, Subscription> unsubscribe)
        {
            _invoke = invoke;
            _eventType = eventType;
            _unsubscribe = unsubscribe;
        }

        public void Invoke(object message) => _invoke(message);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _unsubscribe(_eventType, this);
        }
    }
}
