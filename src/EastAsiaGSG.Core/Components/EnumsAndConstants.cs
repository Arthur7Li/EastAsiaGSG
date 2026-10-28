namespace EastAsiaGSG.Core.Components;

/// <summary>
/// The 8 military equipment archetypes across all division templates and production lines.
/// </summary>
public enum EquipmentType : byte
{
    InfantryKit = 0,
    SupportVehicles = 1,
    IFV = 2,
    MBT = 3,
    Artillery = 4,
    AttackHeli = 5,
    TacticalAir = 6,
    PGM = 7
}

/// <summary>
/// Generational technology tiers (1970 Reserve through 1995 Digital/Stealth).
/// </summary>
public enum TechGeneration : byte
{
    Gen0_1970Reserve = 0,
    Gen1_1980Baseline = 1,
    Gen2_1988LateColdWar = 2,
    Gen3_1995DigitalStealth = 3
}

/// <summary>
/// Strategic industrial resources produced in states.
/// </summary>
public enum ResourceType : byte
{
    Oil = 0,
    Steel = 1,
    Aluminum = 2,
    Semiconductors = 3
}

/// <summary>
/// State-level constructible building types queued in ConstructionSystem.
/// </summary>
public enum BuildingType : byte
{
    None = 0,
    CivilianFactory = 1,
    MilitaryFactory = 2,
    NavalDockyard = 3,
    Infrastructure = 4,
    Airbase = 5,
    RadarSam = 6
}

/// <summary>
/// Province terrain classification governing movement cost and combat modifiers.
/// </summary>
public enum TerrainType : byte
{
    Plains = 0,
    Forest = 1,
    Hills = 2,
    Mountain = 3,
    Urban = 4,
    Jungle = 5,
    Marsh = 6,
    Ocean = 7
}

/// <summary>
/// Geopolitical alignment factions for strategic air/naval control tracking.
/// </summary>
public enum FactionId : byte
{
    Neutral = 0,
    BlueforPacom = 1,
    RedforSocialist = 2,
    PrcSphere = 3
}

/// <summary>
/// Bitmask flags on CSR province adjacency edges.
/// </summary>
[Flags]
public enum AdjacencyEdgeFlags : byte
{
    None = 0,
    Standard = 1 << 0,
    River = 1 << 1,
    Strait = 1 << 2,
    Railway = 1 << 3
}

/// <summary>
/// Constant 1-byte country tags (0..14) for the 7 Major Powers and 8 Regional Minors.
/// </summary>
public static class CountryTag
{
    // 7 Major Powers
    public const byte PRC = 0;
    public const byte USA = 1;
    public const byte SOV = 2, RUS = 2;
    public const byte JPN = 3;
    public const byte ROK = 4;
    public const byte PRK = 5;
    public const byte TWN = 6;

    // 8 Regional Minors
    public const byte VNM = 7;
    public const byte PHL = 8;
    public const byte MNG = 9;
    public const byte THA = 10;
    public const byte LAO = 11;
    public const byte KHM = 12;
    public const byte MMR = 13;
    public const byte MYS = 14;

    public const byte ActiveCount = 15;
    public const byte MaxCapacity = 16;
    public const byte None = 255;

    private static readonly string[] TagCodes =
    [
        "PRC", "USA", "SOV", "JPN", "ROK", "PRK", "TWN",
        "VNM", "PHL", "MNG", "THA", "LAO", "KHM", "MMR", "MYS"
    ];

    public static string GetCode(byte tagId)
    {
        return tagId < TagCodes.Length ? TagCodes[tagId] : "NONE";
    }

    public static bool TryParse(ReadOnlySpan<char> code, out byte tagId)
    {
        if (code.Length == 3)
        {
            for (byte i = 0; i < TagCodes.Length; i++)
            {
                if (code.Equals(TagCodes[i].AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    tagId = i;
                    return true;
                }
            }
        }

        tagId = None;
        return false;
    }
}
