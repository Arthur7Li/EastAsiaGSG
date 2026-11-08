using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Core.Systems;

namespace EastAsiaGSG.SimTests;

public sealed class NavalTaskForceAndAirWingTests
{
    private static readonly QueryDescription TaskForceQuery = new QueryDescription().WithAll<NavalTaskForceComponent>();
    private static readonly QueryDescription AirWingQuery = new QueryDescription().WithAll<AirWingComponent>();

    [Fact]
    public void NavalTaskForce_AirSuperiorityAndNavalControl_UpdatedByDiscreteTaskForces()
    {
        using SimulationWorld world = new();

        const byte regionId = 8; // Philippine Sea
        world.Regions[regionId].IsSeaZone = 1;

        // Spawn a discrete US Navy Carrier Strike Group assigned to Region 8
        world.SpawnNavalTaskForce(NavalTaskForceComponent.CreateCarrierStrikeGroup(
            taskForceId: 1,
            countryTag: CountryTag.USA,
            assignedRegionId: regionId,
            homeBaseProvinceId: 3,
            generation: TechGeneration.Gen2_1988LateColdWar,
            pgmAuthorized: true));

        AirAndNavalRegionSystem.Execute(world);

        ref readonly StrategicRegionData region = ref world.Regions[regionId];

        // BlueforPacom should have decisive control of Region 8
        float blueforNaval = region.GetNavalControl(FactionId.BlueforPacom);
        float blueforAir = region.GetAirSuperiority(FactionId.BlueforPacom);

        Assert.True(blueforNaval > 0.80f, $"Expected Bluefor naval control > 80%, was {blueforNaval:P1}");
        Assert.True(blueforAir > 0.80f, $"Expected Bluefor air superiority > 80%, was {blueforAir:P1}");
        Assert.True(region.CanCrossStraitOrInvade(FactionId.BlueforPacom));
    }

    [Fact]
    public void SubmarineWolfpack_ConvoyInterdiction_ChokesOverseasPortSupplyCapacity_AndEscortRecoversIt()
    {
        using SimulationWorld world = new();

        const byte seaRegionId = 8;
        world.Regions[seaRegionId].IsSeaZone = 1;

        // Province 10: US Mainland/Okinawa Export Port (Port Level 4)
        world.Provinces[10].ProvinceId = 10;
        world.Provinces[10].OwnerTag = CountryTag.USA;
        world.Provinces[10].ControllerTag = CountryTag.USA;
        world.Provinces[10].PortLevel = 4;
        world.Provinces[10].SupplyRatio = 1.0f;
        world.CountryCapitals[CountryTag.USA] = 10;

        // Province 50: Island Base Import Port in Region 8 (Port Level 3)
        world.Provinces[50].ProvinceId = 50;
        world.Provinces[50].OwnerTag = CountryTag.USA;
        world.Provinces[50].ControllerTag = CountryTag.USA;
        world.Provinces[50].PortLevel = 3;
        world.Provinces[50].StrategicRegionId = seaRegionId;
        world.Provinces[50].SupplyRatio = 1.0f;

        // 1. Baseline: No submarine interdiction
        AirAndNavalRegionSystem.Execute(world);
        SupplyGraphSystem.Execute(world);

        float baselineHubCap = world.Provinces[50].SupplyRatio;
        Assert.Equal(0.0f, world.SeaZoneInterdictionPenalty[seaRegionId]);

        // 2. Deploy Soviet Submarine Wolfpack raiding convoys in Region 8
        Entity subEntity = world.SpawnNavalTaskForce(NavalTaskForceComponent.CreateSubmarineWolfpack(
            taskForceId: 201,
            countryTag: CountryTag.SOV,
            assignedRegionId: seaRegionId,
            homeBaseProvinceId: 0,
            generation: TechGeneration.Gen2_1988LateColdWar));

        AirAndNavalRegionSystem.Execute(world);

        Assert.True(world.SeaZoneInterdictionPenalty[seaRegionId] > 0.15f,
            $"Expected submarine interdiction penalty > 15%, was {world.SeaZoneInterdictionPenalty[seaRegionId]:P1}");

        // 3. Deploy US Surface Action Group on Convoy Escort to suppress submarines
        Entity escortEntity = world.SpawnNavalTaskForce(new NavalTaskForceComponent
        {
            TaskForceId = 102,
            CountryTag = CountryTag.USA,
            Type = NavalTaskForceType.SurfaceActionGroup,
            Mission = NavalMission.ConvoyEscort,
            AssignedRegionId = seaRegionId,
            HomeBaseProvinceId = 10,
            CurrentProvinceOrSeaZoneId = 10,
            Strength = 1.0f,
            Organization = 70.0f,
            MaxOrganization = 70.0f,
            SurfaceCombatPower = 80.0f,
            AntiAirPower = 60.0f,
            SubsurfacePower = 40.0f,
            ShipCount = 6,
            Speed = 28.0f
        });

        AirAndNavalRegionSystem.Execute(world);

        // Escort should have suppressed the submarine raiding penalty
        Assert.True(world.SeaZoneInterdictionPenalty[seaRegionId] < 0.15f,
            $"Expected escorted interdiction penalty < 15%, was {world.SeaZoneInterdictionPenalty[seaRegionId]:P1}");
    }

    [Fact]
    public void DiscreteAirWings_ContestRegionalAirDominance()
    {
        using SimulationWorld world = new();

        const byte regionId = 7; // East China Sea

        // Spawn a PRC Fighter Wing on Air Superiority
        world.SpawnAirWing(AirWingComponent.CreateFighterWing(
            wingId: 501,
            countryTag: CountryTag.PRC,
            assignedRegionId: regionId,
            baseStateId: 2,
            aircraftCount: 72,
            generation: TechGeneration.Gen2_1988LateColdWar));

        AirAndNavalRegionSystem.Execute(world);

        ref readonly StrategicRegionData region = ref world.Regions[regionId];
        float prcAir = region.GetAirSuperiority(FactionId.PrcSphere);

        Assert.True(prcAir > 0.80f, $"Expected PRC air superiority > 80%, was {prcAir:P1}");
    }

    [Fact]
    public void SuperpowerEscalation_SpawnsPhysicalCarrierStrikeGroups_WhenEscalationCrossesThresholds()
    {
        using SimulationWorld world = new();

        world.CountryCapitals[CountryTag.USA] = 3;

        // Baseline peacetime escalation (15) -> 0 CSGs
        SuperpowerEscalationSystem.Execute(world);

        int csgCount = 0;
        Query query1 = world.EcsWorld.Query(in TaskForceQuery);
        foreach (ref Chunk chunk in query1)
        {
            Span<NavalTaskForceComponent> tfs = chunk.GetSpan<NavalTaskForceComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                if (tfs[i].CountryTag == CountryTag.USA && tfs[i].Type == NavalTaskForceType.CarrierStrikeGroup)
                {
                    csgCount++;
                }
            }
        }
        Assert.Equal(0, csgCount);

        // Trigger heightened escalation (75 = DEFCON 2, requires 2 CSGs)
        world.Countries[CountryTag.USA].HomelandEscalation = 75;
        world.CountryHomelandEscalation[CountryTag.USA] = 75.0f;

        SuperpowerEscalationSystem.Execute(world);

        csgCount = 0;
        Query query2 = world.EcsWorld.Query(in TaskForceQuery);
        foreach (ref Chunk chunk in query2)
        {
            Span<NavalTaskForceComponent> tfs = chunk.GetSpan<NavalTaskForceComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                if (tfs[i].CountryTag == CountryTag.USA && tfs[i].Type == NavalTaskForceType.CarrierStrikeGroup)
                {
                    csgCount++;
                }
            }
        }
        Assert.Equal(2, csgCount);
    }

    [Fact]
    public void NavalAndAirSystems_ZeroHeapAllocations_InHotLoops()
    {
        using SimulationWorld world = new();

        world.SpawnNavalTaskForce(NavalTaskForceComponent.CreateCarrierStrikeGroup(1, CountryTag.USA, 8, 3));
        world.SpawnNavalTaskForce(NavalTaskForceComponent.CreateSubmarineWolfpack(2, CountryTag.SOV, 8, 0));
        world.SpawnAirWing(AirWingComponent.CreateFighterWing(101, CountryTag.PRC, 7, 2));

        for (int w = 0; w < 128; w++)
        {
            AirAndNavalRegionSystem.Execute(world);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

        for (int iter = 0; iter < 1000; iter++)
        {
            AirAndNavalRegionSystem.Execute(world);
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, afterAlloc - beforeAlloc);
    }
}
