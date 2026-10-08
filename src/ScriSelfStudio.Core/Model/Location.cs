namespace ScriSelfStudio.Core.Model;

/// <summary>
/// Локация проекта — место действия. Живёт рядом со структурой книги и связывается
/// со сценами по идентификатору; в ранних файлах это сущность <c>location</c>
/// (макет: раздел «Локации/Объекты»).
/// </summary>
public sealed class Location : ProjectEntity
{
    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Notes { get; set; }
}
