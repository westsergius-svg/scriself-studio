namespace ScriSelfStudio.Core.Model;

using System.Text.Json.Serialization;

using ScriSelfStudio.Core.Schema;

/// <summary>
/// Корневой агрегат проекта. Содержит серии, книги, главы и сцены (ТЗ п. 10, 29).
/// </summary>
public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int SchemaVersion { get; set; } = Schema.SchemaVersion.Current;

    public EntityAudit Audit { get; set; } = new();

    public ProjectSettings Settings { get; set; } = new();

    /// <summary>
    /// Формат файла, из которого проект открыт. <c>null</c> — собственный формат приложения;
    /// иначе проект сохраняется в том же формате, чтобы его читали старые приложения (ТЗ п. 5).
    /// </summary>
    public string? SourceFormat { get; set; }

    /// <summary>
    /// Исходный документ формата-источника (JSON) для сохранения полей, которых нет
    /// в нашей модели. Пусто для собственного формата.
    /// </summary>
    public string? SourceDocumentJson { get; set; }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<Series> Series { get; } = [];

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<PlotLine> PlotLines { get; } = [];

    /// <summary>Персонажи книги с карточками (ТЗ п. 27; ADR 010).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<Character> Characters { get; } = [];

    /// <summary>Локации проекта (места действия).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<Location> Locations { get; } = [];

    /// <summary>Значимые предметы истории.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<StoryObject> StoryObjects { get; } = [];

    public Character? FindCharacter(Guid id) => Characters.FirstOrDefault(character => character.Id == id);

    public Location? FindLocation(Guid id) => Locations.FirstOrDefault(location => location.Id == id);

    public StoryObject? FindStoryObject(Guid id) => StoryObjects.FirstOrDefault(item => item.Id == id);

    public PlotLine? FindPlotLine(Guid id) => PlotLines.FirstOrDefault(line => line.Id == id);

    public PlotLineStep? FindPlotLineStep(Guid id) => PlotLines
        .SelectMany(line => line.Steps)
        .FirstOrDefault(step => step.Id == id);
}
