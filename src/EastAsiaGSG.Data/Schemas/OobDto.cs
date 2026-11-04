namespace EastAsiaGSG.Data.Schemas;

/// <summary>
/// Root DTO for a nation's 1985 Order of Battle JSON file (/data/oob_1985/{TAG}_oob.json).
/// Matches docs/DATA_SCHEMAS.md Section 3.
/// </summary>
public sealed class OobDto
{
    public string CountryTag { get; set; } = string.Empty;
    public List<DivisionTemplateDto> Templates { get; set; } = [];
    public List<StartingDivisionDto> StartingDivisions { get; set; } = [];
}

/// <summary>
/// DTO for a single division template within an Order of Battle JSON file.
/// </summary>
public sealed class DivisionTemplateDto
{
    public ushort TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte CombatWidth { get; set; } = 20;
    public List<string> Battalions { get; set; } = [];
    public List<string> SupportCompanies { get; set; } = [];
}

/// <summary>
/// DTO for a starting field division spawned at 1985-01-01 00:00.
/// </summary>
public sealed class StartingDivisionDto
{
    public string Name { get; set; } = string.Empty;
    public ushort TemplateId { get; set; }
    public ushort StartingProvinceId { get; set; }
    public float StartingExperience { get; set; } = 0.5f;
    public float StartingStrength { get; set; } = 1.0f;
    public bool PgmAuthorized { get; set; }
}
