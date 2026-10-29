using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Core.Systems;
using EastAsiaGSG.Data.Loaders;

namespace EastAsiaGSG.SimTests;

/// <summary>
/// Headless xUnit test suite validating Phase 4, Task 4.1:
/// SupplyGraphSystem Two-Tier Hub/Rail/Port Dijkstra, Soviet Trans-Siberian Artery bottleneck logic,
/// division demand calculations, isolated pocket starvation gates, and zero-allocation performance invariants.
/// </summary>
public sealed class SupplyGraphSystemTests
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

    private static Entity SpawnTestDivision(
        SimulationWorld world,
        int divisionId,
        byte countryTag,
        ushort provinceId,
        float hardness = 0.20f,
        float strength = 1.0f)
    {
        var div = new DivisionComponent
        {
            DivisionId = divisionId,
            CountryTag = countryTag,
            CurrentProvinceId = provinceId,
            State = DivisionState.Idle,
            Hardness = hardness,
            Strength = strength,
            Organization = 60.0f,
            MaxOrganization = 60.0f,
            Speed = 4.0f
        };

        return world.EcsWorld.Create(div);
    }

    [Fact]
    public void SupplyGraphSystem_ZeroHeapAllocations_InHotDailyLoop()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        // Spawn multiple divisions across ROK and PRK
        SpawnTestDivision(world, 1, CountryTag.ROK, 0, hardness: 0.20f); // Infantry in Seoul
        SpawnTestDivision(world, 2, CountryTag.ROK, 3, hardness: 0.50f); // Mech in Incheon
        SpawnTestDivision(world, 3, CountryTag.ROK, 6, hardness: 0.80f); // Armor in Paju
        SpawnTestDivision(world, 4, CountryTag.PRK, 60, hardness: 0.20f); // Infantry in Pyongyang
        SpawnTestDivision(world, 5, CountryTag.PRK, 70, hardness: 0.50f); // Mech

        // Warm up system and thread-local buffers
        SupplyGraphSystem.Execute(world);
        SupplyGraphSystem.Execute(world);

        long startBytes = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 50; i++)
        {
            SupplyGraphSystem.Execute(world);
        }

        long endBytes = GC.GetAllocatedBytesForCurrentThread();
        long allocated = endBytes - startBytes;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SupplyGraphSystem_HighThroughputBenchmark_UnderOneMillisecond()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        // Populate with 20 active field divisions
        for (int i = 0; i < 20; i++)
        {
            ushort provId = (ushort)(i * 3);
            byte tag = (byte)(i % 2 == 0 ? CountryTag.ROK : CountryTag.PRK);
            SpawnTestDivision(world, 100 + i, tag, provId, hardness: (i % 3) * 0.35f);
        }

        // Warm up
        for (int i = 0; i < 10; i++)
        {
            SupplyGraphSystem.Execute(world);
        }

        const int iterations = 1000;
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            SupplyGraphSystem.Execute(world);
        }

        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 0.50, $"SupplyGraphSystem average tick was {avgMs:F4} ms, exceeding 0.50 ms threshold!");
    }

    [Fact]
    public void SupplyGraphSystem_TransSiberianRailway_SeveranceDisconnectsDownstreamHubs()
    {
        using SimulationWorld world = new();

        // Setup synthetic 3-province Trans-Siberian corridor:
        // Prov 0 (State 0, Irkutsk/Chita Entry Node, Infrastructure 5)
        // -> Rail -> Prov 1 (State 1, Intermediate Corridor, Infrastructure 5)
        // -> Rail -> Prov 2 (State 2, Vladivostok Hub, SupplyHubLevel 3, Infrastructure 5)

        world.CountryCapitals[CountryTag.SOV] = 0;

        world.States[0].StateId = 0;
        world.States[0].ControllerTag = CountryTag.SOV;
        world.States[0].InfrastructureLevel = 5;

        world.States[1].StateId = 1;
        world.States[1].ControllerTag = CountryTag.SOV;
        world.States[1].InfrastructureLevel = 5;

        world.States[2].StateId = 2;
        world.States[2].ControllerTag = CountryTag.SOV;
        world.States[2].InfrastructureLevel = 5;
        world.States[2].CivilianFactories = 0;
        world.States[2].MilitaryFactories = 0;

        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].StateId = 0;
        world.Provinces[0].ControllerTag = CountryTag.SOV;
        world.Provinces[0].SupplyHubLevel = 4;
        world.Provinces[0].InfrastructureLevel = 5;

        world.Provinces[1].ProvinceId = 1;
        world.Provinces[1].StateId = 1;
        world.Provinces[1].ControllerTag = CountryTag.SOV;
        world.Provinces[1].SupplyHubLevel = 0;
        world.Provinces[1].InfrastructureLevel = 5;

        world.Provinces[2].ProvinceId = 2;
        world.Provinces[2].StateId = 2;
        world.Provinces[2].ControllerTag = CountryTag.SOV;
        world.Provinces[2].SupplyHubLevel = 3;
        world.Provinces[2].InfrastructureLevel = 5;

        // Build bidirectional railway adjacency: 0 <-> 1 <-> 2
        Span<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard | AdjacencyEdgeFlags.Railway),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard | AdjacencyEdgeFlags.Railway),
            new DirectedEdge(1, 2, AdjacencyEdgeFlags.Standard | AdjacencyEdgeFlags.Railway),
            new DirectedEdge(2, 1, AdjacencyEdgeFlags.Standard | AdjacencyEdgeFlags.Railway)
        ];
        world.Graph.BuildCsr(world.Provinces.AsSpan(0, 3), edges);

        // Station a heavy armored division at Vladivostok (Prov 2)
        SpawnTestDivision(world, 1, CountryTag.SOV, 2, hardness: 0.85f);

        // 1. Initial intact railway: Far East hub receives off-map Trans-Siberian supply
        SupplyGraphSystem.Execute(world);
        Assert.Equal(1.0f, world.Provinces[2].SupplyRatio);

        // 2. Sever the Trans-Siberian corridor at Prov 1 (occupied by hostile forces)
        world.Provinces[1].ControllerTag = CountryTag.PRK;

        SupplyGraphSystem.Execute(world);

        // Downstream hub at Prov 2 is cut off from the Trans-Siberian Artery.
        // With State 2 having 0 factories, local delivered supply is 0, plunging ratio to minimum floor.
        Assert.Equal(SupplyGraphSystem.MinSupplyRatio, world.Provinces[2].SupplyRatio);
    }

    [Fact]
    public void SupplyGraphSystem_EncircledPocket_DivisionsStarveBelowReinforcementGate()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        // Encircled ROK division isolated deep behind enemy lines in Hamgyong (Province 108, PRK territory)
        world.Provinces[108].ControllerTag = CountryTag.ROK; // ROK pocket in northern territory
        Entity divEntity = SpawnTestDivision(world, 10, CountryTag.ROK, 108, hardness: 0.20f, strength: 0.50f);

        // Execute daily supply tick
        SupplyGraphSystem.Execute(world);

        ref readonly ProvinceData prov = ref world.Provinces[108];
        Assert.True(prov.SupplyRatio <= SupplyGraphSystem.MinSupplyRatio);
        Assert.True(prov.SupplyRatio < ReinforcementSystem.MinReinforcementSupplyRatio);

        // Verify ReinforcementSystem halts reinforcement in this pocket
        ref DivisionComponent div = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
        float strengthBefore = div.Strength;
        ReinforcementSystem.Execute(world);

        Assert.Equal(strengthBefore, div.Strength);
    }

    [Fact]
    public void SupplyGraphSystem_OverlandFriction_AttenuatesWithDistanceAndTerrain()
    {
        using SimulationWorld world = new();

        world.CountryCapitals[CountryTag.ROK] = 10;

        for (ushort s = 0; s < 4; s++)
        {
            world.States[s].StateId = s;
            world.States[s].ControllerTag = CountryTag.ROK;
            world.States[s].InfrastructureLevel = 3;
        }

        // Hub at Prov 10
        world.Provinces[10].ProvinceId = 10;
        world.Provinces[10].StateId = 0;
        world.Provinces[10].ControllerTag = CountryTag.ROK;
        world.Provinces[10].SupplyHubLevel = 3;
        world.Provinces[10].InfrastructureLevel = 3;
        world.Provinces[10].Terrain = TerrainType.Plains;

        // Plains neighbor at Prov 11 (1 hop)
        world.Provinces[11].ProvinceId = 11;
        world.Provinces[11].StateId = 1;
        world.Provinces[11].ControllerTag = CountryTag.ROK;
        world.Provinces[11].InfrastructureLevel = 3;
        world.Provinces[11].Terrain = TerrainType.Plains;

        // Mountain neighbor at Prov 12 (1 hop)
        world.Provinces[12].ProvinceId = 12;
        world.Provinces[12].StateId = 2;
        world.Provinces[12].ControllerTag = CountryTag.ROK;
        world.Provinces[12].InfrastructureLevel = 3;
        world.Provinces[12].Terrain = TerrainType.Mountain;

        // Plains 2 hops away at Prov 13 (connected to Prov 11)
        world.Provinces[13].ProvinceId = 13;
        world.Provinces[13].StateId = 3;
        world.Provinces[13].ControllerTag = CountryTag.ROK;
        world.Provinces[13].InfrastructureLevel = 3;
        world.Provinces[13].Terrain = TerrainType.Plains;

        Span<DirectedEdge> edges =
        [
            new DirectedEdge(10, 11, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(11, 10, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(10, 12, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(12, 10, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(11, 13, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(13, 11, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces.AsSpan(), edges);

        // Put a division in each province with equal demand (demand = 1.0)
        SpawnTestDivision(world, 11, CountryTag.ROK, 11, hardness: 0.10f);
        SpawnTestDivision(world, 12, CountryTag.ROK, 12, hardness: 0.10f);
        SpawnTestDivision(world, 13, CountryTag.ROK, 13, hardness: 0.10f);

        SupplyGraphSystem.Execute(world);

        // Add 50 divisions to 11, 12, 13 to force ratio = delivered / demanded (demand = 151)
        for (int i = 0; i < 50; i++)
        {
            SpawnTestDivision(world, 100 + i, CountryTag.ROK, 11, hardness: 0.85f);
            SpawnTestDivision(world, 200 + i, CountryTag.ROK, 12, hardness: 0.85f);
            SpawnTestDivision(world, 300 + i, CountryTag.ROK, 13, hardness: 0.85f);
        }

        SupplyGraphSystem.Execute(world);

        // Plains neighbor (11) must receive more delivered supply than Mountain neighbor (12)
        Assert.True(world.Provinces[11].SupplyRatio > world.Provinces[12].SupplyRatio,
            $"Expected Plains Prov 11 supply ({world.Provinces[11].SupplyRatio}) > Mountain Prov 12 supply ({world.Provinces[12].SupplyRatio})");

        // 1 hop Plains (11) must receive more delivered supply than 2 hops Plains (13)
        Assert.True(world.Provinces[11].SupplyRatio > world.Provinces[13].SupplyRatio,
            $"Expected 1-hop Prov 11 supply ({world.Provinces[11].SupplyRatio}) > 2-hop Prov 13 supply ({world.Provinces[13].SupplyRatio})");
    }

    [Fact]
    public void SupplyGraphSystem_DivisionOvercrowding_DegradesSupplyRatioProportionally()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        // Seoul (Province 0) has high supply
        // 1. One division: 100% supply
        Entity divA = SpawnTestDivision(world, 1, CountryTag.ROK, 0, hardness: 0.50f);
        SupplyGraphSystem.Execute(world);

        Assert.Equal(1.0f, world.Provinces[0].SupplyRatio);

        // 2. Add 50 heavy armored divisions (demanding 150.0 units) to an outpost province (e.g. Prov 6 at Paju)
        for (int i = 0; i < 50; i++)
        {
            SpawnTestDivision(world, 10 + i, CountryTag.ROK, 6, hardness: 0.80f);
        }

        SupplyGraphSystem.Execute(world);

        // Massive demand in Prov 6 causes congestion and throttles SupplyRatio
        float ratioAtPaju = world.Provinces[6].SupplyRatio;
        Assert.True(ratioAtPaju < 1.0f, $"Expected congested province to throttle below 1.0f, but got {ratioAtPaju}");
        Assert.True(ratioAtPaju >= SupplyGraphSystem.MinSupplyRatio);
    }

    [Fact]
    public void SupplyGraphSystem_PortToPortSeaLanes_SuppliesIslandBase()
    {
        using SimulationWorld world = new();

        world.CountryCapitals[CountryTag.USA] = 0;

        for (ushort s = 0; s < 3; s++)
        {
            world.States[s].StateId = s;
            world.States[s].ControllerTag = CountryTag.USA;
            world.States[s].InfrastructureLevel = 5;
        }

        // Mainland Capital & Naval Export Port at Prov 0
        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].StateId = 0;
        world.Provinces[0].ControllerTag = CountryTag.USA;
        world.Provinces[0].PortLevel = 6;
        world.Provinces[0].SupplyHubLevel = 4;
        world.Provinces[0].InfrastructureLevel = 5;

        // Overseas Island Import Port at Prov 20 (separated by sea, no rail connection)
        world.Provinces[20].ProvinceId = 20;
        world.Provinces[20].StateId = 1;
        world.Provinces[20].ControllerTag = CountryTag.USA;
        world.Provinces[20].PortLevel = 4;
        world.Provinces[20].SupplyHubLevel = 2;
        world.Provinces[20].InfrastructureLevel = 4;

        // Island Inland Prov 21 connected to Prov 20 by standard road
        world.Provinces[21].ProvinceId = 21;
        world.Provinces[21].StateId = 2;
        world.Provinces[21].ControllerTag = CountryTag.USA;
        world.Provinces[21].InfrastructureLevel = 4;

        Span<DirectedEdge> edges =
        [
            new DirectedEdge(20, 21, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(21, 20, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces.AsSpan(), edges);

        SpawnTestDivision(world, 50, CountryTag.USA, 21, hardness: 0.50f);

        SupplyGraphSystem.Execute(world);

        // Island province receives convoy-delivered supply through Prov 20 port
        Assert.Equal(1.0f, world.Provinces[21].SupplyRatio);
    }

    [Fact]
    public void SupplyGraphSystem_EmptyProvinces_NoDivisionDivisionByZero_CleanSupplyRatio()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        // Entire map has 0 divisions
        SupplyGraphSystem.Execute(world);

        for (ushort i = 0; i < 120; i++)
        {
            ref readonly ProvinceData prov = ref world.Provinces[i];
            Assert.False(float.IsNaN(prov.SupplyRatio), $"Province {i} had NaN SupplyRatio");
            Assert.False(float.IsInfinity(prov.SupplyRatio), $"Province {i} had Infinity SupplyRatio");
            Assert.True(prov.SupplyRatio >= SupplyGraphSystem.MinSupplyRatio && prov.SupplyRatio <= SupplyGraphSystem.MaxSupplyRatio,
                $"Province {i} had out-of-range SupplyRatio {prov.SupplyRatio}");
        }
    }

    [Fact]
    public void SupplyGraphSystem_AlliedTerritory_CoalitionUnitsShareSupply()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        // Setup US PACOM division stationed in Seoul (Province 0, ROK territory)
        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        world.Countries[CountryTag.USA].Faction = FactionId.BlueforPacom;

        SpawnTestDivision(world, 99, CountryTag.USA, 0, hardness: 0.50f);

        SupplyGraphSystem.Execute(world);

        // US unit in Seoul is supported by the Bluefor coalition hub and receives full supply
        Assert.Equal(1.0f, world.Provinces[0].SupplyRatio);
    }
}
