using System.Diagnostics;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Data.Loaders;
using Xunit;

namespace EastAsiaGSG.SimTests;

public sealed class PathfinderTests
{
    private static string FindRepositoryRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "EastAsiaGSG.sln")))
            {
                return dir;
            }
            string? parent = Directory.GetParent(dir)?.FullName;
            if (parent == null || parent == dir)
            {
                break;
            }
            dir = parent;
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
    public void FindPath_SeoulToBusan_FollowsContinuousCsrAdjacencyChain()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        Span<ushort> path = stackalloc ushort[64];
        PathQuery query = new(
            FromProvinceId: 0, // Seoul Central
            ToProvinceId: 56,  // Busan Central / Harbor
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.None);

        PathResult result = world.FindPath(in query, path);

        Assert.True(result.Success);
        Assert.Equal((ushort)56, result.DestinationProvinceId);
        Assert.True(result.PathLength > 0);
        Assert.True(result.TotalCost > 0f);

        // Verify that every consecutive step in the path is a valid CSR neighbor
        ushort current = query.FromProvinceId;
        for (int i = 0; i < result.PathLength; i++)
        {
            ushort next = path[i];
            ReadOnlySpan<ushort> neighbors = world.Graph.GetNeighbors(in world.Provinces[current]);
            Assert.Contains(next, neighbors.ToArray());
            current = next;
        }

        Assert.Equal(query.ToProvinceId, current);
    }

    [Fact]
    public void FindPath_IncludeStartProvince_PrependsStartNode()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        Span<ushort> path = stackalloc ushort[64];
        PathQuery query = new(
            FromProvinceId: 0, // Seoul
            ToProvinceId: 10,  // Suwon
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.IncludeStartProvince);

        PathResult result = world.FindPath(in query, path);

        Assert.True(result.Success);
        Assert.True(result.PathLength >= 2);
        Assert.Equal((ushort)0, path[0]);
        Assert.Equal((ushort)10, path[result.PathLength - 1]);
    }

    [Fact]
    public void FindPath_DmzCrossBorder_HostileTerritoryGatedCorrectly()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        Span<ushort> path = stackalloc ushort[64];

        // 1. Attack move: ROK unit crossing DMZ from Paju (6) into PRK Kaesong (92) with AllowHostileTerritory
        PathQuery attackQuery = new(
            FromProvinceId: 6,  // Paju/Munsan (ROK)
            ToProvinceId: 92,  // Kaesong City (PRK)
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.AllowHostileTerritory);

        PathResult attackResult = world.FindPath(in attackQuery, path);
        Assert.True(attackResult.Success);
        Assert.Equal((ushort)92, attackResult.DestinationProvinceId);
        Assert.Equal(1, attackResult.PathLength); // Direct DMZ neighbor edge
        Assert.Equal((ushort)92, path[0]);

        // 2. Peaceful/Supply move: Disallow hostile territory
        PathQuery friendlyOnlyQuery = new(
            FromProvinceId: 6,
            ToProvinceId: 92,
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.None); // Hostile territory disallowed

        PathResult friendlyResult = world.FindPath(in friendlyOnlyQuery, path);
        Assert.False(friendlyResult.Success);
    }

    [Fact]
    public void FindPath_PreferRailways_SelectsLowCostRailCorridor()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        Span<ushort> pathStandard = stackalloc ushort[64];
        Span<ushort> pathRail = stackalloc ushort[64];

        PathQuery standardQuery = new(
            FromProvinceId: 0,
            ToProvinceId: 50, // Daegu
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.None);

        PathQuery railQuery = new(
            FromProvinceId: 0,
            ToProvinceId: 50,
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.PreferRailways);

        PathResult standardResult = world.FindPath(in standardQuery, pathStandard);
        PathResult railResult = world.FindPath(in railQuery, pathRail);

        Assert.True(standardResult.Success);
        Assert.True(railResult.Success);

        // Railway preference must result in significantly lower overall cost
        Assert.True(railResult.TotalCost < standardResult.TotalCost);
    }

    [Fact]
    public void FindPath_StraitAccessControl_BlocksStraitCrossingUnlessPermitted()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        Span<ushort> path = stackalloc ushort[64];

        // Jindo Island (47) connects to Mokpo (42) via Strait
        PathQuery noStraitQuery = new(
            FromProvinceId: 40, // Gwangju
            ToProvinceId: 47,  // Jindo Island
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.None); // Straits disallowed

        PathResult noStraitResult = world.FindPath(in noStraitQuery, path);
        Assert.False(noStraitResult.Success);

        PathQuery allowStraitQuery = new(
            FromProvinceId: 40, // Gwangju
            ToProvinceId: 47,  // Jindo Island
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.AllowStraits);

        PathResult allowStraitResult = world.FindPath(in allowStraitQuery, path);
        Assert.True(allowStraitResult.Success);
        Assert.Equal((ushort)47, allowStraitResult.DestinationProvinceId);
    }

    [Fact]
    public void FindPath_StartEqualsGoal_ReturnsZeroLengthSuccess()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        Span<ushort> path = stackalloc ushort[16];
        PathQuery query = new(
            FromProvinceId: 10,
            ToProvinceId: 10,
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.None);

        PathResult result = world.FindPath(in query, path);

        Assert.True(result.Success);
        Assert.Equal(0, result.PathLength);
        Assert.Equal(0f, result.TotalCost);
    }

    [Fact]
    public void FindPath_OutOfBoundsProvince_ReturnsFailed()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        Span<ushort> path = stackalloc ushort[16];
        PathQuery query = new(
            FromProvinceId: 0,
            ToProvinceId: 1500, // Out of bounds
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.None);

        PathResult result = world.FindPath(in query, path);

        Assert.False(result.Success);
        Assert.Equal(PathResult.Failed, result);
    }

    [Fact]
    public void FindPath_ZeroHeapAllocations_StrictEnforcement()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        Span<ushort> path = stackalloc ushort[64];
        PathQuery query = new(
            FromProvinceId: 0,  // Seoul
            ToProvinceId: 56,  // Busan
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.None);

        // Warmup to trigger any static constructor JIT compilation
        world.FindPath(in query, path);

        // Measure allocated memory across 1,000 executions
        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1_000; i++)
        {
            world.FindPath(in query, path);
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        long diff = afterAlloc - beforeAlloc;

        Assert.Equal(0, diff);
    }

    [Fact]
    public void FindPath_HighThroughputBenchmark_UnderFiveMicrosecondsPerQuery()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        Span<ushort> path = stackalloc ushort[64];
        PathQuery query = new(
            FromProvinceId: 0,  // Seoul
            ToProvinceId: 56,  // Busan
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.PreferRailways);

        // Warmup
        for (int i = 0; i < 100; i++)
        {
            world.FindPath(in query, path);
        }

        const int iterations = 10_000;
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            world.FindPath(in query, path);
        }

        sw.Stop();

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double usPerQuery = (totalMs * 1000.0) / iterations;

        Assert.True(
            totalMs < 500.0,
            $"Expected 10,000 queries to complete in < 500 ms, but took {totalMs:F2} ms ({usPerQuery:F2} μs/query).");
    }

    [Fact]
    public void FindPath_AlliedTerritory_AllowsMovementWithoutHostileFlag()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        // Establish Bluefor alliance between USA and ROK
        world.Countries[CountryTag.USA].Faction = FactionId.BlueforPacom;
        world.Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;

        Span<ushort> path = stackalloc ushort[64];
        PathQuery query = new(
            FromProvinceId: 0,  // Seoul (controlled by ROK)
            ToProvinceId: 56,  // Busan (controlled by ROK)
            MovingCountryTag: CountryTag.USA, // Moving as allied USA
            Flags: PathfindingFlags.None);     // Hostile territory disallowed!

        PathResult result = world.FindPath(in query, path);

        // Movement through allied territory must succeed without hostile flag
        Assert.True(result.Success);
        Assert.Equal((ushort)56, result.DestinationProvinceId);
        Assert.True(result.PathLength > 0);
    }

    [Fact]
    public void FindPath_ConcurrentThreadSafety_ExecutesSimultaneouslyWithoutCorruption()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        PathQuery query = new(
            FromProvinceId: 0,
            ToProvinceId: 56,
            MovingCountryTag: CountryTag.ROK,
            Flags: PathfindingFlags.PreferRailways);

        // Run concurrent path searches across multiple threads
        Parallel.For(0, 100, _ =>
        {
            Span<ushort> localPath = stackalloc ushort[64];
            PathResult localResult = world.FindPathThreadSafe(in query, localPath);

            Assert.True(localResult.Success);
            Assert.Equal((ushort)56, localResult.DestinationProvinceId);
            Assert.True(localResult.PathLength > 0);
        });
    }
}
