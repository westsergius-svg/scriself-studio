namespace ScriSelfStudio.Core.Model;

using System.Text.Json.Serialization;

/// <summary>Глава внутри книги (ТЗ п. 10).</summary>
public sealed class Chapter : ProjectEntity
{
    public Guid BookId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<Scene> Scenes { get; } = [];
}
