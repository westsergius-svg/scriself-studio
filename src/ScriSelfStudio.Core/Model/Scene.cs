using ScriSelfStudio.Core.Text;

namespace ScriSelfStudio.Core.Model;

/// <summary>Сцена внутри главы: единица редактирования текста (ТЗ п. 10, 29).</summary>
public sealed class Scene : ProjectEntity
{
    public Guid ChapterId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public SceneStatus Status { get; set; } = SceneStatus.Idea;

    /// <summary>
    /// Объём текста в словах: пересчитывается при загрузке проекта и при каждой
    /// правке текста. На диск не пишется — это производное значение документа,
    /// поэтому схема файла не меняется.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Words { get; set; }

    /// <summary>
    /// Ревизия текста сцены. Увеличивается при каждом изменении документа и служит
    /// основой для защиты от перезаписи чужих правок (ТЗ п. 22, 23).
    /// </summary>
    public long Revision { get; set; }

    public RichDocument Document { get; set; } = RichDocument.CreateEmpty();

    public SceneMetadata Metadata { get; set; } = new();

    [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
    public List<SceneVersion> Versions { get; } = [];
}
