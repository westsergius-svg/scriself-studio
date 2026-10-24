namespace ScriSelfStudio.Core.Updates;

/// <summary>
/// Итог проверки обновлений: найденная сборка либо текст ошибки канала.
/// </summary>
/// <param name="Latest">Сборка в канале; <c>null</c>, если канал пуст или недоступен.</param>
/// <param name="Error">Причина сбоя проверки; <c>null</c>, если проверка прошла.</param>
public sealed record UpdateCheckResult(UpdateInfo? Latest, string? Error)
{
    /// <summary>Канал недоступен.</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Канал ответил, но сборок в нём нет.</summary>
    public bool IsEmpty => Latest is null && !HasError;

    public static UpdateCheckResult Found(UpdateInfo latest) => new(latest, null);

    public static UpdateCheckResult Empty() => new(null, null);

    public static UpdateCheckResult Failed(string error) => new(null, error);
}
