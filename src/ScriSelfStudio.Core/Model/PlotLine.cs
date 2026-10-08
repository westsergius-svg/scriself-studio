namespace ScriSelfStudio.Core.Model;

using System.Text.Json.Serialization;

/// <summary>Сюжетная линия проекта (ТЗ п. 5, 27). Реализуется на этапе 8.</summary>
public sealed class PlotLine : ProjectEntity
{
    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Короткая подпись линии в списке (макет: подпись пункта).</summary>
    public string? Description { get; set; }

    /// <summary>Цель линии: чего линия добивается в книге (макет: «Цель»).</summary>
    public string? Goal { get; set; }

    /// <summary>Стадия готовности линии (макет: «Статус»).</summary>
    public PlotLineStatus Status { get; set; } = PlotLineStatus.InProgress;

    /// <summary>Свободные заметки автора (макет: «Заметки»).</summary>
    public string? Notes { get; set; }

    public string? ColorHex { get; set; }

    /// <summary>Упорядоченные этапы линии (ТЗ п. 27; ADR 010).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    [JsonPropertyName("stages")]
    public List<PlotLineStep> Steps { get; } = [];

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<Guid> SceneIds { get; } = [];
}
