using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;

namespace EastAsiaGSG.SimTests;

public sealed class CapitulationAndDiplomacyTests
{
    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();
    private static readonly QueryDescription ProductionLineQuery = new QueryDescription().WithAll<ProductionLineComponent>();
    private static readonly QueryDescription TaskForceQuery = new QueryDescription().WithAll<NavalTaskForceComponent>();

    [Fact]
    public void CapitulationSystem_TriggersSurrender_WhenVpRatioFallsBelowStabilityThreshold_AndCapitalLost()
    {
        using SimulationWorld world = new();

        world.DeclareWar(CountryTag.ROK, CountryTag.PRK);
        world.CountryCapitals[CountryTag.PRK] = 60; // Pyongyang

        // Setup PRK territory: 5 provinces, 100 total VPs
        // Prov 60 (Capital, 50 VP), Prov 61 (20 VP), Prov 62 (10 VP), Prov 63 (10 VP), Prov 64 (10 VP)
        world.Provinces[60].OwnerTag = CountryTag.PRK;
        world.Provinces[60].VictoryPoints = 50;
        world.Provinces[60].ControllerTag = CountryTag.ROK; // ROK captured capital!

        world.Provinces[61].OwnerTag = CountryTag.PRK;
        world.Provinces[61].VictoryPoints = 20;
        world.Provinces[61].ControllerTag = CountryTag.ROK; // ROK captured Prov 61!

        world.Provinces[62].OwnerTag = CountryTag.PRK;
        world.Provinces[62].VictoryPoints = 10;
        world.Provinces[62].ControllerTag = CountryTag.ROK; // ROK captured Prov 62!

        world.Provinces[63].OwnerTag = CountryTag.PRK;
        world.Provinces[63].VictoryPoints = 10;
        world.Provinces[63].ControllerTag = CountryTag.PRK; // PRK still holds Prov 63 (10 VP)

        world.Provinces[64].OwnerTag = CountryTag.PRK;
        world.Provinces[64].VictoryPoints = 10;
        world.Provinces[64].ControllerTag = CountryTag.PRK; // PRK still holds Prov 64 (10 VP)

        // PRK controlled VPs: 20 / 100 = 20%
        // Stability = 0.70 => Surrender threshold = 0.20 + (0.15 * 0.70) = 0.305 (30.5%)
        // 20% < 30.5% and capital lost => Surrender!

        // Seed PRK equipment stockpile
        world.Countries[CountryTag.PRK].SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline, 2000);
        world.Countries[CountryTag.PRK].SetStockpile(EquipmentType.MBT, TechGeneration.Gen1_1980Baseline, 500);

        // Seed field division for PRK
        world.SpawnDivision(DivisionComponent.Create(
            divisionId: 501,
            countryTag: CountryTag.PRK,
            startingProvinceId: 63));

        int baselineNewsCount = world.TriggeredNewsEventCount;

        // Execute CapitulationSystem
        CapitulationSystem.Execute(world);

        // 1. Capitulated status recorded
        Assert.True(world.HasCapitulated(CountryTag.PRK));

        // 2. Wars dissolved
        Assert.False(world.IsAtWarWith(CountryTag.ROK, CountryTag.PRK));

        // 3. Remaining provinces transferred to victor (ROK)
        Assert.Equal(CountryTag.ROK, world.Provinces[63].ControllerTag);
        Assert.Equal(CountryTag.ROK, world.Provinces[64].ControllerTag);

        // 4. 50% of PRK stockpile captured by ROK, PRK stockpile zeroed
        Assert.Equal(0, world.Countries[CountryTag.PRK].GetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline));
        Assert.Equal(1000, world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline));
        Assert.Equal(250, world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.MBT, TechGeneration.Gen1_1980Baseline));

        // 5. Remaining PRK field divisions disbanded
        int remainingDivs = 0;
        Query query = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                if (divs[i].CountryTag == CountryTag.PRK)
                {
                    remainingDivs++;
                }
            }
        }
        Assert.Equal(0, remainingDivs);

        // 6. News event triggered
        Assert.True(world.TriggeredNewsEventCount > baselineNewsCount);
        int capitulationHash = CapitulationSystem.GetCapitulationNewsHash(CountryTag.PRK);
        Assert.True(world.HasTriggeredNewsEvent(capitulationHash));
    }

    [Fact]
    public void CapitulationSystem_DoesNotTrigger_WhenCapitalIsStillHeld()
    {
        using SimulationWorld world = new();

        world.DeclareWar(CountryTag.ROK, CountryTag.PRK);
        world.CountryCapitals[CountryTag.PRK] = 60; // Pyongyang

        world.Provinces[60].OwnerTag = CountryTag.PRK;
        world.Provinces[60].VictoryPoints = 50;
        world.Provinces[60].ControllerTag = CountryTag.PRK; // Capital still held!

        world.Provinces[61].OwnerTag = CountryTag.PRK;
        world.Provinces[61].VictoryPoints = 50;
        world.Provinces[61].ControllerTag = CountryTag.ROK; // Lost 50%

        CapitulationSystem.Execute(world);

        // Should NOT capitulate while capital is intact and VPs are above threshold
        Assert.False(world.HasCapitulated(CountryTag.PRK));
        Assert.True(world.IsAtWarWith(CountryTag.ROK, CountryTag.PRK));
    }

    [Fact]
    public void SimulationCommand_SwitchProductionEquipment_UpgradesGenerationAndRetainsEfficiency()
    {
        using SimulationWorld world = new();

        // Spawn a Gen 1 MBT production line with 60% efficiency
        Entity lineEntity = world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 101,
            countryTag: CountryTag.USA,
            archetype: EquipmentType.MBT,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 10,
            initialEfficiency: 0.60f));

        // Enqueue command to upgrade to Gen 2 MBT
        world.EnqueueCommand(SimulationCommand.CreateSwitchProductionEquipment(
            CountryTag.USA,
            lineId: 101,
            EquipmentType.MBT,
            TechGeneration.Gen2_1988LateColdWar));

        // Execute command via StepSixHours
        world.StepSixHours();

        ref readonly ProductionLineComponent line = ref world.EcsWorld.Get<ProductionLineComponent>(lineEntity);
        Assert.Equal(EquipmentType.MBT, line.Archetype);
        Assert.Equal(TechGeneration.Gen2_1988LateColdWar, line.Generation);

        // Retains 70% of 0.60 = 0.42
        Assert.Equal(0.42f, line.Efficiency, 0.01f);
    }

    [Fact]
    public void SimulationCommand_CreateProductionLine_SpawnsNewEcsProductionLine()
    {
        using SimulationWorld world = new();

        world.EnqueueCommand(SimulationCommand.CreateCreateProductionLine(
            CountryTag.TWN,
            EquipmentType.PGM,
            TechGeneration.Gen2_1988LateColdWar,
            assignedFactories: 5));

        world.StepSixHours();

        bool found = false;
        Query query = world.EcsWorld.Query(in ProductionLineQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<ProductionLineComponent> lines = chunk.GetSpan<ProductionLineComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                if (lines[i].CountryTag == CountryTag.TWN && lines[i].Archetype == EquipmentType.PGM)
                {
                    Assert.Equal(5, lines[i].AssignedMilitaryFactories);
                    Assert.Equal(TechGeneration.Gen2_1988LateColdWar, lines[i].Generation);
                    found = true;
                    break;
                }
            }
        }
        Assert.True(found);
    }

    [Fact]
    public void SimulationCommand_AssignNavalMission_UpdatesTaskForceRegionAndMission()
    {
        using SimulationWorld world = new();

        Entity tfEntity = world.SpawnNavalTaskForce(NavalTaskForceComponent.CreateCarrierStrikeGroup(
            taskForceId: 777,
            countryTag: CountryTag.USA,
            assignedRegionId: 8,
            homeBaseProvinceId: 3));

        world.EnqueueCommand(SimulationCommand.CreateAssignNavalMission(
            CountryTag.USA,
            taskForceId: 777,
            targetRegionId: 7, // Reassign to East China Sea
            NavalMission.StrikeForce));

        world.StepSixHours();

        ref readonly NavalTaskForceComponent tf = ref world.EcsWorld.Get<NavalTaskForceComponent>(tfEntity);
        Assert.Equal(7, tf.AssignedRegionId);
        Assert.Equal(NavalMission.StrikeForce, tf.Mission);
    }

    [Fact]
    public void CapitulationSystem_ZeroHeapAllocations_InDailyTick()
    {
        using SimulationWorld world = new();

        world.DeclareWar(CountryTag.ROK, CountryTag.PRK);

        // Warm up JIT
        for (int i = 0; i < 16; i++)
        {
            CapitulationSystem.Execute(world);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            CapitulationSystem.Execute(world);
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, afterAlloc - beforeAlloc);
    }
}
