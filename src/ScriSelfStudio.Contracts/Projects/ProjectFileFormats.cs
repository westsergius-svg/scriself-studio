namespace ScriSelfStudio.Contracts.Projects;

/// <summary>
/// Идентификаторы форматов файла проекта (ТЗ п. 5). Собственный формат приложения
/// не имеет идентификатора: <c>Project.SourceFormat == null</c>.
/// </summary>
public static class ProjectFileFormats
{
    /// <summary>Старый контейнер: ZIP с <c>manifest.json</c> и <c>content.json</c>.</summary>
    public const string ZipJson = "scri-zip-v1";
}
