namespace ScriSelfStudio.Core.Model;

/// <summary>
/// Служебные отметки времени и признак мягкого удаления (ТЗ п. 10).
/// </summary>
public sealed class EntityAudit
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
