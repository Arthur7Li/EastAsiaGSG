using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Components;

/// <summary>
/// Operational archetype of a discrete Pacific naval task force.
/// </summary>
public enum NavalTaskForceType : byte
{
    CarrierStrikeGroup = 0,    // CVN/CV + air wing + cruiser/destroyer escort screen
    SurfaceActionGroup = 1,    // Cruisers, destroyers, guided missile frigates
    SubmarineWolfpack = 2,     // Nuclear/diesel-electric attack submarines (SSN/SSK)
    AmphibiousReadyGroup = 3   // Landing helicopter assault/dock ships + embarked Marines
}

/// <summary>
/// Assigned operational mission of a naval task force within a strategic region or sea zone.
/// </summary>
public enum NavalMission : byte
{
    Patrol = 0,                // General naval control & sea lane surveillance
    StrikeForce = 1,           // Rapid reaction force, heavy surface & air engagement
    ConvoyInterdiction = 2,    // Submarine commerce raiding reducing overseas port throughput
    ConvoyEscort = 3,          // Anti-submarine warfare screen protecting merchant sea lanes
    NavalInvasionSupport = 4   // Shore bombardment & amphibious assault escort
}

/// <summary>
/// Explicit 64-byte unmanaged ECS component tracking a discrete naval task force entity.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct NavalTaskForceComponent
{
    [FieldOffset(0)]  public int TaskForceId;
    [FieldOffset(4)]  public byte CountryTag;
    [FieldOffset(5)]  public NavalTaskForceType Type;
    [FieldOffset(6)]  public NavalMission Mission;
    [FieldOffset(7)]  public byte AssignedRegionId;
    [FieldOffset(8)]  public ushort HomeBaseProvinceId;
    [FieldOffset(10)] public ushort CurrentProvinceOrSeaZoneId;
    [FieldOffset(12)] public float Strength;                     // 0.0f..1.0f
    [FieldOffset(16)] public float Organization;                 // 0.0f..100.0f
    [FieldOffset(20)] public float MaxOrganization;
    [FieldOffset(24)] public float SurfaceCombatPower;
    [FieldOffset(28)] public float AntiAirPower;
    [FieldOffset(32)] public float SubsurfacePower;
    [FieldOffset(36)] public float ConvoyInterdictionEfficiency; // 0.0f..1.0f
    [FieldOffset(40)] public byte PgmAuthorized;
    [FieldOffset(41)] public byte Generation;                     // TechGeneration
    [FieldOffset(42)] public ushort ShipCount;
    [FieldOffset(44)] public float Speed;
    [FieldOffset(48)] public int Reserved0;
    [FieldOffset(52)] public int Reserved1;
    [FieldOffset(56)] public int Reserved2;
    [FieldOffset(60)] public int Reserved3;

    public readonly bool IsCombatReady
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Strength > 0.10f && Organization > 10.0f;
    }

    public static NavalTaskForceComponent CreateCarrierStrikeGroup(
        int taskForceId,
        byte countryTag,
        byte assignedRegionId,
        ushort homeBaseProvinceId,
        TechGeneration generation = TechGeneration.Gen2_1988LateColdWar,
        bool pgmAuthorized = true)
    {
        return new NavalTaskForceComponent
        {
            TaskForceId = taskForceId,
            CountryTag = countryTag,
            Type = NavalTaskForceType.CarrierStrikeGroup,
            Mission = NavalMission.StrikeForce,
            AssignedRegionId = assignedRegionId,
            HomeBaseProvinceId = homeBaseProvinceId,
            CurrentProvinceOrSeaZoneId = homeBaseProvinceId,
            Strength = 1.0f,
            Organization = 80.0f,
            MaxOrganization = 80.0f,
            SurfaceCombatPower = 120.0f,
            AntiAirPower = 100.0f,
            SubsurfacePower = 40.0f,
            ConvoyInterdictionEfficiency = 0.20f,
            PgmAuthorized = pgmAuthorized ? (byte)1 : (byte)0,
            Generation = (byte)generation,
            ShipCount = 8,
            Speed = 30.0f
        };
    }

    public static NavalTaskForceComponent CreateSurfaceActionGroup(
        int taskForceId,
        byte countryTag,
        byte assignedRegionId,
        ushort homeBaseProvinceId,
        TechGeneration generation = TechGeneration.Gen1_1980Baseline)
    {
        return new NavalTaskForceComponent
        {
            TaskForceId = taskForceId,
            CountryTag = countryTag,
            Type = NavalTaskForceType.SurfaceActionGroup,
            Mission = NavalMission.Patrol,
            AssignedRegionId = assignedRegionId,
            HomeBaseProvinceId = homeBaseProvinceId,
            CurrentProvinceOrSeaZoneId = homeBaseProvinceId,
            Strength = 1.0f,
            Organization = 70.0f,
            MaxOrganization = 70.0f,
            SurfaceCombatPower = 80.0f,
            AntiAirPower = 60.0f,
            SubsurfacePower = 30.0f,
            ConvoyInterdictionEfficiency = 0.15f,
            PgmAuthorized = 0,
            Generation = (byte)generation,
            ShipCount = 6,
            Speed = 28.0f
        };
    }

    public static NavalTaskForceComponent CreateSubmarineWolfpack(
        int taskForceId,
        byte countryTag,
        byte assignedRegionId,
        ushort homeBaseProvinceId,
        TechGeneration generation = TechGeneration.Gen2_1988LateColdWar)
    {
        return new NavalTaskForceComponent
        {
            TaskForceId = taskForceId,
            CountryTag = countryTag,
            Type = NavalTaskForceType.SubmarineWolfpack,
            Mission = NavalMission.ConvoyInterdiction,
            AssignedRegionId = assignedRegionId,
            HomeBaseProvinceId = homeBaseProvinceId,
            CurrentProvinceOrSeaZoneId = homeBaseProvinceId,
            Strength = 1.0f,
            Organization = 60.0f,
            MaxOrganization = 60.0f,
            SurfaceCombatPower = 10.0f,
            AntiAirPower = 5.0f,
            SubsurfacePower = 90.0f,
            ConvoyInterdictionEfficiency = 0.65f,
            PgmAuthorized = 1,
            Generation = (byte)generation,
            ShipCount = 4,
            Speed = 25.0f
        };
    }

    public static NavalTaskForceComponent CreateAmphibiousReadyGroup(
        int taskForceId,
        byte countryTag,
        byte assignedRegionId,
        ushort homeBaseProvinceId)
    {
        return new NavalTaskForceComponent
        {
            TaskForceId = taskForceId,
            CountryTag = countryTag,
            Type = NavalTaskForceType.AmphibiousReadyGroup,
            Mission = NavalMission.NavalInvasionSupport,
            AssignedRegionId = assignedRegionId,
            HomeBaseProvinceId = homeBaseProvinceId,
            CurrentProvinceOrSeaZoneId = homeBaseProvinceId,
            Strength = 1.0f,
            Organization = 65.0f,
            MaxOrganization = 65.0f,
            SurfaceCombatPower = 40.0f,
            AntiAirPower = 45.0f,
            SubsurfacePower = 15.0f,
            ConvoyInterdictionEfficiency = 0.05f,
            PgmAuthorized = 0,
            Generation = (byte)TechGeneration.Gen1_1980Baseline,
            ShipCount = 5,
            Speed = 22.0f
        };
    }
}
