namespace ScriSelfStudio.Core.Model;

/// <summary>
/// Значимый предмет истории — то, что важно для сюжета (артефакт, вещь, документ).
/// В ранних файлах это сущность <c>object</c> (макет: раздел «Локации/Объекты»).
/// </summary>
public sealed class StoryObject : ProjectEntity
{
    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Notes { get; set; }
}
