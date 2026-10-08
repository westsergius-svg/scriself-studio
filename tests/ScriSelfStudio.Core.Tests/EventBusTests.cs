namespace ScriSelfStudio.Core.Tests;

using ScriSelfStudio.Core.Events;

public sealed class EventBusTests
{
    private sealed record TestEvent(int Value);

    [Fact]
    public void Publish_delivers_to_subscribers()
    {
        var bus = new EventBus();
        var received = new List<int>();
        using var subscription = bus.Subscribe<TestEvent>(message => received.Add(message.Value));

        bus.Publish(new TestEvent(1));
        bus.Publish(new TestEvent(2));

        Assert.Equal([1, 2], received);
    }

    [Fact]
    public void Disposed_subscription_stops_delivery()
    {
        var bus = new EventBus();
        var received = 0;
        var subscription = bus.Subscribe<TestEvent>(_ => received++);

        bus.Publish(new TestEvent(1));
        subscription.Dispose();
        bus.Publish(new TestEvent(2));

        Assert.Equal(1, received);
    }

    [Fact]
    public void Subscribers_of_other_event_types_are_not_called()
    {
        var bus = new EventBus();
        var otherReceived = false;
        using var subscription = bus.Subscribe<string>(_ => otherReceived = true);

        bus.Publish(new TestEvent(5));

        Assert.False(otherReceived);
    }
}
