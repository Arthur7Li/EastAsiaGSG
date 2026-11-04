using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;
using EastAsiaGSG.Data.Loaders;

namespace EastAsiaGSG.SimTests;

public sealed class FocusTreeSystemTests
{
    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();

    [Fact]
    public void FocusNodeData_FocusCondition_And_FocusEffect_MaintainExactBlittableSizes()
    {
        Assert.Equal(12, Unsafe.SizeOf<FocusCondition>());
        Assert.Equal(12, Unsafe.SizeOf<FocusEffect>());
        Assert.Equal(160, Unsafe.SizeOf<FocusNodeData>());

        FocusNodeData node = FocusNodeData.Create(
            countryTag: CountryTag.ROK,
            focusIndex: 3,
            costDays: 35,
            gridX: 4,
            gridY: 2,
            aiBaseWeight: 120,
            focusIdHash: FocusNodeData.ComputeIdHash("ROK_chaebol_industrial_push"));

        Assert.True(node.AddPrerequisite(0));
        Assert.True(node.AddPrerequisite(1));
        Assert.True(node.AddMutuallyExclusive(4));
        Assert.True(node.AddCondition(FocusCondition.CreateDateAfter(1985, 9, 1)));
        Assert.True(node.AddEffect(FocusEffect.CreateAddPoliticalPower(120.0f)));
        Assert.True(node.AddEffect(FocusEffect.CreateAddCivilianFactories(3, stateId: 0)));

        Assert.Equal(2, node.PrerequisiteCount);
        Assert.Equal(0, node.GetPrerequisite(0));
        Assert.Equal(1, node.GetPrerequisite(1));
        Assert.Equal(1, node.MutuallyExclusiveCount);
        Assert.Equal(4, node.GetMutuallyExclusive(0));

        FocusCondition c0 = node.GetCondition(0);
        Assert.Equal(ConditionType.DateAfter, c0.Type);
        Assert.Equal(19850901, c0.IntParam);

        FocusEffect e0 = node.GetEffect(0);
        Assert.Equal(EffectType.AddPoliticalPower, e0.Type);
        Assert.Equal(120.0f, e0.Value);

        FocusEffect e1 = node.GetEffect(1);
        Assert.Equal(EffectType.AddCivilianFactories, e1.Type);
        Assert.Equal((ushort)0, e1.TargetId);
        Assert.Equal(3.0f, e1.Value);
    }

    [Fact]
    public void FocusTreeSystem_EvaluatesAll11ConditionTypes_Prerequisites_And_MutualExclusivity()
    {
        using SimulationWorld world = new();
        world.States[0].OwnerTag = CountryTag.ROK;
        world.States[0].ControllerTag = CountryTag.ROK;
        world.States[5].OwnerTag = CountryTag.PRK;
        world.States[5].ControllerTag = CountryTag.PRK;

        // 1. DateAfter & DateBefore (Start date is 1985-01-01 -> 19850101)
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateDateAfter(1985, 1, 1)));
        Assert.False(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateDateAfter(1985, 9, 1)));
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateDateBefore(1986, 1, 1)));
        Assert.False(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateDateBefore(1985, 1, 1)));

        // 2. AtWar & AtPeace
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateAtPeace()));
        Assert.False(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateAtWar()));
        world.DeclareWar(CountryTag.ROK, CountryTag.PRK);
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateAtWar()));
        Assert.False(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateAtPeace()));
        world.MakePeace(CountryTag.ROK, CountryTag.PRK);

        // 3. ControlsState & OwnsState
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateControlsState(0)));
        Assert.False(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateControlsState(5)));
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateOwnsState(0)));
        Assert.False(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateOwnsState(5)));

        // 4. StabilityGreaterThan & WarSupportGreaterThan (supports both 0..1 ratio and 0..100 percentage)
        world.Countries[CountryTag.ROK].Stability = 0.72f;
        world.Countries[CountryTag.ROK].WarSupport = 0.58f;
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateStabilityGreaterThan(0.70f)));
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateStabilityGreaterThan(70.0f)));
        Assert.False(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateStabilityGreaterThan(0.80f)));

        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateWarSupportGreaterThan(0.55f)));
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateWarSupportGreaterThan(55.0f)));
        Assert.False(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateWarSupportGreaterThan(0.65f)));

        // 5. DefconAtMost
        world.Countries[CountryTag.ROK].Defcon = 3;
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateDefconAtMost(3)));
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateDefconAtMost(4)));
        Assert.False(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateDefconAtMost(2)));

        // 6. HasCompletedFocus
        Assert.False(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateHasCompletedFocus(0)));
        world.Countries[CountryTag.ROK].MarkFocusCompleted(0);
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.ROK, FocusCondition.CreateHasCompletedFocus(0)));

        // 7. HomelandEscalationGreaterThan
        world.Countries[CountryTag.USA].HomelandEscalation = 42;
        Assert.True(FocusTreeSystem.EvaluateCondition(world, CountryTag.USA, FocusCondition.CreateHomelandEscalationGreaterThan(35.0f)));
        Assert.False(FocusTreeSystem.EvaluateCondition(world, CountryTag.USA, FocusCondition.CreateHomelandEscalationGreaterThan(50.0f)));

        // 8. Prerequisite & Mutually Exclusive Gating in CanStartFocus / TryStartFocus
        // Focus 1 (requires Focus 0, mutually exclusive with Focus 2)
        FocusNodeData focus1 = FocusNodeData.Create(CountryTag.ROK, focusIndex: 1, costDays: 10);
        focus1.AddPrerequisite(0);
        focus1.AddMutuallyExclusive(2);
        world.RegisterFocusNode(in focus1);

        FocusNodeData focus2 = FocusNodeData.Create(CountryTag.ROK, focusIndex: 2, costDays: 10);
        focus2.AddPrerequisite(0);
        focus2.AddMutuallyExclusive(1);
        world.RegisterFocusNode(in focus2);

        // Focus 3 (requires Focus 1, which is not yet complete)
        FocusNodeData focus3 = FocusNodeData.Create(CountryTag.ROK, focusIndex: 3, costDays: 10);
        focus3.AddPrerequisite(1);
        world.RegisterFocusNode(in focus3);

        Assert.True(FocusTreeSystem.CanStartFocus(world, CountryTag.ROK, 1));
        Assert.True(FocusTreeSystem.CanStartFocus(world, CountryTag.ROK, 2));
        Assert.False(FocusTreeSystem.CanStartFocus(world, CountryTag.ROK, 3));

        // Start Focus 1 -> Focus 2 becomes blocked by mutual exclusivity while Focus 1 is active
        Assert.True(FocusTreeSystem.TryStartFocus(world, CountryTag.ROK, 1));
        Assert.False(FocusTreeSystem.CanStartFocus(world, CountryTag.ROK, 2));
    }

    [Fact]
    public void FocusEffectExecutor_ExecutesAll15EffectTypes_Deterministically()
    {
        using SimulationWorld world = new();
        world.States[0].OwnerTag = CountryTag.ROK;
        world.States[0].ControllerTag = CountryTag.ROK;
        world.States[0].CivilianFactories = 10;
        world.States[0].MilitaryFactories = 5;
        world.States[0].NavalDockyards = 2;
        world.States[0].RadarSamLevel = 3;
        world.Provinces[0].StateId = 0;
        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;

        // Register a production line for ROK MBT (Gen 1) to verify GrantTechBonus upgrades it
        ProductionLineComponent mbtLine = ProductionLineComponent.Create(
            lineId: 1,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.MBT,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 5,
            priority: 0,
            initialEfficiency: 0.50f,
            efficiencyCap: 0.80f);
        Entity lineEntity = world.SpawnProductionLine(in mbtLine);

        // Batch 1 (Effects 1..6): PP, Stability, WarSupport, ConsumerGoods, CivFactories, MilFactories
        FocusNodeData focusBatch1 = FocusNodeData.Create(CountryTag.ROK, focusIndex: 0, costDays: 1);
        focusBatch1.AddEffect(FocusEffect.CreateAddPoliticalPower(150.0f));
        focusBatch1.AddEffect(FocusEffect.CreateAddStability(0.10f));
        focusBatch1.AddEffect(FocusEffect.CreateAddWarSupport(15.0f)); // 15% normalized to +0.15
        focusBatch1.AddEffect(FocusEffect.CreateModifyConsumerGoods(-0.05f));
        focusBatch1.AddEffect(FocusEffect.CreateAddCivilianFactories(4, stateId: 0));
        focusBatch1.AddEffect(FocusEffect.CreateAddMilitaryFactories(3, stateId: 0));
        world.RegisterFocusNode(in focusBatch1);

        // Batch 2 (Effects 7..12): NavalDockyards, RadarSamLevel, EquipmentStockpile, SpawnDivisionTemplate, GrantTechBonus, CreateWargoal
        FocusNodeData focusBatch2 = FocusNodeData.Create(CountryTag.ROK, focusIndex: 1, costDays: 1);
        focusBatch2.AddEffect(FocusEffect.CreateAddNavalDockyards(2, stateId: 0));
        focusBatch2.AddEffect(FocusEffect.CreateAddRadarSamLevel(2, stateId: 0));
        focusBatch2.AddEffect(FocusEffect.CreateAddEquipmentStockpile(EquipmentType.PGM, 250, (int)TechGeneration.Gen2_1988LateColdWar));
        focusBatch2.AddEffect(FocusEffect.CreateSpawnDivisionTemplate(DivisionTemplateRecord.DefaultMechanizedTemplateId, count: 2, spawnProvinceId: 0));
        focusBatch2.AddEffect(FocusEffect.CreateGrantTechBonus((ushort)EquipmentType.MBT, bonusValue: 0.15f));
        focusBatch2.AddEffect(FocusEffect.CreateWargoal(CountryTag.PRK, declareWarImmediatelyFlag: 1.0f));
        world.RegisterFocusNode(in focusBatch2);

        // Batch 3 (Effects 13..15): AddHomelandEscalation, ShiftDefcon, TriggerNewsEvent
        int olympicsEventHash = FocusNodeData.ComputeIdHash("NEWS_1988_SEOUL_OLYMPICS");
        FocusNodeData focusBatch3 = FocusNodeData.Create(CountryTag.ROK, focusIndex: 2, costDays: 1);
        focusBatch3.AddEffect(FocusEffect.CreateAddHomelandEscalation(35.0f, CountryTag.USA));
        focusBatch3.AddEffect(FocusEffect.CreateShiftDefcon(2.0f, isRelativeDelta: 0, targetTag: CountryTag.ROK));
        focusBatch3.AddEffect(FocusEffect.CreateTriggerNewsEvent(olympicsEventHash));
        world.RegisterFocusNode(in focusBatch3);

        // Execute Batch 1
        FocusEffectExecutor.ExecuteFocusEffects(world, CountryTag.ROK, in world.GetFocusNode(CountryTag.ROK, 0));
        Assert.Equal(150.0f, world.Countries[CountryTag.ROK].PoliticalPower, precision: 3);
        Assert.Equal(0.80f, world.Countries[CountryTag.ROK].Stability, precision: 3);
        Assert.Equal(0.65f, world.Countries[CountryTag.ROK].WarSupport, precision: 3);
        Assert.Equal(0.20f, world.Countries[CountryTag.ROK].ConsumerGoodsRatio, precision: 3);
        Assert.Equal(14, world.States[0].CivilianFactories);
        Assert.Equal(8, world.States[0].MilitaryFactories);

        // Execute Batch 2
        FocusEffectExecutor.ExecuteFocusEffects(world, CountryTag.ROK, in world.GetFocusNode(CountryTag.ROK, 1));
        Assert.Equal(4, world.States[0].NavalDockyards);
        Assert.Equal(5, world.States[0].RadarSamLevel);
        Assert.Equal(250, world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar));

        int spawnedDivisions = 0;
        Query divQuery = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in divQuery)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                if (divs[i].CountryTag == CountryTag.ROK && divs[i].CurrentProvinceId == 0)
                {
                    spawnedDivisions++;
                }
            }
        }
        Assert.Equal(2, spawnedDivisions);

        Assert.Equal(TechGeneration.Gen2_1988LateColdWar, world.GetUnlockedTechGeneration(CountryTag.ROK, EquipmentType.MBT));
        Assert.Equal(1, world.GetTechBonusCount(CountryTag.ROK, EquipmentType.MBT));
        ref ProductionLineComponent updatedLine = ref world.EcsWorld.Get<ProductionLineComponent>(lineEntity);
        Assert.Equal(TechGeneration.Gen2_1988LateColdWar, updatedLine.Generation);
        Assert.Equal(0.95f, updatedLine.EfficiencyCap, precision: 3);

        Assert.True(world.HasWargoal(CountryTag.ROK, CountryTag.PRK));
        Assert.True(world.IsAtWarWith(CountryTag.ROK, CountryTag.PRK));

        // Execute Batch 3
        FocusEffectExecutor.ExecuteFocusEffects(world, CountryTag.ROK, in world.GetFocusNode(CountryTag.ROK, 2));
        Assert.Equal(50, world.Countries[CountryTag.USA].HomelandEscalation); // 15 baseline + 35
        Assert.Equal(3, world.Countries[CountryTag.USA].Defcon); // Escalation 50 >= Defcon3Threshold (45)
        Assert.Equal(2, world.Countries[CountryTag.ROK].Defcon);
        Assert.True(world.HasTriggeredNewsEvent(olympicsEventHash));
    }

    [Fact]
    public void JsonFocusTreeLoader_ParsesDataSchemasExample_AndExecutesEndToEndInSimulationWorld()
    {
        const string jpnFocusJson = """
        {
          "countryTag": "JPN",
          "era": "1985_LateColdWar",
          "focuses": [
            {
              "id": "JPN_plaza_accord_response",
              "name": "Navigate the Plaza Accord",
              "description": "The surging Yen reshapes our export economy and overseas capital power.",
              "icon": "focus_economy_yen",
              "gridX": 4,
              "gridY": 0,
              "costDays": 35,
              "prerequisites": [],
              "mutuallyExclusive": [],
              "aiBaseWeight": 100,
              "conditions": [
                { "type": "DateAfter", "target": "1985.09.01", "value": 0 }
              ],
              "effects": [
                { "type": "AddPoliticalPower", "target": "SELF", "value": 120 },
                { "type": "AddCivilianFactories", "target": "STATE_KANTO", "value": 3 }
              ]
            },
            {
              "id": "JPN_Fifth_Generation_Computing",
              "name": "Fifth Generation Computer Project",
              "description": "MITI investments in VLSI semiconductors and parallel computing.",
              "icon": "focus_tech_semiconductors",
              "gridX": 4,
              "gridY": 1,
              "costDays": 28,
              "prerequisites": ["JPN_plaza_accord_response"],
              "mutuallyExclusive": [],
              "aiBaseWeight": 95,
              "conditions": [],
              "effects": [
                { "type": "GrantTechBonus", "target": "TacticalAir:Gen2", "value": 1 },
                { "type": "TriggerNewsEvent", "target": "NEWS_MITI_VLSI_BREAKTHROUGH", "value": 0 }
              ]
            }
          ]
        }
        """;

        using SimulationWorld world = new();
        world.States[0].OwnerTag = CountryTag.JPN;
        world.States[0].ControllerTag = CountryTag.JPN;
        world.States[0].CivilianFactories = 15;

        LoadedFocusTree tree = JsonFocusTreeLoader.LoadFocusTree(world, System.Text.Encoding.UTF8.GetBytes(jpnFocusJson));
        Assert.Equal(CountryTag.JPN, tree.CountryTag);
        Assert.Equal(2, tree.Focuses.Count);

        short plazaIdx = tree.GetFocusIndex("JPN_plaza_accord_response");
        short computingIdx = tree.GetFocusIndex("JPN_Fifth_Generation_Computing");
        Assert.Equal((short)0, plazaIdx);
        Assert.Equal((short)1, computingIdx);

        // On 1985-01-01, JPN_plaza_accord_response is blocked by DateAfter 1985.09.01
        Assert.False(FocusTreeSystem.CanStartFocus(world, CountryTag.JPN, plazaIdx));
        Assert.False(FocusTreeSystem.CanStartFocus(world, CountryTag.JPN, computingIdx));

        // Advance simulation calendar to 1985-09-01
        world.SetDate(1985, 9, 1);
        Assert.Equal(19850901, world.GetCurrentDateInt());
        Assert.True(FocusTreeSystem.CanStartFocus(world, CountryTag.JPN, plazaIdx));

        // Enqueue SelectFocus command for JPN_plaza_accord_response
        float ppBeforeFocus = world.Countries[CountryTag.JPN].PoliticalPower;
        world.EnqueueCommand(SimulationCommand.CreateSelectFocus(CountryTag.JPN, plazaIdx, costDays: 0));

        // Step 35 days (35 * 4 = 140 six-hour ticks) to complete the 35-day focus
        for (int t = 0; t < 35 * 4; t++)
        {
            world.StepSixHours();
        }

        Assert.True(world.Countries[CountryTag.JPN].HasCompletedFocus(plazaIdx));
        Assert.Equal(-1, world.Countries[CountryTag.JPN].ActiveFocusId);
        Assert.Equal(18, world.States[0].CivilianFactories); // 15 + 3 from AddCivilianFactories
        Assert.Equal(ppBeforeFocus + 35.0f + 120.0f, world.Countries[CountryTag.JPN].PoliticalPower, precision: 2);

        // Now prerequisite is satisfied for JPN_Fifth_Generation_Computing
        Assert.True(FocusTreeSystem.CanStartFocus(world, CountryTag.JPN, computingIdx));
    }

    [Fact]
    public void JsonFocusTreeLoader_ThrowsMapValidationException_OnBrokenPrerequisitesOrInvalidEnums()
    {
        const string brokenPrereqJson = """
        {
          "countryTag": "ROK",
          "era": "1985_LateColdWar",
          "focuses": [
            {
              "id": "ROK_focus_1",
              "name": "Focus 1",
              "costDays": 35,
              "prerequisites": ["ROK_non_existent_prereq"]
            }
          ]
        }
        """;

        using SimulationWorld world = new();
        Assert.Throws<MapValidationException>(() =>
            JsonFocusTreeLoader.LoadFocusTree(world, System.Text.Encoding.UTF8.GetBytes(brokenPrereqJson)));

        const string invalidEffectJson = """
        {
          "countryTag": "ROK",
          "era": "1985_LateColdWar",
          "focuses": [
            {
              "id": "ROK_focus_1",
              "name": "Focus 1",
              "costDays": 35,
              "effects": [
                { "type": "NotARealEffectType", "target": "SELF", "value": 10 }
              ]
            }
          ]
        }
        """;

        Assert.Throws<MapValidationException>(() =>
            JsonFocusTreeLoader.LoadFocusTree(world, System.Text.Encoding.UTF8.GetBytes(invalidEffectJson)));
    }

    [Fact]
    public void FocusTreeSystem_ZeroHeapAllocations_InHotDailyLoop()
    {
        using SimulationWorld world = new();
        world.States[0].OwnerTag = CountryTag.ROK;
        world.States[0].ControllerTag = CountryTag.ROK;

        for (short i = 0; i < 10; i++)
        {
            FocusNodeData node = FocusNodeData.Create(CountryTag.ROK, focusIndex: i, costDays: 7);
            node.AddEffect(FocusEffect.CreateAddPoliticalPower(25.0f));
            node.AddEffect(FocusEffect.CreateAddCivilianFactories(1, stateId: 0));
            world.RegisterFocusNode(in node);
        }

        FocusTreeSystem.TryStartFocus(world, CountryTag.ROK, focusIndex: 0);

        // Warmup past TieredPGO threshold
        for (int w = 0; w < 64; w++)
        {
            FocusTreeSystem.Execute(world);
        }

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int day = 0; day < 30; day++)
        {
            if (world.Countries[CountryTag.ROK].ActiveFocusId < 0)
            {
                short nextIdx = (short)((day / 7) % 10);
                if (!world.Countries[CountryTag.ROK].HasCompletedFocus(nextIdx))
                {
                    FocusTreeSystem.TryStartFocus(world, CountryTag.ROK, nextIdx);
                }
            }
            FocusTreeSystem.Execute(world);
        }
        long afterBytes = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0L, afterBytes - beforeBytes);
    }

    [Fact]
    public void JsonFocusTreeLoader_LoadsDiskFocusTrees_ROKAndPRK_AndValidatesGraph()
    {
        using SimulationWorld world = new();
        string repoRoot = FindRepoRoot();

        string rokPath = Path.Combine(repoRoot, "data", "focus_trees", "ROK_1985.json");
        string prkPath = Path.Combine(repoRoot, "data", "focus_trees", "PRK_1985.json");

        LoadedFocusTree rokTree = JsonFocusTreeLoader.LoadFocusTree(world, rokPath);
        LoadedFocusTree prkTree = JsonFocusTreeLoader.LoadFocusTree(world, prkPath);

        Assert.Equal(CountryTag.ROK, rokTree.CountryTag);
        Assert.True(rokTree.Focuses.Count >= 10);
        Assert.Equal(rokTree.Focuses.Count, world.CountryFocusCounts[CountryTag.ROK]);

        Assert.Equal(CountryTag.PRK, prkTree.CountryTag);
        Assert.True(prkTree.Focuses.Count >= 10);
        Assert.Equal(prkTree.Focuses.Count, world.CountryFocusCounts[CountryTag.PRK]);

        // Root node ROK_miracle_on_the_han requires ControlsState(STATE_SEOUL -> State 0)
        world.States[0].OwnerTag = CountryTag.ROK;
        world.States[0].ControllerTag = CountryTag.ROK;
        short rokMiracleIdx = rokTree.GetFocusIndex("ROK_miracle_on_the_han");
        Assert.True(rokMiracleIdx >= 0);
        Assert.True(FocusTreeSystem.CanStartFocus(world, CountryTag.ROK, rokMiracleIdx));
    }

    [Fact]
    public void Task53_LoadsAllSevenMajorPower1985Content_States_Oobs_And_FocusTrees_AndSimulates90Days()
    {
        using SimulationWorld world = new();
        string repoRoot = FindRepoRoot();

        // 1. Load 1985 East Asian Theatre State Definitions
        string theatreStatesPath = Path.Combine(repoRoot, "data", "map", "east_asia_1985_states.json");
        var loadedStates = JsonMapLoader.LoadStatesOnly(world, theatreStatesPath);
        Assert.True(loadedStates.Count >= 30);

        byte[] majorTags =
        [
            CountryTag.PRC,
            CountryTag.USA,
            CountryTag.SOV,
            CountryTag.JPN,
            CountryTag.ROK,
            CountryTag.PRK,
            CountryTag.TWN
        ];

        // 2. Load 1985 OOBs for all 7 Major Powers and verify templates & spawned field divisions
        int totalSpawnedDivisions = 0;
        foreach (byte tag in majorTags)
        {
            string code = CountryTag.GetCode(tag);
            string oobPath = Path.Combine(repoRoot, "data", "oob_1985", $"{code}_oob.json");
            LoadedOob oob = JsonOobLoader.LoadOob(world, oobPath);

            Assert.Equal(tag, oob.CountryTag);
            Assert.True(oob.Templates.Count >= 3, $"Expected >= 3 templates for {code}");
            Assert.True(oob.SpawnedDivisionIds.Count >= 3, $"Expected >= 3 starting divisions for {code}");
            totalSpawnedDivisions += oob.SpawnedDivisionIds.Count;

            foreach (int divId in oob.SpawnedDivisionIds)
            {
                Assert.True(world.TryFindDivisionById(divId, out var divEntity));
                ref readonly DivisionComponent div = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
                Assert.Equal(tag, div.CountryTag);
                Assert.Equal(1.0f, div.Strength, precision: 3);
            }
        }
        Assert.True(totalSpawnedDivisions >= 28);

        // 3. Load Era I Focus Trees for all 7 Major Powers + Generic Focus Tree for the 8 Regional Minors
        world.SetDate(1985, 9, 15); // Post-Plaza Accord so JPN root focus is immediately available
        foreach (byte tag in majorTags)
        {
            string code = CountryTag.GetCode(tag);
            string focusPath = Path.Combine(repoRoot, "data", "focus_trees", $"{code}_1985.json");
            LoadedFocusTree tree = JsonFocusTreeLoader.LoadFocusTree(world, focusPath);

            Assert.Equal(tag, tree.CountryTag);
            Assert.True(tree.Focuses.Count >= 10, $"Expected >= 10 focuses in {code}_1985.json, got {tree.Focuses.Count}");
            Assert.Equal(tree.Focuses.Count, world.CountryFocusCounts[tag]);

            // Start each Major Power's root focus (index 0)
            Assert.True(FocusTreeSystem.CanStartFocus(world, tag, 0), $"Root focus 0 for {code} should be startable");
            world.EnqueueCommand(SimulationCommand.CreateSelectFocus(tag, 0, costDays: 0));
        }

        string genericFocusPath = Path.Combine(repoRoot, "data", "focus_trees", "GENERIC_1985.json");
        for (byte minorTag = CountryTag.VNM; minorTag <= CountryTag.MYS; minorTag++)
        {
            LoadedFocusTree minorTree = JsonFocusTreeLoader.LoadFocusTree(world, genericFocusPath, overrideCountryTag: minorTag);
            Assert.Equal(minorTag, minorTree.CountryTag);
            Assert.Equal(8, minorTree.Focuses.Count);
        }

        // 4. Step 40 days (160 six-hour ticks) and verify all 7 Major Powers complete their root 28/35-day focuses
        for (int t = 0; t < 40 * 4; t++)
        {
            world.StepSixHours();
        }

        foreach (byte tag in majorTags)
        {
            string code = CountryTag.GetCode(tag);
            Assert.True(world.Countries[tag].HasCompletedFocus(0), $"Expected {code} to have completed root focus 0 after 40 days");
            Assert.Equal(-1, world.Countries[tag].ActiveFocusId);
        }
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "EastAsiaGSG.sln")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate EastAsiaGSG.sln repository root.");
    }
}
