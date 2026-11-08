namespace ScriSelfStudio.Core.Schema;

/// <summary>
/// Версия схемы файла проекта (ТЗ п. 5, 13). Несовместимые версии требуют миграции.
/// </summary>
public static class SchemaVersion
{
    /// <summary>Версия схемы, которую пишет текущая сборка.</summary>
    public const int Current = 6;

    /// <summary>Самая старая версия схемы, для которой существует миграция.</summary>
    public const int MinimumSupported = 1;

    public static bool IsSupported(int version) => version >= MinimumSupported && version <= Current;

    public static bool IsFuture(int version) => version > Current;

    public static bool IsTooOld(int version) => version < MinimumSupported;
}
