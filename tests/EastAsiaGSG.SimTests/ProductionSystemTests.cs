using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;
using EastAsiaGSG.Data.Loaders;

namespace EastAsiaGSG.SimTests;

/// <summary>
/// Headless xUnit test suite validating Phase 3, Task 3.1:
/// ProductionLineComponent layout, C_free &amp; Consumer Goods math, State construction queues,
/// hyperbolic production efficiency growth, daily equipment stockpile output, and zero-allocation invariants.
/// </summary>
public sealed class ProductionSystemTests
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

    [Fact]
    public unsafe void ProductionLineComponent_And_StateData_MaintainExact32ByteBlittableLayout()
    {
        Assert.Equal(32, sizeof(ProductionLineComponent));
        Assert.Equal(32, sizeof(StateData));
        Assert.Equal(8, sizeof(EquipmentResourceCost));

        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ProductionLineComponent>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<StateData>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<EquipmentResourceCost>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<CountryFactorySummary>());

        ProductionLineComponent line = ProductionLineComponent.Create(
            lineId: 10,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.MBT,
            generation: TechGeneration.Gen2_1988LateColdWar,
            assignedMilitaryFactories: 12,
            priority: 150,
            initialEfficiency: 0.25f,
            efficiencyCap: 0.85f);

        Assert.Equal(10, line.LineId);
        Assert.Equal(CountryTag.ROK, line.CountryTag);
        Assert.Equal(EquipmentType.MBT, line.Archetype);
        Assert.Equal(TechGeneration.Gen2_1988LateColdWar, line.Generation);
        Assert.Equal(150, line.Priority);
        Assert.Equal((ushort)12, line.AssignedMilitaryFactories);
        Assert.Equal((ushort)12, line.ActiveMilitaryFactories);
        Assert.Equal(0.25f, line.Efficiency, precision: 4);
        Assert.Equal(0.85f, line.EfficiencyCap, precision: 4);
        // MBT base (14.0) * Gen2 multiplier (1.25) = 17.50 IC
        Assert.Equal(17.50f, line.EquipmentCostIc, precision: 4);
    }

    [Fact]
    public void ConstructionSystem_CalculateFactorySummary_MatchesGameDesignSpecFormulas()
    {
        string root = FindRepositoryRoot();
        string statesPath = Path.Combine(root, "data", "map", "states.json");
        string provincesPath = Path.Combine(root, "data", "map", "provinces.json");

        using SimulationWorld world = new();
        JsonMapLoader.LoadMap(world, statesPath, provincesPath);

        // Verify ROK and PRK industrial totals loaded from data/map/states.json
        CountryFactorySummary rokSummary = ConstructionSystem.CalculateFactorySummary(world, CountryTag.ROK);
        CountryFactorySummary prkSummary = ConstructionSystem.CalculateFactorySummary(world, CountryTag.PRK);

        Assert.True(rokSummary.TotalCivilianFactories > 0);
        Assert.True(rokSummary.TotalMilitaryFactories > 0);
        Assert.Equal(
            rokSummary.TotalCivilianFactories + rokSummary.TotalMilitaryFactories + rokSummary.TotalNavalDockyards,
            rokSummary.TotalFactories);

        int expectedRokConsumer = (int)MathF.Ceiling(rokSummary.TotalFactories * world.Countries[CountryTag.ROK].ConsumerGoodsRatio);
        Assert.Equal(expectedRokConsumer, rokSummary.ConsumerGoodsFactories);
        Assert.Equal(
            Math.Max(0, rokSummary.TotalCivilianFactories - expectedRokConsumer),
            rokSummary.FreeCivilianFactories);

        Assert.True(prkSummary.TotalMilitaryFactories > 0);

        // Verify exact ceiling and trade balance formula on a synthetic nation state:
        // C_total = 15, M_total = 8 + 2 (off-map) = 10, N_dockyards = 4 -> TotalFactories = 29
        // ConsumerGoodsRatio = 0.25 -> C_consumer = ceil(29 * 0.25) = ceil(7.25) = 8
        // TradeBalanceFactories = +3 -> C_free = max(0, 15 - 8 + 3) = 10
        world.States[100].ControllerTag = CountryTag.JPN;
        world.States[100].CivilianFactories = 15;
        world.States[100].MilitaryFactories = 8;
        world.States[100].NavalDockyards = 4;

        world.Countries[CountryTag.JPN].ConsumerGoodsRatio = 0.25f;
        world.Countries[CountryTag.JPN].OffMapMilitaryFactories = 2;
        world.Countries[CountryTag.JPN].TradeBalanceFactories = 3;

        CountryFactorySummary jpnSummary = ConstructionSystem.CalculateFactorySummary(world, CountryTag.JPN);
        Assert.Equal(15, jpnSummary.TotalCivilianFactories);
        Assert.Equal(10, jpnSummary.TotalMilitaryFactories);
        Assert.Equal(4, jpnSummary.TotalNavalDockyards);
        Assert.Equal(29, jpnSummary.TotalFactories);
        Assert.Equal(8, jpnSummary.ConsumerGoodsFactories);
        Assert.Equal(3, jpnSummary.TradeBalanceFactories);
        Assert.Equal(10, jpnSummary.FreeCivilianFactories);
    }

    [Fact]
    public void ConstructionSystem_AdvancesStateBuildingQueue_Enforces15FactoryCap_AndIncrementsBuildingLevels()
    {
        using SimulationWorld world = new();

        // Give ROK 24 Civilian Factories across States 1 and 2, with ConsumerGoodsRatio = 0.0 -> C_free = 24
        world.Countries[CountryTag.ROK].ConsumerGoodsRatio = 0.0f;
        world.Countries[CountryTag.ROK].ConstructionSpeedModifier = 0.20f; // +20% construction speed

        world.States[1].ControllerTag = CountryTag.ROK;
        world.States[1].CivilianFactories = 14;
        world.States[1].MilitaryFactories = 2;

        world.States[2].ControllerTag = CountryTag.ROK;
        world.States[2].CivilianFactories = 10;
        world.States[2].AirbaseLevel = 3;

        // Queue 1 Military Factory (7,200 IC) in State 1 requesting 20 factories (must clamp to 15)
        // Queue 1 Airbase (1,250 IC) in State 2 requesting 15 factories (will receive remaining 24 - 15 = 9 C_free)
        Assert.True(ConstructionSystem.QueueConstruction(ref world.States[1], BuildingType.MilitaryFactory, levels: 1, requestedCivFactories: 20));
        Assert.True(ConstructionSystem.QueueConstruction(ref world.States[2], BuildingType.Airbase, levels: 1, requestedCivFactories: 15));

        Assert.Equal((ushort)15, world.States[1].AssignedCivFactories);
        Assert.Equal((ushort)15, world.States[2].AssignedCivFactories);

        // Execute 1 daily construction tick:
        // State 1 gets 15 C_free -> 15 * 5.0 * 1.20 = 90.0 IC/day
        // State 2 gets remaining 9 C_free -> 9 * 5.0 * 1.20 = 54.0 IC/day
        ConstructionSystem.Execute(world);

        Assert.Equal(90.0f, world.States[1].ConstructionProgress, precision: 3);
        Assert.Equal(54.0f, world.States[2].ConstructionProgress, precision: 3);

        // At 90.0 IC/day, State 1 completes its Military Factory (7,200 IC) on day 80 (80 * 90 = 7,200 IC)
        // State 2 completes its Airbase (1,250 IC) earlier, after ceil(1250 / 54) = 24 days
        for (int day = 2; day <= 80; day++)
        {
            ConstructionSystem.Execute(world);
        }

        // Verify both construction projects finished and incremented their respective state building levels
        Assert.Equal(3, world.States[1].MilitaryFactories);
        Assert.Equal(BuildingType.None, world.States[1].ActiveBuildingType);
        Assert.Equal(0, world.States[1].QueuedBuildingLevels);
        Assert.Equal(0.0f, world.States[1].ConstructionProgress, precision: 3);

        Assert.Equal(4, world.States[2].AirbaseLevel);
        Assert.Equal(BuildingType.None, world.States[2].ActiveBuildingType);
        Assert.Equal(0, world.States[2].QueuedBuildingLevels);
    }

    [Fact]
    public void ProductionSystem_HyperbolicEfficiencyGrowth_DailyOutput_AndStockpileAccumulation()
    {
        using SimulationWorld world = new();

        // Setup ROK with 10 Military Factories in State 1 and +10% FactoryOutputModifier
        world.States[1].ControllerTag = CountryTag.ROK;
        world.States[1].MilitaryFactories = 10;
        world.Countries[CountryTag.ROK].FactoryOutputModifier = 0.10f;

        // Create an InfantryKit (Gen 1, cost = 0.50 IC) line with M_i = 10, E_0 = 0.25, E_cap = 0.80, R_pen = 0.20
        Entity lineEntity = world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 1,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.InfantryKit,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 10,
            initialEfficiency: 0.25f,
            efficiencyCap: 0.80f,
            resourcePenalty: 0.20f));

        // Verify Day 1 mathematical formula directly:
        // DailyIC = 10 * 4.5 * (1 + 0.10) * 0.25 * (1 - 0.20) = 45.0 * 1.10 * 0.25 * 0.80 = 9.90 IC
        // UnitsProduced = 9.90 / 0.50 = 19.80 units -> 19 whole units added to stockpile, 0.80 fractional carried over
        // Delta E = 0.001 * (0.80^2 / 0.25) = 0.001 * (0.64 / 0.25) = 0.00256 -> E_1 = 0.25256
        ProductionSystem.Execute(world);

        ref ProductionLineComponent lineAfterDay1 = ref world.EcsWorld.Get<ProductionLineComponent>(lineEntity);
        Assert.Equal(19, world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline));
        Assert.Equal(0.80f, lineAfterDay1.AccumulatedFractionalUnits, precision: 3);
        Assert.Equal(0.25256f, lineAfterDay1.Efficiency, precision: 5);

        // Simulate 180 full days via StepSixHours() (720 six-hour ticks) and verify hyperbolic efficiency approaches and clamps to E_cap
        for (int tick = 0; tick < 180 * 4; tick++)
        {
            world.StepSixHours();
        }

        ref ProductionLineComponent lineAfter181Days = ref world.EcsWorld.Get<ProductionLineComponent>(lineEntity);
        Assert.True(lineAfter181Days.Efficiency > 0.50f);
        Assert.True(lineAfter181Days.Efficiency <= 0.80f);
        Assert.True(world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline) > 5500);

        // Verify efficiency clamping at E_cap
        float clampedEff = ProductionSystem.CalculateNextEfficiency(0.7995f, 0.80f);
        Assert.Equal(0.80f, clampedEff, precision: 5);
    }

    [Fact]
    public void ProductionSystem_PriorityFactoryAllocation_ResourceShortagePenalties_AndEquipmentSwitchRetention()
    {
        using SimulationWorld world = new();

        // PRK controls State 5 with 12 Military Factories and 18 Steel, 4 Oil (insufficient for full demand)
        world.States[5].ControllerTag = CountryTag.PRK;
        world.States[5].MilitaryFactories = 12;
        world.States[5].SetResource(ResourceType.Oil, 4);
        world.States[5].SetResource(ResourceType.Steel, 18);

        // Spawn lower-priority Artillery line (Priority = 50, Assigned = 8) and higher-priority MBT line (Priority = 200, Assigned = 8)
        Entity lowPrioEntity = world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 10,
            countryTag: CountryTag.PRK,
            archetype: EquipmentType.Artillery,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 8,
            priority: 50,
            initialEfficiency: 0.50f,
            efficiencyCap: 0.80f));

        Entity highPrioEntity = world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 20,
            countryTag: CountryTag.PRK,
            archetype: EquipmentType.MBT,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 8,
            priority: 200,
            initialEfficiency: 0.60f,
            efficiencyCap: 0.80f));

        // Execute daily production so ActiveMilitaryFactories are allocated in priority order:
        // High-priority MBT line gets all 8 requested factories; low-priority Artillery line gets remaining 4 (12 - 8 = 4)
        ProductionSystem.Execute(world);

        ref ProductionLineComponent highLine = ref world.EcsWorld.Get<ProductionLineComponent>(highPrioEntity);
        ref ProductionLineComponent lowLine = ref world.EcsWorld.Get<ProductionLineComponent>(lowPrioEntity);
        Assert.Equal((ushort)8, highLine.ActiveMilitaryFactories);
        Assert.Equal((ushort)4, lowLine.ActiveMilitaryFactories);

        // Evaluate strategic resource penalties:
        // High-priority MBT (8 active factories * [1 Oil, 3 Steel] = 8 Oil, 24 Steel; total 32 required)
        // State 5 provides 4 Oil + 18 Steel = 22 fulfilled -> deficit = (32 - 22) / 32 = 10 / 32 = 0.3125
        // Low-priority Artillery (4 active factories * [2 Steel] = 8 Steel required; 0 remaining) -> deficit = 1.0
        ProductionSystem.EvaluateCountryResourcePenalties(world, CountryTag.PRK);
        Assert.Equal(0.3125f, highLine.ResourcePenalty, precision: 4);
        Assert.Equal(1.0f, lowLine.ResourcePenalty, precision: 4);

        // Verify equipment switching efficiency rules:
        // 1. Same archetype upgrade (Gen 1 -> Gen 2 MBT) retains 70% of current efficiency
        float effBeforeUpgrade = highLine.Efficiency;
        ProductionSystem.SwitchLineEquipment(ref highLine, EquipmentType.MBT, TechGeneration.Gen2_1988LateColdWar);
        Assert.Equal(TechGeneration.Gen2_1988LateColdWar, highLine.Generation);
        Assert.Equal(effBeforeUpgrade * ProductionSystem.SameArchetypeRetentionFactor, highLine.Efficiency, precision: 4);

        // 2. Different archetype switch (MBT -> TacticalAir) resets efficiency to 0.10
        ProductionSystem.SwitchLineEquipment(ref highLine, EquipmentType.TacticalAir, TechGeneration.Gen2_1988LateColdWar);
        Assert.Equal(EquipmentType.TacticalAir, highLine.Archetype);
        Assert.Equal(ProductionLineComponent.MinEfficiency, highLine.Efficiency, precision: 4);
    }

    [Fact]
    public void CommandQueue_ProcessesQueueConstruction_AssignProductionFactories_AndTwoPassLineLookup()
    {
        using SimulationWorld world = new();
        world.States[3].ControllerTag = CountryTag.ROK;
        world.States[3].CivilianFactories = 10;
        world.States[3].MilitaryFactories = 10;

        Entity lineA = world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 500,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.IFV,
            assignedMilitaryFactories: 3));

        // Spawn a second production line whose LineId collides with lineA.Id to verify two-pass priority
        Entity lineB = world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: lineA.Id,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.PGM,
            assignedMilitaryFactories: 2));

        Assert.True(world.TryFindProductionLineById(lineA.Id, out Entity resolved));
        Assert.Equal(lineB, resolved);

        // Enqueue deterministic construction and factory assignment commands
        Assert.True(world.EnqueueCommand(SimulationCommand.CreateQueueConstruction(
            CountryTag.ROK,
            stateId: 3,
            buildingType: BuildingType.RadarSam,
            levels: 2,
            assignedCivFactories: 12)));

        Assert.True(world.EnqueueCommand(SimulationCommand.CreateAssignProductionFactories(
            CountryTag.ROK,
            lineId: 500,
            militaryFactories: 9)));

        world.StepSixHours();

        Assert.Equal(BuildingType.RadarSam, world.States[3].ActiveBuildingType);
        Assert.Equal(2, world.States[3].QueuedBuildingLevels);
        Assert.Equal((ushort)12, world.States[3].AssignedCivFactories);

        ref ProductionLineComponent updatedLineA = ref world.EcsWorld.Get<ProductionLineComponent>(lineA);
        Assert.Equal((ushort)9, updatedLineA.AssignedMilitaryFactories);
    }

    [Fact]
    public void ProductionAndConstructionSystems_ZeroHeapAllocations_InHotDailyLoop()
    {
        using SimulationWorld world = new();

        world.States[1].ControllerTag = CountryTag.ROK;
        world.States[1].CivilianFactories = 15;
        world.States[1].MilitaryFactories = 12;
        ConstructionSystem.QueueConstruction(ref world.States[1], BuildingType.CivilianFactory, levels: 5, requestedCivFactories: 15);

        world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 1,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.InfantryKit,
            assignedMilitaryFactories: 6));
        world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 2,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.MBT,
            assignedMilitaryFactories: 6));

        // Warm up JIT
        ConstructionSystem.Execute(world);
        ProductionSystem.Execute(world);

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            ConstructionSystem.Execute(world);
            ProductionSystem.Execute(world);
        }
        long afterBytes = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0L, afterBytes - beforeBytes);
    }
}
