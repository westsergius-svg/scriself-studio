namespace ScriSelfStudio.Core.Model;

using System.Text.Json.Serialization;

/// <summary>Серия книг внутри проекта (ТЗ п. 10).</summary>
public sealed class Series : ProjectEntity
{
    public Guid ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<Book> Books { get; } = [];
}
