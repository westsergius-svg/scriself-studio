namespace ScriSelfStudio.Core.Model;

using System.Text.Json.Serialization;

/// <summary>Книга внутри серии (ТЗ п. 10).</summary>
public sealed class Book : ProjectEntity
{
    public Guid ProjectId { get; set; }

    public Guid SeriesId { get; set; }

    public string Title { get; set; } = string.Empty;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<Chapter> Chapters { get; } = [];
}
