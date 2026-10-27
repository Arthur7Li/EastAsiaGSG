using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Core.Systems;

namespace EastAsiaGSG.SimTests;

public sealed class SimulationWorldTests
{
    [Fact]
    public unsafe void UnmanagedStructs_HaveExactBlittableSizesAndFixedBufferAccess()
    {
        Assert.Equal(24, sizeof(ProvinceData));
        Assert.Equal(32, sizeof(StateData));
        Assert.Equal(184, sizeof(CountryState));
        Assert.Equal(40, sizeof(StrategicRegionData));
        Assert.Equal(16, sizeof(SimulationCommand));
        Assert.Equal(128, sizeof(DivisionComponent));
        Assert.Equal(64, sizeof(ActiveCombatComponent));

        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<ProvinceData>() == false);
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<StateData>() == false);
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<CountryState>() == false);
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<StrategicRegionData>() == false);
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<SimulationCommand>() == false);
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<DivisionComponent>() == false);
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<ActiveCombatComponent>() == false);

        // Verify CountryTag roster (7 majors + 8 regional minors)
        Assert.Equal(0, CountryTag.PRC);
        Assert.Equal(1, CountryTag.USA);
        Assert.Equal(2, CountryTag.SOV);
        Assert.Equal(3, CountryTag.JPN);
        Assert.Equal(4, CountryTag.ROK);
        Assert.Equal(5, CountryTag.PRK);
        Assert.Equal(6, CountryTag.TWN);
        Assert.Equal(14, CountryTag.MYS);
        Assert.True(CountryTag.TryParse("ROK", out byte rokTag));
        Assert.Equal(CountryTag.ROK, rokTag);
        Assert.Equal("ROK", CountryTag.GetCode(CountryTag.ROK));

        // Verify StateData fixed ushort Resources[4] and CoreTagsMask
        StateData gyeonggi = new()
        {
            StateId = 1,
            OwnerTag = CountryTag.ROK,
            ControllerTag = CountryTag.ROK,
            Population = 15_200_000,
            CivilianFactories = 8,
            MilitaryFactories = 5,
            NavalDockyards = 2,
            AirbaseLevel = 6,
            RadarSamLevel = 3
        };
        gyeonggi.AddCore(CountryTag.ROK);
        gyeonggi.AddCore(CountryTag.PRK);
        gyeonggi.SetResource(ResourceType.Steel, 14);
        gyeonggi.SetResource(ResourceType.Aluminum, 6);
        gyeonggi.SetResource(ResourceType.Semiconductors, 12);

        Assert.True(gyeonggi.HasCore(CountryTag.ROK));
        Assert.True(gyeonggi.HasCore(CountryTag.PRK));
        Assert.False(gyeonggi.HasCore(CountryTag.JPN));
        Assert.Equal(0, gyeonggi.GetResource(ResourceType.Oil));
        Assert.Equal(14, gyeonggi.GetResource(ResourceType.Steel));
        Assert.Equal(6, gyeonggi.GetResource(ResourceType.Aluminum));
        Assert.Equal(12, gyeonggi.GetResource(ResourceType.Semiconductors));

        // Verify CountryState fixed int EquipmentStockpile[32] (8 archetypes x 4 generations)
        CountryState rok = new() { TagId = CountryTag.ROK };
        rok.SetStockpile(EquipmentType.MBT, TechGeneration.Gen1_1980Baseline, 420);
        rok.AddStockpile(EquipmentType.MBT, TechGeneration.Gen2_1988LateColdWar, 180);
        rok.SetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar, 950);

        Assert.Equal(420, rok.GetStockpile(EquipmentType.MBT, TechGeneration.Gen1_1980Baseline));
        Assert.Equal(180, rok.GetStockpile(EquipmentType.MBT, TechGeneration.Gen2_1988LateColdWar));
        Assert.Equal(600, rok.GetTotalArchetypeStockpile(EquipmentType.MBT));
        Assert.Equal(950, rok.GetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar));

        // Verify StrategicRegionData Strait & Amphibious Control Gate (>60% Naval & >50% Air)
        StrategicRegionData taiwanStrait = new() { RegionId = 4, IsSeaZone = 1 };
        taiwanStrait.SetNavalControl(FactionId.PrcSphere, 0.65f);
        taiwanStrait.SetAirSuperiority(FactionId.PrcSphere, 0.48f);
        Assert.False(taiwanStrait.CanCrossStraitOrInvade(FactionId.PrcSphere));

        taiwanStrait.SetAirSuperiority(FactionId.PrcSphere, 0.55f);
        Assert.True(taiwanStrait.CanCrossStraitOrInvade(FactionId.PrcSphere));
    }

    [Fact]
    public void ProvinceGraph_CsrBuildAndLookup_ReturnsExactNeighborsAndEdgeFlags()
    {
        using SimulationWorld world = new();

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(102, 105, AdjacencyEdgeFlags.River),
            new DirectedEdge(101, 102, AdjacencyEdgeFlags.Standard | AdjacencyEdgeFlags.Railway),
            new DirectedEdge(101, 103, AdjacencyEdgeFlags.Strait),
            new DirectedEdge(102, 101, AdjacencyEdgeFlags.Standard | AdjacencyEdgeFlags.Railway)
        ];

        world.Graph.BuildCsr(world.Provinces, edges);

        Assert.Equal(4, world.Graph.EdgeCount);

        ref readonly ProvinceData prov101 = ref world.Provinces[101];
        Assert.Equal(2, prov101.AdjacencyCount);

        ReadOnlySpan<ushort> neighbors101 = world.Graph.GetNeighbors(in prov101);
        Assert.Equal(2, neighbors101.Length);
        Assert.Equal((ushort)102, neighbors101[0]);
        Assert.Equal((ushort)103, neighbors101[1]);

        Assert.True(world.Graph.TryGetEdgeFlags(in prov101, 102, out AdjacencyEdgeFlags flags101To102));
        Assert.Equal(AdjacencyEdgeFlags.Standard | AdjacencyEdgeFlags.Railway, flags101To102);

        Assert.True(world.Graph.TryGetEdgeFlags(in prov101, 103, out AdjacencyEdgeFlags flags101To103));
        Assert.Equal(AdjacencyEdgeFlags.Strait, flags101To103);

        ref readonly ProvinceData prov102 = ref world.Provinces[102];
        Assert.True(world.Graph.TryGetEdgeFlags(in prov102, 105, out AdjacencyEdgeFlags flags102To105));
        Assert.Equal(AdjacencyEdgeFlags.River, flags102To105);
    }

    [Fact]
    public void CommandRingBuffer_DrainsDeterministicallyDuringSixHourTick()
    {
        using SimulationWorld world = new();

        Assert.True(world.EnqueueCommand(SimulationCommand.CreateSelectFocus(CountryTag.JPN, focusId: 7, costDays: 2)));
        Assert.True(world.EnqueueCommand(SimulationCommand.CreateShiftDefcon(CountryTag.USA, targetDefcon: 3)));
        Assert.True(world.EnqueueCommand(SimulationCommand.CreateAssignFactory(CountryTag.ROK, stateOrLineId: 1, factoryCount: 12)));

        // State must not mutate prior to StepSixHours()
        Assert.Equal(-1, world.Countries[CountryTag.JPN].ActiveFocusId);
        Assert.Equal(5, world.Countries[CountryTag.USA].Defcon);
        Assert.Equal(0, world.States[1].AssignedCivFactories);

        // Tick 0 (1985-01-01 00:00 -> 06:00): Drains commands AND runs daily tick #1 (decrementing focus days 2 -> 1)
        world.StepSixHours();

        Assert.Equal(3, world.CommandsProcessedCount);
        Assert.Equal(0, world.CommandQueue.Count);
        Assert.Equal(7, world.Countries[CountryTag.JPN].ActiveFocusId);
        Assert.Equal(1, world.Countries[CountryTag.JPN].FocusDaysRemaining);
        Assert.Equal(3, world.Countries[CountryTag.USA].Defcon);
        Assert.Equal(12, world.States[1].AssignedCivFactories);

        // Advance 3 more ticks to reach 1985-01-02 00:00 and run daily tick #2 (completing the 2-day focus)
        world.StepSixHours(); // 06:00 -> 12:00
        world.StepSixHours(); // 12:00 -> 18:00
        world.StepSixHours(); // 18:00 -> 1985-01-02 00:00
        world.StepSixHours(); // 1985-01-02 00:00 -> 06:00 (Daily tick #2 fires at 00:00)

        Assert.Equal(-1, world.Countries[CountryTag.JPN].ActiveFocusId);
        Assert.Equal(0, world.Countries[CountryTag.JPN].FocusDaysRemaining);
        Assert.True(world.Countries[CountryTag.JPN].HasCompletedFocus(7));
    }

    [Fact]
    public void CombatResolutionSystem_CalculateExchange_MatchesGameDesignSpecFormulas()
    {
        // Attacker: SoftAttack=60, HardAttack=40, Piercing=55, Armor=60
        // Target: Hardness=0.50, Armor=45, Piercing=40, Defense=40
        // PGM Active for Attacker (+35% attack), Attacker Armor (60) > Target Piercing (40) (+40% OrgDamage)
        CombatExchangeResult result = CombatResolutionSystem.CalculateExchange(
            softAttack: 60.0f,
            hardAttack: 40.0f,
            attackerPiercing: 55.0f,
            attackerArmor: 60.0f,
            targetHardness: 0.50f,
            targetArmor: 45.0f,
            targetPiercing: 40.0f,
            targetDefenseOrBreakthrough: 40.0f,
            terrainModifier: 1.0f,
            supplyModifier: 1.0f,
            airModifier: 1.0f,
            pgmActiveForAttacker: true,
            pgmActiveForTarget: false,
            targetIsAttackingSide: false);

        // 1. A_base = 60 * 0.5 + 40 * 0.5 = 50.0
        Assert.Equal(50.0f, result.BaseAttack, precision: 3);

        // 2. A_eff = 50.0 * 1.35 (PGM) * 1.0 (Pierced: 55 >= 45) = 67.5
        Assert.Equal(67.5f, result.EffectiveAttack, precision: 3);
        Assert.True(result.AttackerPiercedTarget);
        Assert.True(result.AttackerArmorUnpiercedByTarget);

        // 3. ExpectedHits = 0.10 * min(67.5, 40.0) + 0.40 * max(0, 67.5 - 40.0) = 4.0 + 11.0 = 15.0
        Assert.Equal(15.0f, result.ExpectedHits, precision: 3);

        // 4. OrgDamage = 15.0 * 0.18 * 1.40 = 3.78; StrengthDamage = 15.0 * 0.04 = 0.60
        Assert.Equal(3.78f, result.OrgDamage, precision: 3);
        Assert.Equal(0.60f, result.StrengthDamage, precision: 3);
    }

    [Fact]
    public void DivisionMovementAndCombat_ResolvesMultiDirectionAttackPgmAndRetreat()
    {
        using SimulationWorld world = new();

        // Setup 4 provinces:
        // 0 (ROK Paju) & 1 (ROK Yeoncheon) -> attack 2 (PRK Kaesong), which can retreat to 3 (PRK Sariwon)
        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].CenterX = 430;
        world.Provinces[0].CenterY = 550;

        world.Provinces[1].OwnerTag = CountryTag.ROK;
        world.Provinces[1].ControllerTag = CountryTag.ROK;
        world.Provinces[1].CenterX = 460;
        world.Provinces[1].CenterY = 540;

        world.Provinces[2].OwnerTag = CountryTag.PRK;
        world.Provinces[2].ControllerTag = CountryTag.PRK;
        world.Provinces[2].CenterX = 420;
        world.Provinces[2].CenterY = 520;

        world.Provinces[3].OwnerTag = CountryTag.PRK;
        world.Provinces[3].ControllerTag = CountryTag.PRK;
        world.Provinces[3].CenterX = 405;
        world.Provinces[3].CenterY = 470;

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(0, 2, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(2, 0, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 2, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(2, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(2, 3, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(3, 2, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        // Give ROK PGM stockpile
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar, 100);

        // Spawn 2 ROK mechanized divisions in provinces 0 and 1, and 1 PRK infantry division in province 2
        Entity rokDiv1 = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 101,
            countryTag: CountryTag.ROK,
            startingProvinceId: 0,
            softAttack: 65f,
            hardAttack: 45f,
            defense: 55f,
            breakthrough: 50f,
            armor: 50f,
            piercing: 55f,
            hardness: 0.60f,
            maxOrganization: 60f));

        Entity rokDiv2 = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 102,
            countryTag: CountryTag.ROK,
            startingProvinceId: 1,
            softAttack: 60f,
            hardAttack: 40f,
            defense: 50f,
            breakthrough: 45f,
            armor: 45f,
            piercing: 50f,
            hardness: 0.55f,
            maxOrganization: 60f));

        Entity prkDef = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 201,
            countryTag: CountryTag.PRK,
            startingProvinceId: 2,
            softAttack: 35f,
            hardAttack: 15f,
            defense: 40f,
            breakthrough: 25f,
            armor: 15f,
            piercing: 25f,
            hardness: 0.15f,
            maxOrganization: 30f));

        // Authorize PGM on ROK Division 101 and order both ROK divisions to attack Province 2
        world.EnqueueCommand(SimulationCommand.CreateTogglePgm(CountryTag.ROK, divisionEntityId: 101, authorized: true));
        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 101, targetProvinceId: 2));
        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 102, targetProvinceId: 2));

        // First 6-hour tick: commands drain, combat spawns from 2 directions (width = 80 + 20 = 100), and first exchange resolves
        world.StepSixHours();

        Assert.True(world.TryFindActiveCombatAtProvince(2, out Entity combatEntity));
        ref ActiveCombatComponent combat = ref world.EcsWorld.Get<ActiveCombatComponent>(combatEntity);
        Assert.Equal(2, combat.AttackDirectionCount);
        Assert.Equal(100, combat.TotalCombatWidth);
        Assert.Equal(40, combat.UsedAttackerWidth);
        Assert.Equal(20, combat.UsedDefenderWidth);

        // Verify PGM stockpile was consumed by ROK Division 101
        int remainingPgm = world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar);
        Assert.Equal(100 - CombatResolutionSystem.PgmUnitsConsumedPerSixHourTick, remainingPgm);

        // Advance ticks until PRK defender's organization drops to 0 and it retreats to Province 3
        for (int t = 0; t < 15; t++)
        {
            ref DivisionComponent defState = ref world.EcsWorld.Get<DivisionComponent>(prkDef);
            if (defState.CurrentProvinceId == 3)
            {
                break;
            }
            world.StepSixHours();
        }

        ref DivisionComponent finalDef = ref world.EcsWorld.Get<DivisionComponent>(prkDef);
        ref DivisionComponent finalAtt1 = ref world.EcsWorld.Get<DivisionComponent>(rokDiv1);
        Assert.Equal((ushort)3, finalDef.CurrentProvinceId);
        Assert.True(finalDef.Strength > 0.50f);
        Assert.Equal((ushort)2, finalAtt1.CurrentProvinceId);
        Assert.Equal(CountryTag.ROK, world.Provinces[2].ControllerTag);
    }

    [Fact]
    public void StepSixHours_FullYear1460Ticks_ExactDateProgressionAndUnder5Ms()
    {
        using SimulationWorld world = new();
        PopulateMockEastAsiaGraph(world);

        // Warm up JIT with a standalone world so benchmark measures steady-state native execution
        using (SimulationWorld warmupWorld = new())
        {
            PopulateMockEastAsiaGraph(warmupWorld);
            for (int i = 0; i < 64; i++)
            {
                warmupWorld.StepSixHours();
            }
        }

        Assert.Equal(1985, world.CurrentYear);
        Assert.Equal(1, world.CurrentMonth);
        Assert.Equal(1, world.CurrentDay);
        Assert.Equal(0, world.CurrentHour);

        const int ticksPerYear = 1460; // 365 days * 4 six-hour ticks/day
        Stopwatch sw = Stopwatch.StartNew();

        for (int tick = 0; tick < ticksPerYear; tick++)
        {
            world.StepSixHours();
        }

        sw.Stop();
        double elapsedMs = sw.Elapsed.TotalMilliseconds;

        // Verify exact calendar progression: 1985-01-01 00:00 -> 1986-01-01 00:00
        Assert.Equal(ticksPerYear, world.TotalSixHourTicks);
        Assert.Equal(365, world.DailyTicksExecuted);
        Assert.Equal(12, world.MonthlyTicksExecuted);
        Assert.Equal(1986, world.CurrentYear);
        Assert.Equal(1, world.CurrentMonth);
        Assert.Equal(1, world.CurrentDay);
        Assert.Equal(0, world.CurrentHour);

        // Verify daily system progression across all 15 active nations
        Assert.Equal(365.0f, world.Countries[CountryTag.PRC].PoliticalPower);
        Assert.Equal(365.0f, world.Countries[CountryTag.USA].PoliticalPower);
        Assert.Equal(15, world.Countries[CountryTag.USA].HomelandEscalation);

        // Verify performance budget (< 5.0 ms for 1,460 six-hour ticks)
        Assert.True(
            elapsedMs < 5.0,
            $"Expected 1,460 ticks (1 year) to complete in < 5.0 ms, but took {elapsedMs:F3} ms.");
    }

    private static void PopulateMockEastAsiaGraph(SimulationWorld world)
    {
        const int mockProvinceCount = 120;
        DirectedEdge[] edges = new DirectedEdge[mockProvinceCount * 4];
        int edgeIdx = 0;

        for (ushort p = 0; p < mockProvinceCount; p++)
        {
            world.Provinces[p].StateId = (ushort)(p / 8);
            world.Provinces[p].OwnerTag = p < 60 ? CountryTag.ROK : CountryTag.PRK;
            world.Provinces[p].ControllerTag = world.Provinces[p].OwnerTag;
            world.Provinces[p].Terrain = (TerrainType)(p % 5);
            world.Provinces[p].InfrastructureLevel = 3;

            ushort next = (ushort)((p + 1) % mockProvinceCount);
            ushort prev = (ushort)((p + mockProvinceCount - 1) % mockProvinceCount);
            ushort across = (ushort)((p + 10) % mockProvinceCount);
            ushort rail = (ushort)((p + 5) % mockProvinceCount);

            edges[edgeIdx++] = new DirectedEdge(p, next, AdjacencyEdgeFlags.Standard);
            edges[edgeIdx++] = new DirectedEdge(p, prev, AdjacencyEdgeFlags.Standard);
            edges[edgeIdx++] = new DirectedEdge(p, across, AdjacencyEdgeFlags.River);
            edges[edgeIdx++] = new DirectedEdge(p, rail, AdjacencyEdgeFlags.Standard | AdjacencyEdgeFlags.Railway);
        }

        world.Graph.BuildCsr(world.Provinces, edges);
    }
}
