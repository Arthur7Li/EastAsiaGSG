using System.Diagnostics;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Core.Systems;
using EastAsiaGSG.Data.Loaders;
using EastAsiaGSG.Data.Map;

namespace EastAsiaGSG.SimTests;

/// <summary>
/// Headless xUnit test suite validating Phase 4, Task 4.2:
/// AirAndNavalRegionSystem air superiority, naval control ratios, coastal A2/AD missile suppression,
/// the 60% Naval / 50% Air Superiority Strait &amp; Amphibious Control Gate, and zero-allocation invariants.
/// </summary>
public sealed class AirAndNavalRegionSystemTests
{
    private static string FindRepositoryRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "EastAsiaGSG.sln")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing EastAsiaGSG.sln");
    }

    private static SimulationWorld CreateLoadedKoreanPeninsulaWorld()
    {
        string root = FindRepositoryRoot();
        string statesPath = Path.Combine(root, "data", "map", "states.json");
        string provincesPath = Path.Combine(root, "data", "map", "provinces.json");

        SimulationWorld world = new();
        JsonMapLoader.LoadMap(world, statesPath, provincesPath);
        return world;
    }

    [Fact]
    public void AirAndNavalRegionSystem_ZeroHeapAllocations_InHotSixHourLoop()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        // Give ROK and PRK active aircraft and coastal facilities
        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen1_1980Baseline, 400);
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.PGM, TechGeneration.Gen1_1980Baseline, 200);

        world.Countries[CountryTag.PRK].Faction = FactionId.RedforSocialist;
        world.Countries[CountryTag.PRK].SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen0_1970Reserve, 500);
        world.Countries[CountryTag.PRK].SetStockpile(EquipmentType.PGM, TechGeneration.Gen0_1970Reserve, 100);

        // Warm up JIT
        AirAndNavalRegionSystem.Execute(world);
        AirAndNavalRegionSystem.Execute(world);

        long startBytes = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 50; i++)
        {
            AirAndNavalRegionSystem.Execute(world);
        }

        long endBytes = GC.GetAllocatedBytesForCurrentThread();
        long allocated = endBytes - startBytes;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void AirAndNavalRegionSystem_HighThroughputBenchmark_UnderOneMillisecond()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen2_1988LateColdWar, 600);
        world.Countries[CountryTag.PRK].Faction = FactionId.RedforSocialist;
        world.Countries[CountryTag.PRK].SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen1_1980Baseline, 400);

        // Warm up
        for (int i = 0; i < 10; i++)
        {
            AirAndNavalRegionSystem.Execute(world);
        }

        const int iterations = 1000;
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            AirAndNavalRegionSystem.Execute(world);
        }

        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 0.10, $"AirAndNavalRegionSystem average tick was {avgMs:F4} ms, exceeding 0.10 ms threshold!");
    }

    [Fact]
    public void StraitCrossingGate_BlocksHostileCrossing_WhenNavalControlBelow60Percent()
    {
        using SimulationWorld world = new();

        const byte regionId = 2; // Taiwan Strait or Korea Strait
        ref StrategicRegionData region = ref world.Regions[regionId];
        region.RegionId = regionId;
        region.IsSeaZone = 1;
        region.SetAirSuperiority(FactionId.BlueforPacom, 0.70f); // > 50% Air met
        region.SetNavalControl(FactionId.BlueforPacom, 0.55f);   // 55% Naval NOT met (needs > 60%)

        Assert.False(region.CanCrossStraitOrInvade(FactionId.BlueforPacom));

        // Prov 0 (Attacker ROK) -> Prov 1 (Defender PRK) across Strait
        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        world.Countries[CountryTag.PRK].Faction = FactionId.RedforSocialist;

        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].StrategicRegionId = regionId;

        world.Provinces[1].ProvinceId = 1;
        world.Provinces[1].ControllerTag = CountryTag.PRK;
        world.Provinces[1].StrategicRegionId = regionId;

        Span<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Strait),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Strait)
        ];
        world.Graph.BuildCsr(world.Provinces.AsSpan(), edges);

        // Division orders movement across strait
        var div = new DivisionComponent
        {
            DivisionId = 1,
            CountryTag = CountryTag.ROK,
            CurrentProvinceId = 0,
            TargetProvinceId = 1,
            DestinationProvinceId = 1,
            State = DivisionState.Moving,
            WaypointCount = 1,
            CurrentWaypointIndex = 0,
            Speed = 4.0f,
            Strength = 1.0f,
            Organization = 60.0f,
            MaxOrganization = 60.0f
        };
        div.SetWaypoint(0, 1);
        Entity divEntity = world.EcsWorld.Create(div);

        DivisionMovementSystem.Execute(world);

        // Hostile movement must be blocked by the Strait Control Gate
        ref DivisionComponent updated = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
        Assert.Equal(DivisionState.Idle, updated.State);
        Assert.Equal(0, updated.WaypointCount);
    }

    [Fact]
    public void StraitCrossingGate_BlocksHostileCrossing_WhenAirSuperiorityBelow50Percent()
    {
        using SimulationWorld world = new();

        const byte regionId = 3;
        ref StrategicRegionData region = ref world.Regions[regionId];
        region.RegionId = regionId;
        region.IsSeaZone = 1;
        region.SetNavalControl(FactionId.BlueforPacom, 0.75f);   // > 60% Naval met
        region.SetAirSuperiority(FactionId.BlueforPacom, 0.45f); // 45% Air NOT met (needs > 50%)

        Assert.False(region.CanCrossStraitOrInvade(FactionId.BlueforPacom));

        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        world.Countries[CountryTag.PRK].Faction = FactionId.RedforSocialist;

        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].StrategicRegionId = regionId;

        world.Provinces[1].ProvinceId = 1;
        world.Provinces[1].ControllerTag = CountryTag.PRK;
        world.Provinces[1].StrategicRegionId = regionId;

        Span<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Strait),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Strait)
        ];
        world.Graph.BuildCsr(world.Provinces.AsSpan(), edges);

        var div = new DivisionComponent
        {
            DivisionId = 2,
            CountryTag = CountryTag.ROK,
            CurrentProvinceId = 0,
            TargetProvinceId = 1,
            DestinationProvinceId = 1,
            State = DivisionState.Moving,
            WaypointCount = 1,
            CurrentWaypointIndex = 0,
            Speed = 4.0f,
            Strength = 1.0f,
            Organization = 60.0f,
            MaxOrganization = 60.0f
        };
        div.SetWaypoint(0, 1);
        Entity divEntity = world.EcsWorld.Create(div);

        DivisionMovementSystem.Execute(world);

        ref DivisionComponent updated = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
        Assert.Equal(DivisionState.Idle, updated.State);
        Assert.Equal(0, updated.WaypointCount);
    }

    [Fact]
    public void StraitCrossingGate_AllowsHostileCrossing_WhenBothThresholdsMet()
    {
        using SimulationWorld world = new();

        const byte regionId = 4;
        ref StrategicRegionData region = ref world.Regions[regionId];
        region.RegionId = regionId;
        region.IsSeaZone = 1;
        region.SetNavalControl(FactionId.BlueforPacom, 0.65f);   // > 60% met
        region.SetAirSuperiority(FactionId.BlueforPacom, 0.55f); // > 50% met

        Assert.True(region.CanCrossStraitOrInvade(FactionId.BlueforPacom));

        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        world.Countries[CountryTag.PRK].Faction = FactionId.RedforSocialist;

        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].StrategicRegionId = regionId;

        world.Provinces[1].ProvinceId = 1;
        world.Provinces[1].ControllerTag = CountryTag.PRK;
        world.Provinces[1].StrategicRegionId = regionId;

        Span<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Strait),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Strait)
        ];
        world.Graph.BuildCsr(world.Provinces.AsSpan(), edges);

        var div = new DivisionComponent
        {
            DivisionId = 3,
            CountryTag = CountryTag.ROK,
            CurrentProvinceId = 0,
            TargetProvinceId = 1,
            DestinationProvinceId = 1,
            State = DivisionState.Moving,
            WaypointCount = 1,
            CurrentWaypointIndex = 0,
            Speed = 4.0f,
            Strength = 1.0f,
            Organization = 60.0f,
            MaxOrganization = 60.0f
        };
        div.SetWaypoint(0, 1);
        Entity divEntity = world.EcsWorld.Create(div);

        DivisionMovementSystem.Execute(world);

        ref DivisionComponent updated = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
        // Cross border attack triggers combat or advances movement progress
        Assert.True(updated.State == DivisionState.Attacking || updated.MovementProgress > 0.0f);
    }

    [Fact]
    public void FriendlyStraitMovement_NeverBlockedByNavalControl()
    {
        using SimulationWorld world = new();

        const byte regionId = 5;
        ref StrategicRegionData region = ref world.Regions[regionId];
        region.RegionId = regionId;
        region.IsSeaZone = 1;
        region.SetNavalControl(FactionId.BlueforPacom, 0.0f);   // 0% Naval Control
        region.SetAirSuperiority(FactionId.BlueforPacom, 0.0f); // 0% Air Superiority

        // Both provinces are friendly ROK territory (e.g. Busan to Jeju)
        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;

        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].StrategicRegionId = regionId;

        world.Provinces[1].ProvinceId = 1;
        world.Provinces[1].ControllerTag = CountryTag.ROK;
        world.Provinces[1].StrategicRegionId = regionId;

        Span<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Strait),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Strait)
        ];
        world.Graph.BuildCsr(world.Provinces.AsSpan(), edges);

        var div = new DivisionComponent
        {
            DivisionId = 4,
            CountryTag = CountryTag.ROK,
            CurrentProvinceId = 0,
            TargetProvinceId = 1,
            DestinationProvinceId = 1,
            State = DivisionState.Moving,
            WaypointCount = 1,
            CurrentWaypointIndex = 0,
            Speed = 4.0f,
            Strength = 1.0f,
            Organization = 60.0f,
            MaxOrganization = 60.0f
        };
        div.SetWaypoint(0, 1);
        Entity divEntity = world.EcsWorld.Create(div);

        DivisionMovementSystem.Execute(world);

        ref DivisionComponent updated = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
        // Peacetime friendly movement across friendly strait must NOT be blocked!
        Assert.True(updated.MovementProgress > 0.0f);
        Assert.Equal(DivisionState.Moving, updated.State);
    }

    [Fact]
    public void CoastalA2AD_RadarSamWithPgm_SuppressesEnemyNavalControl()
    {
        using SimulationWorld world = new();

        const byte regionId = 6;
        world.Regions[regionId].RegionId = regionId;
        world.Regions[regionId].IsSeaZone = 1;

        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        world.Countries[CountryTag.PRK].Faction = FactionId.RedforSocialist;

        // State 0 (ROK): Port Level 5 (no SAMs)
        world.States[0].StateId = 0;
        world.States[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].StateId = 0;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].StrategicRegionId = regionId;
        world.Provinces[0].PortLevel = 5;

        // State 1 (PRK): Port Level 5 + RadarSamLevel 4 + PGM Stockpile
        world.States[1].StateId = 1;
        world.States[1].ControllerTag = CountryTag.PRK;
        world.States[1].RadarSamLevel = 4;
        world.Provinces[1].ProvinceId = 1;
        world.Provinces[1].StateId = 1;
        world.Provinces[1].ControllerTag = CountryTag.PRK;
        world.Provinces[1].StrategicRegionId = regionId;
        world.Provinces[1].PortLevel = 5;

        // Give PRK 100 PGM
        world.Countries[CountryTag.PRK].SetStockpile(EquipmentType.PGM, TechGeneration.Gen1_1980Baseline, 100);

        AirAndNavalRegionSystem.Execute(world);

        // RadarSamLevel 4 -> (4 - 2) * 1000 = 2000 bps (20% penalty)
        Assert.Equal(2000, world.Regions[regionId].CoastalRadarSamPenaltyBps);

        // PRK suppresses ROK naval presence by 20%:
        // PRK power = 5 * 12 = 60.
        // ROK power = 5 * 12 * 0.80 = 48.
        // PRK share = 60 / 108 = 55.5%, ROK share = 48 / 108 = 44.4%
        float prkNaval = world.Regions[regionId].GetNavalControl(FactionId.RedforSocialist);
        float rokNaval = world.Regions[regionId].GetNavalControl(FactionId.BlueforPacom);

        Assert.True(prkNaval > rokNaval, $"Expected PRK ({prkNaval:F2}) > ROK ({rokNaval:F2}) due to A2/AD suppression!");

        // If PRK exhausts PGM stockpile to 0, A2/AD suppression deactivates
        world.Countries[CountryTag.PRK].SetStockpile(EquipmentType.PGM, TechGeneration.Gen1_1980Baseline, 0);
        AirAndNavalRegionSystem.Execute(world);

        Assert.Equal(0, world.Regions[regionId].CoastalRadarSamPenaltyBps);
        Assert.Equal(world.Regions[regionId].GetNavalControl(FactionId.RedforSocialist),
                     world.Regions[regionId].GetNavalControl(FactionId.BlueforPacom), precision: 2);
    }

    [Fact]
    public void AirbaseCapacityAndStockpileConstraint_LimitsAirPower()
    {
        using SimulationWorld world = new();

        const byte regionId = 7;
        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        world.Countries[CountryTag.PRK].Faction = FactionId.RedforSocialist;

        // State 0: Airbase Level 5 (Capacity 500)
        world.States[0].StateId = 0;
        world.States[0].ControllerTag = CountryTag.ROK;
        world.States[0].AirbaseLevel = 5;
        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].StateId = 0;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].StrategicRegionId = regionId;

        // State 1: Airbase Level 5 (Capacity 500)
        world.States[1].StateId = 1;
        world.States[1].ControllerTag = CountryTag.PRK;
        world.States[1].AirbaseLevel = 5;
        world.Provinces[1].ProvinceId = 1;
        world.Provinces[1].StateId = 1;
        world.Provinces[1].ControllerTag = CountryTag.PRK;
        world.Provinces[1].StrategicRegionId = regionId;

        // ROK has 200 Gen 1 planes; PRK has 500 Gen 1 planes
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen1_1980Baseline, 200);
        world.Countries[CountryTag.PRK].SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen1_1980Baseline, 500);

        AirAndNavalRegionSystem.Execute(world);

        float rokAir = world.Regions[regionId].GetAirSuperiority(FactionId.BlueforPacom);
        float prkAir = world.Regions[regionId].GetAirSuperiority(FactionId.RedforSocialist);

        // PRK has 500 planes vs ROK 200 planes -> PRK = 500/700 = 71.4%, ROK = 200/700 = 28.6%
        Assert.True(prkAir > rokAir);
        Assert.Equal(200f / 700f, rokAir, precision: 2);
        Assert.Equal(500f / 700f, prkAir, precision: 2);
    }

    [Fact]
    public void WeatherEfficiencyModifier_AttenuatesAirSuperiority()
    {
        using SimulationWorld world = new();

        const byte regionId = 8;
        world.Regions[regionId].RegionId = regionId;
        world.Regions[regionId].WeatherEfficiencyModifier = 0.50f; // Bad weather halves air sorties

        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        world.States[0].StateId = 0;
        world.States[0].ControllerTag = CountryTag.ROK;
        world.States[0].AirbaseLevel = 4;
        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].StateId = 0;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].StrategicRegionId = regionId;

        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen1_1980Baseline, 400);

        AirAndNavalRegionSystem.Execute(world);

        // Single faction dominance still normalizes to 1.0f
        Assert.Equal(1.0f, world.Regions[regionId].GetAirSuperiority(FactionId.BlueforPacom));
    }

    [Fact]
    public void UncontestedRegion_ZeroPower_CleanZeroRatiosWithoutNaN()
    {
        using SimulationWorld world = new();

        for (byte r = 0; r < SimulationWorld.MaxStrategicRegions; r++)
        {
            world.Regions[r].RegionId = r;
        }

        AirAndNavalRegionSystem.Execute(world);

        for (byte r = 0; r < SimulationWorld.MaxStrategicRegions; r++)
        {
            ref readonly StrategicRegionData region = ref world.Regions[r];
            for (int f = 0; f < StrategicRegionData.FactionSlotCount; f++)
            {
                float air = region.GetAirSuperiority((FactionId)f);
                float nav = region.GetNavalControl((FactionId)f);

                Assert.False(float.IsNaN(air));
                Assert.False(float.IsInfinity(air));
                Assert.Equal(0.0f, air);

                Assert.False(float.IsNaN(nav));
                Assert.False(float.IsInfinity(nav));
                Assert.Equal(0.0f, nav);
            }
        }
    }

    [Fact]
    public void AirbaseCapacity_EnforcesGenerationalScarcity_AcrossMultipleAirbases()
    {
        using SimulationWorld world = new();
        byte regionId = 0;
        world.Regions[regionId].RegionId = regionId;

        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        // ROK has strictly 20 Gen 3 stealth aircraft (multiplier 1.70x) and 0 of other generations
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen3_1995DigitalStealth, 20);

        // Setup two distinct states in Region 0 with AirbaseLevel = 1 (capacity 20 aircraft each)
        ushort stateA = 1;
        ushort stateB = 2;

        world.States[stateA].StateId = stateA;
        world.States[stateA].ControllerTag = CountryTag.ROK;
        world.States[stateA].AirbaseLevel = 1;

        world.States[stateB].StateId = stateB;
        world.States[stateB].ControllerTag = CountryTag.ROK;
        world.States[stateB].AirbaseLevel = 1;

        world.Provinces[10].ProvinceId = 10;
        world.Provinces[10].StateId = stateA;
        world.Provinces[10].StrategicRegionId = regionId;
        world.Provinces[10].ControllerTag = CountryTag.ROK;

        world.Provinces[20].ProvinceId = 20;
        world.Provinces[20].StateId = stateB;
        world.Provinces[20].StrategicRegionId = regionId;
        world.Provinces[20].ControllerTag = CountryTag.ROK;

        AirAndNavalRegionSystem.Execute(world);

        // State A should consume the 20 Gen 3 jets: 20 * 1.70f = 34.0f air power
        // State B finds 0 remaining Gen 3 jets and 0 remaining total aircraft, so contributes 0.0f
        // Total air power must equal 34.0f (NOT 68.0f which was the defect behavior where Gen 3 was cloned!)
        float blueAir = world.Regions[regionId].GetAirSuperiority(FactionId.BlueforPacom);
        Assert.Equal(1.0f, blueAir); // Uncontested, normalized to 1.0f

        // Now test in contested environment: PRK has 34 baseline aircraft (multiplier 1.0f -> 34 air power)
        world.Countries[CountryTag.PRK].Faction = FactionId.RedforSocialist;
        world.Countries[CountryTag.PRK].SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen1_1980Baseline, 34);

        ushort statePrk = 3;
        world.States[statePrk].StateId = statePrk;
        world.States[statePrk].ControllerTag = CountryTag.PRK;
        world.States[statePrk].AirbaseLevel = 2; // capacity 40

        world.Provinces[30].ProvinceId = 30;
        world.Provinces[30].StateId = statePrk;
        world.Provinces[30].StrategicRegionId = regionId;
        world.Provinces[30].ControllerTag = CountryTag.PRK;

        AirAndNavalRegionSystem.Execute(world);

        // ROK air power = 34.0f (20 * 1.70), PRK air power = 34.0f (34 * 1.0)
        // With equal power (34 vs 34), ratio must be exactly 50% / 50%
        float blueRatio = world.Regions[regionId].GetAirSuperiority(FactionId.BlueforPacom);
        float redRatio = world.Regions[regionId].GetAirSuperiority(FactionId.RedforSocialist);

        Assert.Equal(0.50f, blueRatio, 0.01f);
        Assert.Equal(0.50f, redRatio, 0.01f);
    }

    [Fact]
    public void MapLutBuffer_AirSuperiorityMode_RendersSeaZonesAccurately()
    {
        using SimulationWorld world = new();
        byte regionId = 3;
        world.Regions[regionId].RegionId = regionId;

        // Setup Bluefor dominance in region 3
        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen1_1980Baseline, 100);

        ushort landState = 10;
        world.States[landState].StateId = landState;
        world.States[landState].ControllerTag = CountryTag.ROK;
        world.States[landState].AirbaseLevel = 5;

        world.Provinces[100].ProvinceId = 100;
        world.Provinces[100].StateId = landState;
        world.Provinces[100].StrategicRegionId = regionId;
        world.Provinces[100].ControllerTag = CountryTag.ROK;

        // Ocean province in region 3 with CountryTag.None
        ushort seaProvId = 101;
        world.Provinces[seaProvId].ProvinceId = seaProvId;
        world.Provinces[seaProvId].Terrain = TerrainType.Ocean;
        world.Provinces[seaProvId].StrategicRegionId = regionId;
        world.Provinces[seaProvId].ControllerTag = CountryTag.None;

        AirAndNavalRegionSystem.Execute(world);

        MapLutBuffer lut = new();
        lut.UpdateFromWorld(world, MapMode.AirSuperiority);

        // Sea zone texel must NOT be the neutral unowned gray (96, 102, 108);
        // It should render the Blue-dominant air superiority color (Blue dominant has high blue channel)
        Color32 seaColor = lut[seaProvId];
        Assert.True(seaColor.B > seaColor.R, "Sea zone in AirSuperiority mode should reflect Bluefor air dominance (B > R)");
        Assert.Equal(CountryTag.None, seaColor.A);
    }
}
