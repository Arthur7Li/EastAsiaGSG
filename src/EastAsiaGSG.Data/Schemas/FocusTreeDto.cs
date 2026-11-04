namespace EastAsiaGSG.Data.Schemas;

/// <summary>
/// Root DTO for deserializing a nation's Focus Tree JSON file (/data/focus_trees/{TAG}_1985.json).
/// </summary>
public sealed class FocusTreeDto
{
    public string CountryTag { get; set; } = string.Empty;
    public string Era { get; set; } = "1985_LateColdWar";
    public List<FocusNodeDto> Focuses { get; set; } = [];
}

/// <summary>
/// DTO representing a single National Focus node in JSON.
/// </summary>
public sealed class FocusNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public short GridX { get; set; }
    public short GridY { get; set; }
    public short CostDays { get; set; } = 35;
    public List<string> Prerequisites { get; set; } = [];
    public List<string> MutuallyExclusive { get; set; } = [];
    public ushort AiBaseWeight { get; set; } = 100;
    public List<FocusConditionDto> Conditions { get; set; } = [];
    public List<FocusEffectDto> Effects { get; set; } = [];
}

/// <summary>
/// DTO representing a single availability condition on a National Focus node.
/// </summary>
public sealed class FocusConditionDto
{
    public string Type { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public float Value { get; set; }
}

/// <summary>
/// DTO representing a single completion effect on a National Focus node.
/// </summary>
public sealed class FocusEffectDto
{
    public string Type { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public float Value { get; set; }
}
