using System.Diagnostics;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Loaders;
using EastAsiaGSG.Data.Serialization;

namespace EastAsiaGSG.SimTests;

public sealed class WorldSnapshotSerializerTests
{
    private static string FindRepoRoot()
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

    [Fact]
    public void SaveAndLoad_BitExactRoundtrip_RestoresAllProvincesStatesCountriesAndStockpiles()
    {
        using SimulationWorld world = new();
        string repoRoot = FindRepoRoot();

        JsonMapLoader.LoadMap(
            world,
            Path.Combine(repoRoot, "data", "map", "east_asia_1985_states.json"),
            Path.Combine(repoRoot, "data", "map", "east_asia_1985_provinces.json"));

        // Mutate some states and scalars to simulate mid-game play
        world.StepSixHours();
        world.StepSixHours();
        world.Provinces[0].ControllerTag = CountryTag.PRK;
        world.Provinces[0].SupplyRatio = 0.42f;
        world.States[0].AssignedCivFactories = 7;
        world.Countries[CountryTag.ROK].Stability = 0.88f;
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.MBT, TechGeneration.Gen2_1988LateColdWar, 1337);
        world.CountryWarMasks[CountryTag.ROK] = 1 << CountryTag.PRK;
        world.CountryWarMasks[CountryTag.PRK] = 1 << CountryTag.ROK;
        world.CountryCapitulatedMask = 1 << CountryTag.LAO;
        world.AiControlledCountryMask = 0b10101;
        world.CountryDoctrines[CountryTag.USA] = TacticalDoctrine.AirLandBattle;
        world.SeaZoneInterdictionPenalty[3] = 0.65f;
        world.RecordNewsEvent("TEST_EVENT_HASH".GetHashCode(), CountryTag.USA);

        using MemoryStream ms = new();
        WorldSnapshotSerializer.SaveToStream(world, ms);
        Assert.True(ms.Length > 0);

        // Load into a new world instance
        ms.Position = 0;
        using SimulationWorld restored = new();
        WorldSnapshotSerializer.LoadFromStream(restored, ms);

        // Verify Scalars
        Assert.Equal(world.TotalSixHourTicks, restored.TotalSixHourTicks);
        Assert.Equal(world.CurrentYear, restored.CurrentYear);
        Assert.Equal(world.CurrentMonth, restored.CurrentMonth);
        Assert.Equal(world.CurrentDay, restored.CurrentDay);
        Assert.Equal(world.CurrentHour, restored.CurrentHour);
        Assert.Equal(world.CountryCapitulatedMask, restored.CountryCapitulatedMask);
        Assert.Equal(world.AiControlledCountryMask, restored.AiControlledCountryMask);
        Assert.Equal(world.TriggeredNewsEventCount, restored.TriggeredNewsEventCount);

        // Verify Provinces
        Assert.Equal(CountryTag.PRK, restored.Provinces[0].ControllerTag);
        Assert.Equal(0.42f, restored.Provinces[0].SupplyRatio);

        // Verify States
        Assert.Equal(7, restored.States[0].AssignedCivFactories);

        // Verify Countries & Stockpiles
        Assert.Equal(0.88f, restored.Countries[CountryTag.ROK].Stability);
        Assert.Equal(1337, restored.Countries[CountryTag.ROK].GetStockpile(EquipmentType.MBT, TechGeneration.Gen2_1988LateColdWar));
        Assert.Equal(world.CountryWarMasks[CountryTag.ROK], restored.CountryWarMasks[CountryTag.ROK]);
        Assert.Equal(TacticalDoctrine.AirLandBattle, restored.CountryDoctrines[CountryTag.USA]);
        Assert.Equal(0.65f, restored.SeaZoneInterdictionPenalty[3]);

        // Verify Graph Adjacencies
        Assert.Equal(world.AdjacencyTargets.Length, restored.AdjacencyTargets.Length);
        Assert.Equal(world.AdjacencyEdgeFlags.Length, restored.AdjacencyEdgeFlags.Length);
        for (int i = 0; i < world.AdjacencyTargets.Length; i++)
        {
            Assert.Equal(world.AdjacencyTargets[i], restored.AdjacencyTargets[i]);
            Assert.Equal(world.AdjacencyEdgeFlags[i], restored.AdjacencyEdgeFlags[i]);
        }
    }

    [Fact]
    public void SaveAndLoad_DynamicEntities_RestoresAllDivisionsCombatsTaskForcesAndProductionLines()
    {
        using SimulationWorld world = new();

        // Spawn dynamic ECS entities
        DivisionComponent div = DivisionComponent.Create(
            divisionId: 42,
            countryTag: CountryTag.ROK,
            startingProvinceId: 10,
            templateId: 1,
            startingExperience: 0.75f,
            pgmAuthorized: true);
        div.Organization = 45.5f;
        div.Strength = 0.92f;
        world.SpawnDivision(in div);

        ActiveCombatComponent combat = ActiveCombatComponent.Create(
            combatId: 99,
            targetProvinceId: 50,
            primaryOriginProvinceId: 49,
            attackerTag: CountryTag.USA,
            defenderTag: CountryTag.SOV);
        combat.BattleProgress = 62.5f;
        combat.AttackerDoctrine = TacticalDoctrine.AirLandBattle;
        world.EcsWorld.Create(combat);

        ProductionLineComponent line = ProductionLineComponent.Create(
            lineId: 777,
            countryTag: CountryTag.JPN,
            archetype: EquipmentType.TacticalAir,
            generation: TechGeneration.Gen2_1988LateColdWar,
            assignedMilitaryFactories: 15,
            initialEfficiency: 0.68f);
        world.SpawnProductionLine(in line);

        NavalTaskForceComponent tf = NavalTaskForceComponent.CreateCarrierStrikeGroup(
            taskForceId: 88,
            countryTag: CountryTag.USA,
            assignedRegionId: 2,
            homeBaseProvinceId: 500,
            generation: TechGeneration.Gen2_1988LateColdWar);
        tf.Mission = NavalMission.Patrol;
        world.SpawnNavalTaskForce(in tf);

        AirWingComponent wing = AirWingComponent.CreateCasWing(
            wingId: 999,
            countryTag: CountryTag.ROK,
            assignedRegionId: 7,
            baseStateId: 0,
            aircraftCount: 100);
        world.SpawnAirWing(in wing);

        using MemoryStream ms = new();
        WorldSnapshotSerializer.SaveToStream(world, ms);

        // Load into fresh world
        ms.Position = 0;
        using SimulationWorld restored = new();
        WorldSnapshotSerializer.LoadFromStream(restored, ms);

        // Verify Division Entity & Spatial Index Parity
        Assert.Equal(1, restored.SpatialIndex.GetDivisionCountInProvince(10));
        ReadOnlySpan<SpatialDivisionItem> items = restored.SpatialIndex.GetDivisionsInProvince(10);
        Assert.Equal(1, items.Length);
        Assert.Equal(42, items[0].DivisionId);
        Assert.Equal(CountryTag.ROK, items[0].CountryTag);
        Assert.Equal(45.5f, items[0].Organization);

        // Query entities in restored world
        int divCount = 0;
        int combatCount = 0;
        int lineCount = 0;
        int tfCount = 0;
        int wingCount = 0;

        foreach (ref Arch.Core.Chunk chunk in restored.EcsWorld.Query(new Arch.Core.QueryDescription().WithAll<DivisionComponent>()))
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                divCount++;
                Assert.Equal(42, divs[i].DivisionId);
                Assert.Equal(CountryTag.ROK, divs[i].CountryTag);
                Assert.Equal(45.5f, divs[i].Organization);
                Assert.Equal(0.92f, divs[i].Strength);
                Assert.True(divs[i].IsPgmAuthorized);
            }
        }
        Assert.Equal(1, divCount);

        foreach (ref Arch.Core.Chunk chunk in restored.EcsWorld.Query(new Arch.Core.QueryDescription().WithAll<ActiveCombatComponent>()))
        {
            Span<ActiveCombatComponent> combats = chunk.GetSpan<ActiveCombatComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                combatCount++;
                Assert.Equal(99, combats[i].CombatId);
                Assert.Equal(62.5f, combats[i].BattleProgress);
                Assert.Equal(TacticalDoctrine.AirLandBattle, combats[i].AttackerDoctrine);
            }
        }
        Assert.Equal(1, combatCount);

        foreach (ref Arch.Core.Chunk chunk in restored.EcsWorld.Query(new Arch.Core.QueryDescription().WithAll<ProductionLineComponent>()))
        {
            Span<ProductionLineComponent> lines = chunk.GetSpan<ProductionLineComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                lineCount++;
                Assert.Equal(777, lines[i].LineId);
                Assert.Equal(CountryTag.JPN, lines[i].CountryTag);
                Assert.Equal(EquipmentType.TacticalAir, lines[i].Archetype);
                Assert.Equal(15, lines[i].AssignedMilitaryFactories);
            }
        }
        Assert.Equal(1, lineCount);

        foreach (ref Arch.Core.Chunk chunk in restored.EcsWorld.Query(new Arch.Core.QueryDescription().WithAll<NavalTaskForceComponent>()))
        {
            Span<NavalTaskForceComponent> tfs = chunk.GetSpan<NavalTaskForceComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                tfCount++;
                Assert.Equal(88, tfs[i].TaskForceId);
                Assert.Equal(NavalMission.Patrol, tfs[i].Mission);
                Assert.Equal(2, tfs[i].AssignedRegionId);
            }
        }
        Assert.Equal(1, tfCount);

        foreach (ref Arch.Core.Chunk chunk in restored.EcsWorld.Query(new Arch.Core.QueryDescription().WithAll<AirWingComponent>()))
        {
            Span<AirWingComponent> wings = chunk.GetSpan<AirWingComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                wingCount++;
                Assert.Equal(999, wings[i].WingId);
                Assert.Equal(AirMission.CloseAirSupport, wings[i].Mission);
            }
        }
        Assert.Equal(1, wingCount);
    }

    [Fact]
    public void SaveAndLoad_PerformanceBenchmark_RoundtripsUnder5Milliseconds()
    {
        using SimulationWorld world = new();
        string repoRoot = FindRepoRoot();

        JsonMapLoader.LoadMap(
            world,
            Path.Combine(repoRoot, "data", "map", "east_asia_1985_states.json"),
            Path.Combine(repoRoot, "data", "map", "east_asia_1985_provinces.json"));

        // Populate with ~50 divisions and 10 production lines
        for (int i = 0; i < 50; i++)
        {
            DivisionComponent div = DivisionComponent.Create(
                divisionId: 1000 + i,
                countryTag: (byte)(i % 7),
                startingProvinceId: (ushort)(i * 4),
                templateId: 1,
                startingExperience: 0.5f,
                pgmAuthorized: true);
            world.SpawnDivision(in div);
        }

        for (int i = 0; i < 10; i++)
        {
            ProductionLineComponent line = ProductionLineComponent.Create(
                lineId: 2000 + i,
                countryTag: (byte)(i % 7),
                archetype: (EquipmentType)(i % 8),
                generation: TechGeneration.Gen1_1980Baseline,
                assignedMilitaryFactories: 10,
                initialEfficiency: 0.5f);
            world.SpawnProductionLine(in line);
        }

        using MemoryStream ms = new();

        // Warmup JIT
        WorldSnapshotSerializer.SaveToStream(world, ms);
        ms.Position = 0;
        using SimulationWorld warmupWorld = new();
        WorldSnapshotSerializer.LoadFromStream(warmupWorld, ms);

        // Benchmark Save + Load roundtrip over 10 iterations
        const int iterations = 10;
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            ms.SetLength(0);
            WorldSnapshotSerializer.SaveToStream(world, ms);
            ms.Position = 0;
            WorldSnapshotSerializer.LoadFromStream(warmupWorld, ms);
        }

        sw.Stop();
        double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 10.0, $"Average Save/Load roundtrip must be < 10 ms (target < 5 ms), got {avgMs:F2} ms");
    }

    [Fact]
    public void Load_CorruptedOrInvalidHeader_ThrowsInvalidDataException()
    {
        using SimulationWorld world = new();
        byte[] invalidData = new byte[64];
        // Fill with random/zeros (not EAGSAV01)
        using MemoryStream ms = new(invalidData);

        Assert.Throws<InvalidDataException>(() => WorldSnapshotSerializer.LoadFromStream(world, ms));
    }

    [Fact]
    public void SaveToFile_LoadFromFile_RoundtripsSuccessfully()
    {
        using SimulationWorld world = new();
        world.Countries[CountryTag.ROK].Stability = 0.95f;

        string tempPath = Path.Combine(Path.GetTempPath(), $"east_asia_test_save_{Guid.NewGuid():N}.eagsav");
        try
        {
            WorldSnapshotSerializer.SaveToFile(world, tempPath);
            Assert.True(File.Exists(tempPath));

            using SimulationWorld restored = new();
            WorldSnapshotSerializer.LoadFromFile(restored, tempPath);
            Assert.Equal(0.95f, restored.Countries[CountryTag.ROK].Stability);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
