namespace ScriSelfStudio.TestSupport;

using ScriSelfStudio.Core;

/// <summary>Управляемые часы: поведение, зависящее от времени, должно быть проверяемым.</summary>
public sealed class FakeClock : ISystemClock
{
    public FakeClock(DateTimeOffset? start = null) =>
        UtcNow = start ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
