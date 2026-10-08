namespace ScriSelfStudio.Core;

/// <summary>Источник времени. Позволяет проверять поведение, зависящее от времени.</summary>
public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
