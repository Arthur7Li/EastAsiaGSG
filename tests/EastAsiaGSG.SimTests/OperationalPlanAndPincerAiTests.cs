using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Core.Systems;

namespace EastAsiaGSG.SimTests;

public sealed class OperationalPlanAndPincerAiTests
{
    [Fact]
    public void OperationalPlanSystem_AssignsSchwerpunktToArmoredTemplates_AndDefensiveScreenToInfantry()
    {
        using SimulationWorld world = new();

        world.CountryCapitals[CountryTag.PRK] = 2; // Pyongyang capital
        world.Provinces[2].ControllerTag = CountryTag.PRK;
        world.Provinces[2].VictoryPoints = 20;

        world.DeclareWar(CountryTag.ROK, CountryTag.PRK);

        // Foot infantry division (low hardness, low breakthrough)
        Entity infEntity = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 101,
            countryTag: CountryTag.ROK,
            startingProvinceId: 0,
            hardness: 0.10f,
            breakthrough: 20.0f,
            armor: 10.0f));

        // Mechanized/armored division (high hardness, high breakthrough, high armor)
        Entity armorEntity = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 102,
            countryTag: CountryTag.ROK,
            startingProvinceId: 1,
            hardness: 0.40f,
            breakthrough: 40.0f,
            armor: 30.0f));

        world.SpatialIndex.Rebuild(world);
        OperationalPlanSystem.EvaluateCountryOperationalPlan(world, CountryTag.ROK);

        ref readonly DivisionComponent inf = ref world.EcsWorld.Get<DivisionComponent>(infEntity);
        ref readonly DivisionComponent armor = ref world.EcsWorld.Get<DivisionComponent>(armorEntity);

        Assert.Equal(OperationalOrderStance.DefensiveScreen, inf.OperationalStance);
        Assert.Equal(DivisionComponent.NoProvince, inf.ObjectiveProvinceId);

        Assert.Equal(OperationalOrderStance.Schwerpunkt, armor.OperationalStance);
        Assert.Equal((ushort)2, armor.ObjectiveProvinceId);
        Assert.True(armor.IsSchwerpunkt);
    }

    [Fact]
    public void TacticalFrontlineAI_ExecutesMultiProvinceCoordinatedPincerAttack_ExpandingCombatWidth()
    {
        using SimulationWorld world = new();

        // 3 provinces:
        // Prov 0 (ROK West)  <-> Prov 2 (PRK Fortress)
        // Prov 1 (ROK East)  <-> Prov 2 (PRK Fortress)
        for (ushort p = 0; p < 3; p++)
        {
            byte tag = p < 2 ? CountryTag.ROK : CountryTag.PRK;
            world.Provinces[p].ProvinceId = p;
            world.Provinces[p].OwnerTag = tag;
            world.Provinces[p].ControllerTag = tag;
            world.Provinces[p].Terrain = TerrainType.Plains;
            world.Provinces[p].SupplyRatio = 1.0f;
            world.Provinces[p].SupplyHubLevel = 2;
        }

        DirectedEdge[] edges =
        [
            new DirectedEdge(0, 2, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(2, 0, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 2, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(2, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        world.DeclareWar(CountryTag.ROK, CountryTag.PRK);

        // Spawn attacking divisions in Prov 0 and Prov 1
        Entity rok1 = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 101,
            countryTag: CountryTag.ROK,
            startingProvinceId: 0,
            softAttack: 60.0f,
            defense: 50.0f));

        Entity rok2 = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 102,
            countryTag: CountryTag.ROK,
            startingProvinceId: 1,
            softAttack: 60.0f,
            defense: 50.0f));

        // Spawn defender in Prov 2
        Entity prk = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 201,
            countryTag: CountryTag.PRK,
            startingProvinceId: 2,
            softAttack: 20.0f,
            defense: 40.0f));

        world.SpatialIndex.Rebuild(world);

        // Evaluate Tactical Frontline AI for ROK
        TacticalFrontlineAISystem.EvaluateCountryFrontline(world, CountryTag.ROK);

        // Assert that move orders were queued for both Division 101 and Division 102 into Province 2
        Assert.True(world.CommandQueue.Count >= 2);

        // Advance 1 six-hour tick: drains commands, moves divisions, and initiates combat
        world.StepSixHours();

        ref readonly DivisionComponent d1 = ref world.EcsWorld.Get<DivisionComponent>(rok1);
        ref readonly DivisionComponent d2 = ref world.EcsWorld.Get<DivisionComponent>(rok2);

        Assert.Equal(DivisionState.Attacking, d1.State);
        Assert.Equal(DivisionState.Attacking, d2.State);
        Assert.Equal((ushort)2, d1.TargetProvinceId);
        Assert.Equal((ushort)2, d2.TargetProvinceId);

        // Verify active combat at Province 2 has expanded combat width (80 base + 20 for 2nd direction = 100)
        Assert.True(world.TryFindActiveCombatAtProvince(2, out Entity combatEntity));
        ref readonly ActiveCombatComponent combat = ref world.EcsWorld.Get<ActiveCombatComponent>(combatEntity);
        Assert.Equal(100, combat.TotalCombatWidth);
        Assert.Equal(2, combat.AttackDirectionCount);
    }

    [Fact]
    public void TacticalFrontlineAI_EntrenchmentHysteresis_EntrenchedUnitsHoldDefensiveRedoubt()
    {
        using SimulationWorld world = new();

        // 2 provinces: Prov 0 (ROK Mountain Fort) <-> Prov 1 (PRK Plains)
        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].Terrain = TerrainType.Mountain;
        world.Provinces[0].SupplyRatio = 1.0f;

        world.Provinces[1].ProvinceId = 1;
        world.Provinces[1].OwnerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;
        world.Provinces[1].Terrain = TerrainType.Plains;
        world.Provinces[1].SupplyRatio = 1.0f;

        DirectedEdge[] edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);
        world.DeclareWar(CountryTag.ROK, CountryTag.PRK);

        // ROK division has built 45% entrenchment in the mountain
        DivisionComponent rokDiv = DivisionComponent.Create(
            divisionId: 101,
            countryTag: CountryTag.ROK,
            startingProvinceId: 0,
            softAttack: 40.0f,
            defense: 70.0f);
        rokDiv.Entrenchment = 0.45f;
        rokDiv.OperationalStance = OperationalOrderStance.DefensiveScreen;
        world.SpawnDivision(rokDiv);

        // PRK division in Prov 1 with parity defense (not an overwhelming blowout)
        DivisionComponent prkDiv = DivisionComponent.Create(
            divisionId: 201,
            countryTag: CountryTag.PRK,
            startingProvinceId: 1,
            softAttack: 35.0f,
            defense: 50.0f);
        world.SpawnDivision(prkDiv);

        world.SpatialIndex.Rebuild(world);

        // Evaluate Frontline AI: ROK division should hold line and NOT abandon its redoubt
        TacticalFrontlineAISystem.EvaluateCountryFrontline(world, CountryTag.ROK);

        Assert.Equal(0, world.CommandQueue.Count);
    }

    [Fact]
    public void StrategicMinisterAI_AutonomouslyRecruitsDivision_WhenManpowerAndEquipmentAvailable()
    {
        using SimulationWorld world = new();

        world.CountryCapitals[CountryTag.ROK] = 5;
        world.Provinces[5].ControllerTag = CountryTag.ROK;
        world.Provinces[5].Terrain = TerrainType.Plains;

        ref CountryState rok = ref world.Countries[CountryTag.ROK];
        rok.Manpower = 50_000;
        rok.SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline, 2_000);
        rok.SetStockpile(EquipmentType.Artillery, TechGeneration.Gen1_1980Baseline, 200);

        world.SpatialIndex.Rebuild(world);
        StrategicMinisterAISystem.EvaluateAndRecruitDivisions(world, CountryTag.ROK);

        Assert.True(world.CommandQueue.Count > 0);
        Assert.True(world.CommandQueue.TryDequeue(out SimulationCommand cmd));
        Assert.Equal(CommandType.RecruitDivision, cmd.Type);
        Assert.Equal(CountryTag.ROK, cmd.CountryTag);
        Assert.Equal(5, cmd.IntParam0); // Spawn at capital
    }

    [Fact]
    public void StrategicMinisterAI_AutonomouslyDeclaresWar_WhenWargoalExistsAndReadinessHigh()
    {
        using SimulationWorld world = new();

        world.GrantWargoal(CountryTag.ROK, CountryTag.PRK);
        Assert.True(world.HasWargoal(CountryTag.ROK, CountryTag.PRK));
        Assert.False(world.IsAtWarWith(CountryTag.ROK, CountryTag.PRK));

        // Grant ROK DEFCON 1 general war authorization
        ref CountryState rok = ref world.Countries[CountryTag.ROK];
        rok.Defcon = 1;
        rok.WarSupport = 0.85f;

        // Spawn ROK division so military readiness is non-zero
        world.SpawnDivision(DivisionComponent.Create(101, CountryTag.ROK, startingProvinceId: 0));
        world.SpatialIndex.Rebuild(world);

        StrategicMinisterAISystem.EvaluateAndExecuteWargoals(world, CountryTag.ROK);

        Assert.True(world.CommandQueue.Count > 0);
        Assert.True(world.CommandQueue.TryDequeue(out SimulationCommand cmd));
        Assert.Equal(CommandType.DeclareWar, cmd.Type);
        Assert.Equal(CountryTag.ROK, cmd.CountryTag);
        Assert.Equal(CountryTag.PRK, cmd.TargetId);
    }

    [Fact]
    public void OperationalPlanAndAiSystems_ZeroHeapAllocations_InDailyAndMonthlyLoops()
    {
        using SimulationWorld world = new();

        for (ushort p = 0; p < 10; p++)
        {
            world.Provinces[p].ProvinceId = p;
            world.Provinces[p].OwnerTag = p < 5 ? CountryTag.ROK : CountryTag.PRK;
            world.Provinces[p].ControllerTag = world.Provinces[p].OwnerTag;
            world.Provinces[p].Terrain = TerrainType.Plains;
            world.Provinces[p].SupplyRatio = 1.0f;
        }

        world.DeclareWar(CountryTag.ROK, CountryTag.PRK);
        world.CountryCapitals[CountryTag.PRK] = 9;

        world.SpawnDivision(DivisionComponent.Create(101, CountryTag.ROK, startingProvinceId: 2, hardness: 0.40f, breakthrough: 35.0f));
        world.SpawnDivision(DivisionComponent.Create(102, CountryTag.ROK, startingProvinceId: 3, hardness: 0.10f, breakthrough: 20.0f));
        world.SpawnDivision(DivisionComponent.Create(201, CountryTag.PRK, startingProvinceId: 6, hardness: 0.10f, breakthrough: 20.0f));

        world.SpatialIndex.Rebuild(world);

        // Warm up JIT past TieredPGO
        for (int w = 0; w < 128; w++)
        {
            OperationalPlanSystem.EvaluateCountryOperationalPlan(world, CountryTag.ROK);
            TacticalFrontlineAISystem.EvaluateCountryFrontline(world, CountryTag.ROK);
            StrategicMinisterAISystem.EvaluateAndRecruitDivisions(world, CountryTag.ROK);
            StrategicMinisterAISystem.EvaluateAndExecuteWargoals(world, CountryTag.ROK);
            world.CommandQueue.Clear();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

        for (int iter = 0; iter < 1000; iter++)
        {
            OperationalPlanSystem.EvaluateCountryOperationalPlan(world, CountryTag.ROK);
            TacticalFrontlineAISystem.EvaluateCountryFrontline(world, CountryTag.ROK);
            StrategicMinisterAISystem.EvaluateAndRecruitDivisions(world, CountryTag.ROK);
            StrategicMinisterAISystem.EvaluateAndExecuteWargoals(world, CountryTag.ROK);
            world.CommandQueue.Clear();
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, afterAlloc - beforeAlloc);
    }
}
