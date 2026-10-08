namespace ScriSelfStudio.Contracts.Json;

using System.Text.Json;
using System.Text.Json.Serialization;
using ScriSelfStudio.Contracts.Settings;

/// <summary>Сериализация настроек приложения (ТЗ п. 32: System.Text.Json).</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppPreferences))]
[JsonSerializable(typeof(RecentProjectEntry))]
[JsonSerializable(typeof(List<RecentProjectEntry>))]
public sealed partial class ContractsJsonContext : JsonSerializerContext
{
}

/// <summary>Доступ к настройкам сериализации контрактов.</summary>
public static class ContractsJson
{
    public static JsonSerializerOptions Options => ContractsJsonContext.Default.Options;
}
