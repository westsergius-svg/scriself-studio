namespace ScriSelfStudio.Core.Model;

using System.Text.Json.Serialization;

/// <summary>
/// Пользовательские метаданные сцены (ТЗ п. 10: поле Metadata).
/// Служебные отметки времени хранятся отдельно в <see cref="ProjectEntity.Audit"/>.
/// </summary>
public sealed class SceneMetadata
{
    /// <summary>Ключевая сцена книги: на карте помечается отдельно (ТЗ п. 27).</summary>
    public bool IsKey { get; set; }

    /// <summary>
    /// Сцена заблокирована от правок: на карте помечается замком (макет «Карта книги»).
    /// Отметка авторская и живёт в метаданных, схема файла не меняется.
    /// </summary>
    public bool IsLocked { get; set; }

    public string? PointOfView { get; set; }

    public string? Location { get; set; }

    public string? StoryTime { get; set; }

    public string? Notes { get; set; }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<string> Tags { get; } = [];
}
