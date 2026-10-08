namespace ScriSelfStudio.App;

using System.IO;

/// <summary>
/// Разбор аргументов командной строки приложения. Путь к проекту можно передать
/// при запуске (например, из проводника Windows) — ТЗ п. 27, 31.
/// </summary>
public static class StartupCommandLine
{
    public const string ProjectExtension = ".scri";

    /// <summary>
    /// Возвращает полный путь к проекту, переданному аргументом, либо <c>null</c>.
    /// Аргументы, начинающиеся с «-», принадлежат Avalonia и игнорируются.
    /// </summary>
    public static string? ResolveProjectPath(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (var argument in args)
        {
            if (string.IsNullOrWhiteSpace(argument) || argument[0] == '-')
            {
                continue;
            }

            if (!Path.GetExtension(argument).Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                return Path.GetFullPath(argument);
            }
            catch (ArgumentException)
            {
                // Путь непригоден для нормализации: отдаём как есть, чтобы служба проекта
                // вернула понятную ошибку вместо падения на старте.
                return argument;
            }
        }

        return null;
    }
}
