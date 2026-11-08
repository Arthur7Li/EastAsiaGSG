using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Core.Systems;

namespace EastAsiaGSG.SimTests;

public sealed class CombatDoctrineAndCombinedArmsTests
{
    [Fact]
    public void CombatResolutionSystem_CombinedArmsSynergy_GrantsFifteenPercentAttackAndBreakthrough()
    {
        // 1. Pure infantry (Hardness 0.05, no combined arms)
        DivisionComponent pureInf = DivisionComponent.Create(
            divisionId: 1,
            countryTag: CountryTag.PRK,
            startingProvinceId: 0,
            softAttack: 40.0f,
            hardAttack: 10.0f,
            hardness: 0.05f);

        Assert.False(CombatResolutionSystem.HasCombinedArms(in pureInf));

        CombatExchangeResult baseExchange = CombatResolutionSystem.CalculateExchange(
            softAttack: 40.0f,
            hardAttack: 10.0f,
            attackerPiercing: 20.0f,
            attackerArmor: 10.0f,
            targetHardness: 0.10f,
            targetArmor: 10.0f,
            targetPiercing: 20.0f,
            targetDefenseOrBreakthrough: 50.0f,
            attackerCombinedArms: false);

        // 2. Mechanized division (Hardness 0.35, combined arms synergy)
        DivisionComponent mechDiv = DivisionComponent.Create(
            divisionId: 2,
            countryTag: CountryTag.ROK,
            startingProvinceId: 1,
            softAttack: 40.0f,
            hardAttack: 10.0f,
            hardness: 0.35f);

        Assert.True(CombatResolutionSystem.HasCombinedArms(in mechDiv));

        CombatExchangeResult caExchange = CombatResolutionSystem.CalculateExchange(
            softAttack: 40.0f,
            hardAttack: 10.0f,
            attackerPiercing: 20.0f,
            attackerArmor: 10.0f,
            targetHardness: 0.10f,
            targetArmor: 10.0f,
            targetPiercing: 20.0f,
            targetDefenseOrBreakthrough: 50.0f,
            attackerCombinedArms: true);

        // Combined arms effective attack should be exactly 15% higher
        Assert.Equal(baseExchange.EffectiveAttack * 1.15f, caExchange.EffectiveAttack, 0.05f);
        Assert.True(caExchange.OrgDamage > baseExchange.OrgDamage);
    }

    [Fact]
    public void CombatResolutionSystem_TacticalDoctrine_RockPaperScissorsCounterPlay()
    {
        // AirLandBattle counters MassAssault (Attacker advantage)
        Assert.Equal(1, CombatResolutionSystem.ResolveTacticalDoctrineAdvantage(
            TacticalDoctrine.AirLandBattle, TacticalDoctrine.MassAssault));
        Assert.Equal(2, CombatResolutionSystem.ResolveTacticalDoctrineAdvantage(
            TacticalDoctrine.MassAssault, TacticalDoctrine.AirLandBattle));

        // DeepOperations counters ElasticDefense (Attacker advantage)
        Assert.Equal(1, CombatResolutionSystem.ResolveTacticalDoctrineAdvantage(
            TacticalDoctrine.DeepOperations, TacticalDoctrine.ElasticDefense));
        Assert.Equal(2, CombatResolutionSystem.ResolveTacticalDoctrineAdvantage(
            TacticalDoctrine.ElasticDefense, TacticalDoctrine.DeepOperations));

        // ElasticDefense counters AirLandBattle (Defender advantage)
        Assert.Equal(2, CombatResolutionSystem.ResolveTacticalDoctrineAdvantage(
            TacticalDoctrine.AirLandBattle, TacticalDoctrine.ElasticDefense));
        Assert.Equal(1, CombatResolutionSystem.ResolveTacticalDoctrineAdvantage(
            TacticalDoctrine.ElasticDefense, TacticalDoctrine.AirLandBattle));

        // MassAssault counters DeepOperations (Defender advantage)
        Assert.Equal(2, CombatResolutionSystem.ResolveTacticalDoctrineAdvantage(
            TacticalDoctrine.DeepOperations, TacticalDoctrine.MassAssault));
        Assert.Equal(1, CombatResolutionSystem.ResolveTacticalDoctrineAdvantage(
            TacticalDoctrine.MassAssault, TacticalDoctrine.DeepOperations));

        // Symmetric doctrines yield neutral 0
        Assert.Equal(0, CombatResolutionSystem.ResolveTacticalDoctrineAdvantage(
            TacticalDoctrine.AirLandBattle, TacticalDoctrine.AirLandBattle));
        Assert.Equal(0, CombatResolutionSystem.ResolveTacticalDoctrineAdvantage(
            TacticalDoctrine.Standard, TacticalDoctrine.MassAssault));
    }

    [Fact]
    public void CombatResolutionSystem_24HourDoctrineRoll_UpdatesActiveCombatAdvantage()
    {
        using SimulationWorld world = new();

        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].OwnerTag = CountryTag.USA;
        world.Provinces[0].ControllerTag = CountryTag.USA;
        world.Provinces[0].Terrain = TerrainType.Plains;
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
        world.DeclareWar(CountryTag.USA, CountryTag.PRK);

        // USA has AirLandBattle; PRK has MassAssault
        Assert.Equal(TacticalDoctrine.AirLandBattle, world.CountryDoctrines[CountryTag.USA]);
        Assert.Equal(TacticalDoctrine.MassAssault, world.CountryDoctrines[CountryTag.PRK]);

        Entity usaDiv = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 101,
            countryTag: CountryTag.USA,
            startingProvinceId: 0,
            softAttack: 50.0f,
            defense: 50.0f));

        Entity prkDiv = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 201,
            countryTag: CountryTag.PRK,
            startingProvinceId: 1,
            softAttack: 30.0f,
            defense: 50.0f));

        // USA attacks PRK in Province 1
        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.USA, 101, 1));

        // Step 1 tick (ElapsedSixHourTicks 0 -> evaluates doctrine roll)
        world.StepSixHours();

        Assert.True(world.TryFindActiveCombatAtProvince(1, out Entity combatEntity));
        ref readonly ActiveCombatComponent combat = ref world.EcsWorld.Get<ActiveCombatComponent>(combatEntity);

        Assert.Equal(TacticalDoctrine.AirLandBattle, combat.AttackerDoctrine);
        Assert.Equal(TacticalDoctrine.MassAssault, combat.DefenderDoctrine);
        Assert.Equal(1, combat.TacticalAdvantageSide); // Attacker holds advantage
    }

    [Fact]
    public void CombatResolutionSystem_ZeroHeapAllocations_WithDoctrinesAndCombinedArms()
    {
        using SimulationWorld world = new();

        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].OwnerTag = CountryTag.USA;
        world.Provinces[0].ControllerTag = CountryTag.USA;
        world.Provinces[0].SupplyRatio = 1.0f;

        world.Provinces[1].ProvinceId = 1;
        world.Provinces[1].OwnerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;
        world.Provinces[1].SupplyRatio = 1.0f;

        DirectedEdge[] edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);
        world.DeclareWar(CountryTag.USA, CountryTag.PRK);

        Entity usa = world.SpawnDivision(DivisionComponent.Create(101, CountryTag.USA, 0, hardness: 0.35f));
        Entity prk = world.SpawnDivision(DivisionComponent.Create(201, CountryTag.PRK, 1, hardness: 0.10f));

        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.USA, 101, 1));

        // Step 1 tick to spawn ActiveCombatComponent
        world.StepSixHours();

        for (int w = 0; w < 128; w++)
        {
            CombatResolutionSystem.Execute(world);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

        for (int iter = 0; iter < 1000; iter++)
        {
            CombatResolutionSystem.Execute(world);
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, afterAlloc - beforeAlloc);
    }
}
