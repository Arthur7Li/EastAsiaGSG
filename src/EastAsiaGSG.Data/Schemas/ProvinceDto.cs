namespace EastAsiaGSG.Data.Schemas;

/// <summary>
/// Data Transfer Object representing a province definition and its raw adjacency edges deserialized from JSON.
/// </summary>
public sealed class ProvinceDto
{
    public ushort ProvinceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ushort StateId { get; set; }
    public string? OwnerTag { get; set; }
    public string? ControllerTag { get; set; }
    public string Terrain { get; set; } = "Plains";
    public byte StrategicRegionId { get; set; }
    public byte SupplyHubLevel { get; set; }
    public byte PortLevel { get; set; }
    public byte InfrastructureLevel { get; set; }
    public short CenterX { get; set; }
    public short CenterY { get; set; }
    public List<ProvinceAdjacencyDto> Adjacencies { get; set; } = [];
}

/// <summary>
/// Adjacency edge target and flag descriptors.
/// </summary>
public sealed class ProvinceAdjacencyDto
{
    public ushort TargetProvinceId { get; set; }
    public string Flags { get; set; } = "Standard";
}
