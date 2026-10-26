namespace EastAsiaGSG.Data.Schemas;

/// <summary>
/// Data Transfer Object representing a strategic state definition deserialized from JSON.
/// </summary>
public sealed class StateDto
{
    public ushort StateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerTag { get; set; } = string.Empty;
    public string? ControllerTag { get; set; }
    public List<string> CoreTags { get; set; } = [];
    public List<ushort> Provinces { get; set; } = [];
    public int Population { get; set; }
    public byte CivilianFactories { get; set; }
    public byte MilitaryFactories { get; set; }
    public byte NavalDockyards { get; set; }
    public byte AirbaseLevel { get; set; }
    public byte RadarSamLevel { get; set; }
    public byte InfrastructureLevel { get; set; }
    public ResourcesDto? Resources { get; set; }
}

/// <summary>
/// Strategic resource yields for a state.
/// </summary>
public sealed class ResourcesDto
{
    public ushort Oil { get; set; }
    public ushort Steel { get; set; }
    public ushort Aluminum { get; set; }
    public ushort Semiconductors { get; set; }
}
