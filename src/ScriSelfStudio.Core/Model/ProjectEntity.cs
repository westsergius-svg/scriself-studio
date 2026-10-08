namespace ScriSelfStudio.Core.Model;

/// <summary>
/// Базовый элемент структуры проекта: идентификатор, порядок сортировки, служебные отметки.
/// Связи между элементами хранятся только по идентификаторам (ТЗ п. 10).
/// </summary>
public abstract class ProjectEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Идентификатор сущности в файле-источнике (например, <c>chapter_001</c> в старом
    /// формате). Нужен, чтобы при сохранении в исходном формате сохранить связи и поля,
    /// которых нет в нашей модели. <c>null</c> — сущность создана у нас.
    /// </summary>
    public string? SourceId { get; set; }

    public int Order { get; set; }

    public EntityAudit Audit { get; set; } = new();

    public bool IsDeleted => Audit.IsDeleted;

    public void Touch(DateTimeOffset now)
    {
        Audit.UpdatedAt = now;
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        Audit.IsDeleted = true;
        Audit.DeletedAt = now;
        Audit.UpdatedAt = now;
    }

    public void RestoreFromDeleted(DateTimeOffset now)
    {
        Audit.IsDeleted = false;
        Audit.DeletedAt = null;
        Audit.UpdatedAt = now;
    }
}
