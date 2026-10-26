using System.Text.Json.Serialization;

namespace EastAsiaGSG.Data.Schemas;

/// <summary>
/// Source-generated JsonSerializerContext for zero-reflection, AOT-compatible deserialization of map data.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(StateDto))]
[JsonSerializable(typeof(StateDto[]))]
[JsonSerializable(typeof(List<StateDto>))]
[JsonSerializable(typeof(ProvinceDto))]
[JsonSerializable(typeof(ProvinceDto[]))]
[JsonSerializable(typeof(List<ProvinceDto>))]
[JsonSerializable(typeof(ProvinceAdjacencyDto))]
[JsonSerializable(typeof(ResourcesDto))]
public partial class MapJsonContext : JsonSerializerContext
{
}
