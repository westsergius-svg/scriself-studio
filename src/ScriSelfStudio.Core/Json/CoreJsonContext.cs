namespace ScriSelfStudio.Core.Json;

using System.Text.Json;
using System.Text.Json.Serialization;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;

/// <summary>
/// Единый источник настроек сериализации данных проекта (ТЗ п. 32: System.Text.Json).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Project))]
[JsonSerializable(typeof(Series))]
[JsonSerializable(typeof(Book))]
[JsonSerializable(typeof(Chapter))]
[JsonSerializable(typeof(Scene))]
[JsonSerializable(typeof(PlotLine))]
[JsonSerializable(typeof(PlotLineStep))]
[JsonSerializable(typeof(StepParticipant))]
[JsonSerializable(typeof(Character))]
[JsonSerializable(typeof(CharacterArcStage))]
[JsonSerializable(typeof(CharacterRelation))]
[JsonSerializable(typeof(Location))]
[JsonSerializable(typeof(StoryObject))]
[JsonSerializable(typeof(List<PlotLineStep>))]
[JsonSerializable(typeof(List<PlotLine>))]
[JsonSerializable(typeof(List<StepParticipant>))]
[JsonSerializable(typeof(List<Character>))]
[JsonSerializable(typeof(List<CharacterArcStage>))]
[JsonSerializable(typeof(List<CharacterRelation>))]
[JsonSerializable(typeof(List<Location>))]
[JsonSerializable(typeof(List<StoryObject>))]
[JsonSerializable(typeof(SpanStyle))]
[JsonSerializable(typeof(SceneMetadata))]
[JsonSerializable(typeof(ProjectSettings))]
[JsonSerializable(typeof(RichDocument))]
[JsonSerializable(typeof(List<Guid>))]
[JsonSerializable(typeof(List<string>))]
public sealed partial class CoreJsonContext : JsonSerializerContext
{
}

/// <summary>Удобный доступ к настройкам сериализации проекта.</summary>
public static class CoreJson
{
    public static CoreJsonContext Context => CoreJsonContext.Default;

    public static JsonSerializerOptions Options => CoreJsonContext.Default.Options;
}
