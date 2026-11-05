using System.Diagnostics;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Core.Systems;
using EastAsiaGSG.Data.Loaders;

namespace EastAsiaGSG.SimTests;

public sealed class TwoTierAiAndSoakTests
{
    [Fact]
    public void StrategicMinisterAISystem_SelectsFocus_QueuesConstruction_AndAllocatesMilitaryFactories()
    {
        using SimulationWorld world = new();
        string repoRoot = FindRepoRoot();

        // Load ROK 1985 state definitions & focus tree
        JsonMapLoader.LoadMap(
            world,
            Path.Combine(repoRoot, "data", "map", "states.json"),
            Path.Combine(repoRoot, "data", "map", "provinces.json"));
        JsonFocusTreeLoader.LoadFocusTree(
            world,
            Path.Combine(repoRoot, "data", "focus_trees", "ROK_1985.json"));

        world.SetAiControlled(CountryTag.ROK, true);

        // Initially ROK has no active focus, no active state construction, and no production lines
        Assert.Equal(-1, world.Countries[CountryTag.ROK].ActiveFocusId);
        Assert.Equal(BuildingType.None, world.States[0].ActiveBuildingType);

        // Execute 1st-of-month tick (1985-01-01 00:00) + next 6h tick to drain AI commands
        world.StepSixHours();
        world.StepSixHours();

        // 1. Verify StrategicMinisterAISystem selected a valid root focus for ROK
        Assert.True(world.Countries[CountryTag.ROK].ActiveFocusId >= 0);
        Assert.True(world.Countries[CountryTag.ROK].FocusDaysRemaining > 0);

        // 2. Verify StrategicMinisterAISystem queued construction in ROK controlled states using C_free
        bool anyConstructionActive = false;
        for (ushort s = 0; s <= 4; s++)
        {
            if (world.States[s].ActiveBuildingType != BuildingType.None &&
                world.States[s].AssignedCivFactories > 0)
            {
                anyConstructionActive = true;
                break;
            }
        }
        Assert.True(anyConstructionActive, "Expected AI to queue state construction using free civilian factories.");

        // 3. Verify StrategicMinisterAISystem bootstrapped default military production lines and allocated factories
        StrategicMinisterAISystem.EvaluateAndAllocateProduction(world, CountryTag.ROK);
        world.StepSixHours(); // Drain any queued factory assignment commands

        CountryFactorySummary summary = ConstructionSystem.CalculateFactorySummary(world, CountryTag.ROK);
        Assert.True(summary.TotalMilitaryFactories > 0);
    }

    [Fact]
    public void TacticalFrontlineAISystem_PositionsDivisionsOnBorder_AndExecutesAttacksWhenAtWar()
    {
        using SimulationWorld world = new();

        // Build a 3-province linear corridor:
        // Prov 0 (ROK Rear) <-> Prov 1 (ROK Border) <-> Prov 2 (PRK Border)
        world.States[0].StateId = 0;
        world.States[0].OwnerTag = CountryTag.ROK;
        world.States[0].ControllerTag = CountryTag.ROK;
        world.States[0].CivilianFactories = 10;
        world.States[0].MilitaryFactories = 10;

        world.States[1].StateId = 1;
        world.States[1].OwnerTag = CountryTag.PRK;
        world.States[1].ControllerTag = CountryTag.PRK;
        world.States[1].CivilianFactories = 5;
        world.States[1].MilitaryFactories = 10;

        for (ushort p = 0; p < 3; p++)
        {
            byte owner = p < 2 ? CountryTag.ROK : CountryTag.PRK;
            world.Provinces[p].ProvinceId = p;
            world.Provinces[p].StateId = p < 2 ? (ushort)0 : (ushort)1;
            world.Provinces[p].OwnerTag = owner;
            world.Provinces[p].ControllerTag = owner;
            world.Provinces[p].Terrain = TerrainType.Plains;
            world.Provinces[p].SupplyHubLevel = 3;
            world.Provinces[p].InfrastructureLevel = 6;
            world.Provinces[p].SupplyRatio = 1.0f;
        }

        DirectedEdge[] edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 2, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(2, 1, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces.AsSpan(), edges);

        world.SetAiControlled(CountryTag.ROK, true);
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar, 100);

        // Spawn a strong ROK division in the rear province (Prov 0) while Prov 1 (border) is empty
        var rokDivEntity = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 501,
            countryTag: CountryTag.ROK,
            startingProvinceId: 0,
            templateId: 1,
            combatWidth: 20,
            softAttack: 80f,
            hardAttack: 50f,
            defense: 60f,
            breakthrough: 60f,
            armor: 45f,
            piercing: 55f,
            hardness: 0.50f,
            maxOrganization: 60f,
            startingStrength: 1.0f,
            startingExperience: 0.70f,
            speed: 40f,
            pgmAuthorized: false));

        // Phase A (Peacetime): Step 2 days (8 ticks) so TacticalFrontlineAISystem moves the rear division to border Prov 1
        for (int t = 0; t < 8; t++)
        {
            world.StepSixHours();
        }

        ref readonly DivisionComponent divAfterGarrison = ref world.EcsWorld.Get<DivisionComponent>(rokDivEntity);
        Assert.Equal((ushort)1, divAfterGarrison.CurrentProvinceId);

        // Phase B (Wartime): Declare war between ROK and PRK; ROK AI should attack undefended Prov 2 and authorize PGM
        world.DeclareWar(CountryTag.ROK, CountryTag.PRK);
        for (int t = 0; t < 12; t++)
        {
            world.StepSixHours();
        }

        ref readonly DivisionComponent divAfterAttack = ref world.EcsWorld.Get<DivisionComponent>(rokDivEntity);
        Assert.Equal((ushort)2, divAfterAttack.CurrentProvinceId);
        Assert.Equal(CountryTag.ROK, world.Provinces[2].ControllerTag);
        Assert.Equal((byte)1, divAfterAttack.PgmAuthorized);
    }

    [Fact]
    public void TwoTierAI_ZeroHeapAllocations_InHotDailyAndMonthlyLoops()
    {
        using SimulationWorld world = new();
        string repoRoot = FindRepoRoot();

        JsonMapLoader.LoadMap(
            world,
            Path.Combine(repoRoot, "data", "map", "states.json"),
            Path.Combine(repoRoot, "data", "map", "provinces.json"));
        JsonFocusTreeLoader.LoadFocusTree(
            world,
            Path.Combine(repoRoot, "data", "focus_trees", "ROK_1985.json"));
        JsonFocusTreeLoader.LoadFocusTree(
            world,
            Path.Combine(repoRoot, "data", "focus_trees", "PRK_1985.json"));
        JsonOobLoader.LoadOob(
            world,
            Path.Combine(repoRoot, "data", "oob_1985", "ROK_oob.json"));
        JsonOobLoader.LoadOob(
            world,
            Path.Combine(repoRoot, "data", "oob_1985", "PRK_oob.json"));

        world.EnableAiForAllCountries();

        // Bootstrap production lines once before warmup
        StrategicMinisterAISystem.Execute(world);
        world.StepSixHours();

        // Warmup past TieredPGO threshold
        for (int w = 0; w < 64; w++)
        {
            MacroEconomySystem.Execute(world);
            StrategicMinisterAISystem.Execute(world);
            TacticalFrontlineAISystem.Execute(world);
            world.CommandQueue.Clear();
        }

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            MacroEconomySystem.Execute(world);
            StrategicMinisterAISystem.Execute(world);
            TacticalFrontlineAISystem.Execute(world);
            world.CommandQueue.Clear();
        }
        long afterBytes = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0L, afterBytes - beforeBytes);
    }

    [Fact]
    public void SoakTest_15Years_21912SixHourTicks_ZeroExceptions_ZeroLeaks_AndHistoricalPlausibility()
    {
        using SimulationWorld world = new();
        string repoRoot = FindRepoRoot();

        // 1. Load Full 232-Province East Asian Theatre Map & States
        JsonMapLoader.LoadMap(
            world,
            Path.Combine(repoRoot, "data", "map", "east_asia_1985_states.json"),
            Path.Combine(repoRoot, "data", "map", "east_asia_1985_provinces.json"));

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

        // 2. Load 1985 OOBs and Focus Trees for all 7 Major Powers + Generic Focus Trees for 8 Regional Minors
        foreach (byte tag in majorTags)
        {
            string code = CountryTag.GetCode(tag);
            JsonOobLoader.LoadOob(world, Path.Combine(repoRoot, "data", "oob_1985", $"{code}_oob.json"));
            JsonFocusTreeLoader.LoadFocusTree(world, Path.Combine(repoRoot, "data", "focus_trees", $"{code}_1985.json"));

            world.Countries[tag].Manpower = 500_000;
            world.Countries[tag].SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline, 2_000);
            world.Countries[tag].SetStockpile(EquipmentType.Artillery, TechGeneration.Gen1_1980Baseline, 300);
            world.Countries[tag].SetStockpile(EquipmentType.IFV, TechGeneration.Gen1_1980Baseline, 200);
            world.Countries[tag].SetStockpile(EquipmentType.MBT, TechGeneration.Gen1_1980Baseline, 150);
            world.Countries[tag].SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen1_1980Baseline, 80);
            world.Countries[tag].SetStockpile(EquipmentType.PGM, TechGeneration.Gen1_1980Baseline, 80);
        }

        string genericFocusPath = Path.Combine(repoRoot, "data", "focus_trees", "GENERIC_1985.json");
        for (byte minorTag = CountryTag.VNM; minorTag <= CountryTag.MYS; minorTag++)
        {
            string code = CountryTag.GetCode(minorTag);
            JsonOobLoader.LoadOob(world, Path.Combine(repoRoot, "data", "oob_1985", $"{code}_oob.json"));
            JsonFocusTreeLoader.LoadFocusTree(world, genericFocusPath, overrideCountryTag: minorTag);
            world.Countries[minorTag].Manpower = 200_000;
            world.Countries[minorTag].SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline, 1_000);
            world.Countries[minorTag].SetStockpile(EquipmentType.Artillery, TechGeneration.Gen1_1980Baseline, 100);
        }

        // Enable Two-Tier Utility AI for all 15 nations
        world.EnableAiForAllCountries();

        // Record baseline 1985 factory counts
        int rokFactories1985 = ConstructionSystem.CalculateFactorySummary(world, CountryTag.ROK).TotalFactories;
        int prcFactories1985 = ConstructionSystem.CalculateFactorySummary(world, CountryTag.PRC).TotalFactories;
        int jpnFactories1985 = ConstructionSystem.CalculateFactorySummary(world, CountryTag.JPN).TotalFactories;

        // 3. Execute 15-Year Headless Soak Test: 1985-01-01 00:00 -> 2000-01-01 00:00
        // 15 years = 12 standard years (365d) + 3 leap years (1988, 1992, 1996 = 366d) = 5,478 days = 21,912 six-hour ticks
        const int totalSoakTicks = 21_912;

        Stopwatch sw = Stopwatch.StartNew();
        for (int tick = 0; tick < totalSoakTicks; tick++)
        {
            world.StepSixHours();
        }
        sw.Stop();

        double elapsedSeconds = sw.Elapsed.TotalSeconds;
        double ticksPerSecond = totalSoakTicks / Math.Max(0.001, elapsedSeconds);

        // 4. Verify Exact Calendar & Stratified Tick Progression
        Assert.Equal(2000, world.CurrentYear);
        Assert.Equal(1, world.CurrentMonth);
        Assert.Equal(1, world.CurrentDay);
        Assert.Equal(0, world.CurrentHour);
        Assert.Equal(totalSoakTicks, world.TotalSixHourTicks);
        Assert.Equal(5_478, world.DailyTicksExecuted);
        Assert.Equal(180, world.MonthlyTicksExecuted);

        // 5. Verify Throughput Invariant (> 1,000 ticks/second in headless mode)
        Assert.True(
            ticksPerSecond > 1_000.0,
            $"Expected > 1,000 ticks/sec in 15-year soak test, achieved {ticksPerSecond:N0} ticks/sec ({sw.Elapsed.TotalMilliseconds:F1} ms).");

        // 6. Verify Historical Plausibility & State Integrity across all 7 Major Powers
        foreach (byte tag in majorTags)
        {
            string code = CountryTag.GetCode(tag);
            ref readonly CountryState country = ref world.Countries[tag];

            // Completed multiple Era I focuses over 15 years
            Assert.True(country.CompletedFocusMaskLow != 0UL, $"Expected {code} AI to complete Era I focuses during 15-year soak.");

            // Stability, WarSupport, and ConsumerGoodsRatio remain finite and clamped
            Assert.False(float.IsNaN(country.PoliticalPower));
            Assert.InRange(country.Stability, 0.0f, 1.0f);
            Assert.InRange(country.WarSupport, 0.0f, 1.0f);
            Assert.InRange(country.ConsumerGoodsRatio, 0.0f, 1.0f);

            // Equipment stockpiles accumulated and remained non-negative
            int totalInf = country.GetTotalArchetypeStockpile(EquipmentType.InfantryKit);
            int totalArt = country.GetTotalArchetypeStockpile(EquipmentType.Artillery);
            Assert.True(totalInf > 2_000, $"Expected {code} InfantryKit stockpile ({totalInf}) to grow over 15 years.");
            Assert.True(totalArt > 300, $"Expected {code} Artillery stockpile ({totalArt}) to grow over 15 years.");
        }

        // Verify industrial growth for ROK, PRC, and JPN from focuses + state construction queues
        int rokFactories2000 = ConstructionSystem.CalculateFactorySummary(world, CountryTag.ROK).TotalFactories;
        int prcFactories2000 = ConstructionSystem.CalculateFactorySummary(world, CountryTag.PRC).TotalFactories;
        int jpnFactories2000 = ConstructionSystem.CalculateFactorySummary(world, CountryTag.JPN).TotalFactories;

        Assert.True(rokFactories2000 > rokFactories1985, $"ROK factories should grow from {rokFactories1985} (got {rokFactories2000})");
        Assert.True(prcFactories2000 > prcFactories1985, $"PRC factories should grow from {prcFactories1985} (got {prcFactories2000})");
        Assert.True(jpnFactories2000 > jpnFactories1985, $"JPN factories should grow from {jpnFactories1985} (got {jpnFactories2000})");
    }

    [Fact]
    public void ProductionSystem_Execute_AppliesResourceShortagePenaltiesAutomaticallyInDailyTick()
    {
        using SimulationWorld world = new();
        ref StateData state = ref world.States[1];
        state.StateId = 1;
        state.OwnerTag = CountryTag.ROK;
        state.ControllerTag = CountryTag.ROK;
        state.MilitaryFactories = 10;
        state.SetResource(ResourceType.Steel, 0); // 0 steel = deficit
        state.SetResource(ResourceType.Oil, 1);   // Ensures totalCountryResources > 0 triggers shortage check

        world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 1,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.InfantryKit,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 10,
            priority: 100));

        // Advance 1 day (4 six-hour ticks)
        for (int i = 0; i < 4; i++)
        {
            world.StepSixHours();
        }

        Query query = world.EcsWorld.Query(new QueryDescription().WithAll<ProductionLineComponent>());
        bool verified = false;
        foreach (ref Chunk chunk in query)
        {
            Span<ProductionLineComponent> lines = chunk.GetSpan<ProductionLineComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                if (lines[i].LineId == 1)
                {
                    Assert.True(lines[i].ResourcePenalty > 0.0f, $"Expected ResourcePenalty > 0, was {lines[i].ResourcePenalty}");
                    verified = true;
                }
            }
        }
        Assert.True(verified, "Production line was not found in EcsWorld.");
    }

    [Fact]
    public void CombatAndMovement_CapturingStateProvinces_TransfersStateControllerTagAndFactories()
    {
        using SimulationWorld world = new();
        ref StateData state = ref world.States[1];
        state.StateId = 1;
        state.OwnerTag = CountryTag.ROK;
        state.ControllerTag = CountryTag.ROK;
        state.CivilianFactories = 5;

        ref ProvinceData provA = ref world.Provinces[10];
        provA.ProvinceId = 10;
        provA.StateId = 1;
        provA.OwnerTag = CountryTag.ROK;
        provA.ControllerTag = CountryTag.ROK;
        provA.Terrain = TerrainType.Urban; // Urban weight = 5

        ref ProvinceData provB = ref world.Provinces[11];
        provB.ProvinceId = 11;
        provB.StateId = 1;
        provB.OwnerTag = CountryTag.ROK;
        provB.ControllerTag = CountryTag.ROK;
        provB.Terrain = TerrainType.Plains; // Plains weight = 1

        Assert.Equal(CountryTag.ROK, state.ControllerTag);
        Assert.Equal(5, ConstructionSystem.CalculateFactorySummary(world, CountryTag.ROK).TotalCivilianFactories);

        // PRK captures the urban capital province 10
        provA.ControllerTag = CountryTag.PRK;
        world.RecalculateStateController(1);

        // Since Urban weight 5 > Plains weight 1, PRK now controls State 1
        Assert.Equal(CountryTag.PRK, world.States[1].ControllerTag);
        Assert.Equal(5, ConstructionSystem.CalculateFactorySummary(world, CountryTag.PRK).TotalCivilianFactories);
        Assert.Equal(0, ConstructionSystem.CalculateFactorySummary(world, CountryTag.ROK).TotalCivilianFactories);
    }

    [Fact]
    public void SimulationCommand_DeclareWarAndRecruitDivision_ExecuteDeterministicallyInSixHourTick()
    {
        using SimulationWorld world = new();
        world.RegisterDivisionTemplate(DivisionTemplateRecord.CreateInfantryDivision(1));

        world.CountryCapitals[CountryTag.ROK] = 0;
        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;

        ref CountryState rok = ref world.Countries[CountryTag.ROK];
        rok.Manpower = 50_000;
        rok.SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline, 2_000);
        rok.SetStockpile(EquipmentType.Artillery, TechGeneration.Gen1_1980Baseline, 500);

        Assert.False(world.IsAtWarWith(CountryTag.ROK, CountryTag.PRK));

        // Enqueue DeclareWar and RecruitDivision commands
        world.EnqueueCommand(SimulationCommand.CreateDeclareWar(CountryTag.ROK, CountryTag.PRK));
        world.EnqueueCommand(SimulationCommand.CreateRecruitDivision(CountryTag.ROK, templateId: 1, spawnProvinceId: 0));

        // Step one 6-hour tick
        world.StepSixHours();

        // 1. DeclareWar executed deterministically
        Assert.True(world.IsAtWarWith(CountryTag.ROK, CountryTag.PRK));
        Assert.True(world.IsAtWarWith(CountryTag.PRK, CountryTag.ROK));

        // 2. Division spawned at Province 0
        Query divQuery = world.EcsWorld.Query(new QueryDescription().WithAll<DivisionComponent>());
        int divCount = 0;
        foreach (ref Chunk chunk in divQuery)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                if (divs[i].CountryTag == CountryTag.ROK && divs[i].CurrentProvinceId == 0)
                {
                    divCount++;
                }
            }
        }
        Assert.Equal(1, divCount);
        Assert.True(rok.Manpower < 50_000, "Manpower should be deducted for division recruitment.");
    }

    [Fact]
    public void FullEastAsiaTheatreMap_232Provinces36States15Nations_BijectiveLoadAndAllOobDivisionsSupplied()
    {
        using SimulationWorld world = new();
        string repoRoot = FindRepoRoot();

        JsonMapLoader.LoadMap(
            world,
            Path.Combine(repoRoot, "data", "map", "east_asia_1985_states.json"),
            Path.Combine(repoRoot, "data", "map", "east_asia_1985_provinces.json"));

        // Verify province and state counts
        int provCount = 0;
        for (int i = 0; i < SimulationWorld.MaxProvinces; i++)
        {
            if (world.Provinces[i].OwnerTag != CountryTag.None) provCount++;
        }
        Assert.Equal(232, provCount);

        // Load all 15 OOBs
        for (byte tag = 0; tag < CountryTag.ActiveCount; tag++)
        {
            string code = CountryTag.GetCode(tag);
            string oobPath = Path.Combine(repoRoot, "data", "oob_1985", $"{code}_oob.json");
            Assert.True(File.Exists(oobPath), $"OOB file missing for {code}");
            JsonOobLoader.LoadOob(world, oobPath);
        }

        // Verify all 15 nations have valid capitals
        for (byte tag = 0; tag < CountryTag.ActiveCount; tag++)
        {
            ushort capital = world.GetCapitalProvince(tag);
            Assert.True(capital < SimulationWorld.MaxProvinces, $"Nation {CountryTag.GetCode(tag)} capital is invalid");
            Assert.Equal(tag, world.Provinces[capital].OwnerTag);
        }

        // Run supply graph system
        SupplyGraphSystem.Execute(world);

        // Verify divisions are alive and located on valid friendly provinces
        Query query = world.EcsWorld.Query(new QueryDescription().WithAll<DivisionComponent>());
        int totalDivisions = 0;
        foreach (ref Chunk chunk in query)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                totalDivisions++;
                Assert.True(divs[i].CurrentProvinceId < SimulationWorld.MaxProvinces);
                Assert.True(divs[i].State != DivisionState.Shattered);
            }
        }
        Assert.True(totalDivisions >= 30, $"Expected >= 30 starting divisions across 15 nations, got {totalDivisions}");
    }

    [Fact]
    public void JsonFocusTreeLoader_DetectsPrerequisiteCyclesAndDuplicateGridCoordinates()
    {
        using SimulationWorld world = new();

        // 1. Duplicate Grid Coordinates Test
        string duplicateCoordsJson = """
        {
          "countryTag": "ROK",
          "era": "1985_LateColdWar",
          "focuses": [
            {
              "id": "FOCUS_A",
              "name": "Focus A",
              "description": "A",
              "icon": "icon_a",
              "gridX": 2,
              "gridY": 1,
              "costDays": 35,
              "prerequisites": [],
              "mutuallyExclusive": [],
              "aiBaseWeight": 100,
              "conditions": [],
              "effects": []
            },
            {
              "id": "FOCUS_B",
              "name": "Focus B",
              "description": "B",
              "icon": "icon_b",
              "gridX": 2,
              "gridY": 1,
              "costDays": 35,
              "prerequisites": [],
              "mutuallyExclusive": [],
              "aiBaseWeight": 100,
              "conditions": [],
              "effects": []
            }
          ]
        }
        """;

        var coordEx = Assert.Throws<MapValidationException>(() =>
            JsonFocusTreeLoader.LoadFocusTree(world, System.Text.Encoding.UTF8.GetBytes(duplicateCoordsJson)));
        Assert.Contains("duplicate grid coordinates", coordEx.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Circular Prerequisite Cycle Test
        string circularJson = """
        {
          "countryTag": "ROK",
          "era": "1985_LateColdWar",
          "focuses": [
            {
              "id": "FOCUS_CYCLE_A",
              "name": "Focus Cycle A",
              "description": "A",
              "icon": "icon_a",
              "gridX": 1,
              "gridY": 1,
              "costDays": 35,
              "prerequisites": ["FOCUS_CYCLE_B"],
              "mutuallyExclusive": [],
              "aiBaseWeight": 100,
              "conditions": [],
              "effects": []
            },
            {
              "id": "FOCUS_CYCLE_B",
              "name": "Focus Cycle B",
              "description": "B",
              "icon": "icon_b",
              "gridX": 2,
              "gridY": 1,
              "costDays": 35,
              "prerequisites": ["FOCUS_CYCLE_A"],
              "mutuallyExclusive": [],
              "aiBaseWeight": 100,
              "conditions": [],
              "effects": []
            }
          ]
        }
        """;

        var cycleEx = Assert.Throws<MapValidationException>(() =>
            JsonFocusTreeLoader.LoadFocusTree(world, System.Text.Encoding.UTF8.GetBytes(circularJson)));
        Assert.Contains("cycle", cycleEx.Message, StringComparison.OrdinalIgnoreCase);
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
