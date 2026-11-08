using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Components;

/// <summary>
/// Operational mission assigned to a land-based or carrier-embarked air wing.
/// </summary>
public enum AirMission : byte
{
    AirSuperiority = 0,        // Sweep & combat air patrol contesting regional air dominance
    CloseAirSupport = 1,       // Direct tactical strikes assisting frontline divisions in land combat
    NavalStrike = 2,           // Anti-surface strikes against hostile naval task forces
    StrategicInterdiction = 3  // Deep infrastructure and supply hub interdiction
}

/// <summary>
/// Explicit 32-byte unmanaged ECS component tracking a discrete air wing entity.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct AirWingComponent
{
    [FieldOffset(0)]  public int WingId;
    [FieldOffset(4)]  public byte CountryTag;
    [FieldOffset(5)]  public AirMission Mission;
    [FieldOffset(6)]  public byte AssignedRegionId;
    [FieldOffset(7)]  public byte Generation;             // TechGeneration
    [FieldOffset(8)]  public ushort BaseStateId;
    [FieldOffset(10)] public ushort AircraftCount;
    [FieldOffset(12)] public ushort MaxAircraftCount;
    [FieldOffset(14)] public byte Readiness;              // 0..100%
    [FieldOffset(15)] public byte PgmAuthorized;
    [FieldOffset(16)] public float AirSuperiorityPower;
    [FieldOffset(20)] public float GroundAttackPower;
    [FieldOffset(24)] public float NavalAttackPower;
    [FieldOffset(28)] public int Reserved;

    public readonly bool IsCombatReady
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => AircraftCount > 0 && Readiness >= 20;
    }

    public static AirWingComponent CreateFighterWing(
        int wingId,
        byte countryTag,
        byte assignedRegionId,
        ushort baseStateId,
        ushort aircraftCount = 48,
        TechGeneration generation = TechGeneration.Gen2_1988LateColdWar,
        bool pgmAuthorized = false)
    {
        float mult = generation switch
        {
            TechGeneration.Gen0_1970Reserve => 0.70f,
            TechGeneration.Gen1_1980Baseline => 1.00f,
            TechGeneration.Gen2_1988LateColdWar => 1.30f,
            TechGeneration.Gen3_1995DigitalStealth => 1.70f,
            _ => 1.00f
        };

        return new AirWingComponent
        {
            WingId = wingId,
            CountryTag = countryTag,
            Mission = AirMission.AirSuperiority,
            AssignedRegionId = assignedRegionId,
            Generation = (byte)generation,
            BaseStateId = baseStateId,
            AircraftCount = aircraftCount,
            MaxAircraftCount = aircraftCount,
            Readiness = 100,
            PgmAuthorized = pgmAuthorized ? (byte)1 : (byte)0,
            AirSuperiorityPower = aircraftCount * 2.0f * mult,
            GroundAttackPower = aircraftCount * 0.5f * mult,
            NavalAttackPower = aircraftCount * 0.5f * mult,
            Reserved = 0
        };
    }

    public static AirWingComponent CreateCasWing(
        int wingId,
        byte countryTag,
        byte assignedRegionId,
        ushort baseStateId,
        ushort aircraftCount = 36,
        TechGeneration generation = TechGeneration.Gen1_1980Baseline,
        bool pgmAuthorized = true)
    {
        float mult = generation switch
        {
            TechGeneration.Gen0_1970Reserve => 0.70f,
            TechGeneration.Gen1_1980Baseline => 1.00f,
            TechGeneration.Gen2_1988LateColdWar => 1.30f,
            TechGeneration.Gen3_1995DigitalStealth => 1.70f,
            _ => 1.00f
        };

        return new AirWingComponent
        {
            WingId = wingId,
            CountryTag = countryTag,
            Mission = AirMission.CloseAirSupport,
            AssignedRegionId = assignedRegionId,
            Generation = (byte)generation,
            BaseStateId = baseStateId,
            AircraftCount = aircraftCount,
            MaxAircraftCount = aircraftCount,
            Readiness = 100,
            PgmAuthorized = pgmAuthorized ? (byte)1 : (byte)0,
            AirSuperiorityPower = aircraftCount * 0.6f * mult,
            GroundAttackPower = aircraftCount * 2.5f * mult,
            NavalAttackPower = aircraftCount * 0.8f * mult,
            Reserved = 0
        };
    }

    public static AirWingComponent CreateNavalStrikeWing(
        int wingId,
        byte countryTag,
        byte assignedRegionId,
        ushort baseStateId,
        ushort aircraftCount = 36,
        TechGeneration generation = TechGeneration.Gen2_1988LateColdWar,
        bool pgmAuthorized = true)
    {
        float mult = generation switch
        {
            TechGeneration.Gen0_1970Reserve => 0.70f,
            TechGeneration.Gen1_1980Baseline => 1.00f,
            TechGeneration.Gen2_1988LateColdWar => 1.30f,
            TechGeneration.Gen3_1995DigitalStealth => 1.70f,
            _ => 1.00f
        };

        return new AirWingComponent
        {
            WingId = wingId,
            CountryTag = countryTag,
            Mission = AirMission.NavalStrike,
            AssignedRegionId = assignedRegionId,
            Generation = (byte)generation,
            BaseStateId = baseStateId,
            AircraftCount = aircraftCount,
            MaxAircraftCount = aircraftCount,
            Readiness = 100,
            PgmAuthorized = pgmAuthorized ? (byte)1 : (byte)0,
            AirSuperiorityPower = aircraftCount * 0.8f * mult,
            GroundAttackPower = aircraftCount * 0.8f * mult,
            NavalAttackPower = aircraftCount * 3.0f * mult,
            Reserved = 0
        };
    }
}
