using System.Diagnostics;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Core.Systems;
using EastAsiaGSG.Data.Loaders;
using Xunit;

namespace EastAsiaGSG.SimTests;

/// <summary>
/// Headless xUnit tests validating the 6-hour HOI4 land combat resolution systems,
/// 72-hour historical 1985 ROK vs. PRK division battles, combat width limits,
/// PGM stockpile consumption, terrain/river defense, encirclement, and zero-allocation invariants.
/// </summary>
public sealed class CombatSystemTests
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

    #region 1985 Historical Division Template Factories

    private static DivisionComponent CreateRokMechanizedDivision(
        int divisionId,
        ushort provinceId,
        bool pgm = true)
    {
        return DivisionComponent.Create(
            divisionId: divisionId,
            countryTag: CountryTag.ROK,
            startingProvinceId: provinceId,
            templateId: 1,
            combatWidth: 20,
            softAttack: 65f,
            hardAttack: 40f,
            defense: 50f,
            breakthrough: 50f,
            armor: 45f,
            piercing: 50f,
            hardness: 0.50f,
            maxOrganization: 60f,
            startingStrength: 1.0f,
            startingExperience: 0.55f,
            speed: 28f,
            pgmAuthorized: pgm);
    }

    private static DivisionComponent CreateRokInfantryDivision(
        int divisionId,
        ushort provinceId,
        bool pgm = false)
    {
        return DivisionComponent.Create(
            divisionId: divisionId,
            countryTag: CountryTag.ROK,
            startingProvinceId: provinceId,
            templateId: 2,
            combatWidth: 20,
            softAttack: 45f,
            hardAttack: 20f,
            defense: 55f,
            breakthrough: 30f,
            armor: 20f,
            piercing: 45f,
            hardness: 0.20f,
            maxOrganization: 55f,
            startingStrength: 1.0f,
            startingExperience: 0.45f,
            speed: 16f,
            pgmAuthorized: pgm);
    }

    private static DivisionComponent CreatePrkInfantryDivision(
        int divisionId,
        ushort provinceId)
    {
        return DivisionComponent.Create(
            divisionId: divisionId,
            countryTag: CountryTag.PRK,
            startingProvinceId: provinceId,
            templateId: 3,
            combatWidth: 20,
            softAttack: 40f,
            hardAttack: 15f,
            defense: 45f,
            breakthrough: 20f,
            armor: 10f,
            piercing: 20f,
            hardness: 0.15f,
            maxOrganization: 45f,
            startingStrength: 1.0f,
            startingExperience: 0.40f,
            speed: 16f,
            pgmAuthorized: false);
    }

    private static DivisionComponent CreatePrkArmoredDivision(
        int divisionId,
        ushort provinceId)
    {
        return DivisionComponent.Create(
            divisionId: divisionId,
            countryTag: CountryTag.PRK,
            startingProvinceId: provinceId,
            templateId: 4,
            combatWidth: 20,
            softAttack: 45f,
            hardAttack: 45f,
            defense: 40f,
            breakthrough: 45f,
            armor: 35f,
            piercing: 35f,
            hardness: 0.65f,
            maxOrganization: 40f,
            startingStrength: 1.0f,
            startingExperience: 0.50f,
            speed: 24f,
            pgmAuthorized: false);
    }

    #endregion

    #region 72-Hour Combat Balance Simulations

    [Fact]
    public void Simulate72HourBattle_RokMechanizedVsPrkInfantry_BreachesLineWithin72Hours()
    {
        using SimulationWorld world = new();

        // 3-Province Linear Axis:
        // Province 0 (ROK Paju) <-> Province 1 (PRK Kaesong) <-> Province 2 (PRK Sariwon fallback)
        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].Terrain = TerrainType.Plains;
        world.Provinces[0].SupplyRatio = 1.0f;
        world.Provinces[0].CenterX = 430;
        world.Provinces[0].CenterY = 550;

        world.Provinces[1].OwnerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;
        world.Provinces[1].Terrain = TerrainType.Plains;
        world.Provinces[1].SupplyRatio = 1.0f;
        world.Provinces[1].CenterX = 420;
        world.Provinces[1].CenterY = 520;

        world.Provinces[2].OwnerTag = CountryTag.PRK;
        world.Provinces[2].ControllerTag = CountryTag.PRK;
        world.Provinces[2].Terrain = TerrainType.Plains;
        world.Provinces[2].SupplyRatio = 1.0f;
        world.Provinces[2].CenterX = 405;
        world.Provinces[2].CenterY = 470;

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 2, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(2, 1, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        // Grant ROK 100 units of Gen 2 PGM stockpile
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar, 100);

        Entity rokEntity = world.SpawnDivision(CreateRokMechanizedDivision(101, provinceId: 0, pgm: true));
        Entity prkEntity = world.SpawnDivision(CreatePrkInfantryDivision(201, provinceId: 1));

        // Enqueue attack command for ROK Division 101 against Kaesong (Province 1)
        Assert.True(world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 101, targetProvinceId: 1)));

        int battleConcludedTick = -1;

        // Run 12 six-hour ticks (exact 72-hour battle simulation)
        for (int tick = 0; tick < 12; tick++)
        {
            world.StepSixHours();

            ref DivisionComponent prk = ref world.EcsWorld.Get<DivisionComponent>(prkEntity);
            if (battleConcludedTick == -1 && prk.CurrentProvinceId == 2)
            {
                battleConcludedTick = tick + 1; // 1-based tick count
            }
        }

        ref DivisionComponent finalRok = ref world.EcsWorld.Get<DivisionComponent>(rokEntity);
        ref DivisionComponent finalPrk = ref world.EcsWorld.Get<DivisionComponent>(prkEntity);

        // 1. Line breached: PRK broke and retreated between 48 and 66 hours (ticks 8..11)
        Assert.True(battleConcludedTick is >= 8 and <= 11, $"Battle concluded at tick {battleConcludedTick} (expected 8..11)");

        // 2. Province ownership: ROK captured and occupies Province 1
        Assert.Equal((ushort)1, finalRok.CurrentProvinceId);
        Assert.Equal(CountryTag.ROK, world.Provinces[1].ControllerTag);

        // 3. PRK position: successfully retreated to Province 2
        Assert.Equal((ushort)2, finalPrk.CurrentProvinceId);
        Assert.True(finalPrk.Strength > 0.85f, $"PRK strength {finalPrk.Strength} should be modestly depleted");

        // 4. PGM Consumption: ROK consumed 5 PGM per tick during active battle (at least 40 PGM units consumed)
        int remainingPgm = world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar);
        Assert.True(remainingPgm <= 60, $"Remaining PGM should be <= 60, was {remainingPgm}");

        // 5. ROK experienced high-tempo mechanized victory with unpierced armor advantage
        Assert.True(finalRok.Strength > 0.90f, $"ROK strength {finalRok.Strength} maintained due to high breakthrough and armor");
        Assert.True(finalRok.Organization > 40.0f, $"ROK organization {finalRok.Organization} remained healthy");
    }

    [Fact]
    public void Simulate72HourBattle_PrkHumanWaveAgainstEntrenchedMountainRiver_DefenderHolds()
    {
        using SimulationWorld world = new();

        // Province 0 (PRK Kaesong) -> attacks Province 1 (ROK Gangwon Mountain Redoubt)
        world.Provinces[0].OwnerTag = CountryTag.PRK;
        world.Provinces[0].ControllerTag = CountryTag.PRK;
        world.Provinces[0].Terrain = TerrainType.Plains;
        world.Provinces[0].SupplyRatio = 1.0f;
        world.Provinces[0].CenterX = 420;
        world.Provinces[0].CenterY = 520;

        world.Provinces[1].OwnerTag = CountryTag.ROK;
        world.Provinces[1].ControllerTag = CountryTag.ROK;
        world.Provinces[1].Terrain = TerrainType.Mountain; // 0.60x attack penalty for attacker
        world.Provinces[1].SupplyRatio = 1.0f;
        world.Provinces[1].CenterX = 450;
        world.Provinces[1].CenterY = 530;

        ReadOnlySpan<DirectedEdge> edges =
        [
            // River crossing from 0 to 1 imposes additional 0.75x penalty
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard | AdjacencyEdgeFlags.River),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard | AdjacencyEdgeFlags.River)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        // Spawn 3 PRK Infantry divisions (60 width, massed human-wave assault)
        Entity prk1 = world.SpawnDivision(CreatePrkInfantryDivision(201, provinceId: 0));
        Entity prk2 = world.SpawnDivision(CreatePrkInfantryDivision(202, provinceId: 0));
        Entity prk3 = world.SpawnDivision(CreatePrkInfantryDivision(203, provinceId: 0));

        // Spawn 1 ROK Infantry division dug into mountain redoubt with max entrenchment
        Entity rokDef = world.SpawnDivision(CreateRokInfantryDivision(101, provinceId: 1));
        ref DivisionComponent rokDiv = ref world.EcsWorld.Get<DivisionComponent>(rokDef);
        rokDiv.Entrenchment = 0.20f; // +20% defense bonus

        // Order all 3 PRK divisions to attack Province 1
        Assert.True(world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.PRK, divisionEntityId: 201, targetProvinceId: 1)));
        Assert.True(world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.PRK, divisionEntityId: 202, targetProvinceId: 1)));
        Assert.True(world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.PRK, divisionEntityId: 203, targetProvinceId: 1)));

        // Run full 72-hour assault (12 six-hour ticks)
        for (int tick = 0; tick < 12; tick++)
        {
            world.StepSixHours();
        }

        ref DivisionComponent finalRokDef = ref world.EcsWorld.Get<DivisionComponent>(rokDef);

        // Verifications:
        // 1. ROK Mountain Fortress held the line over 72 hours!
        Assert.Equal((ushort)1, finalRokDef.CurrentProvinceId);
        Assert.Equal(CountryTag.ROK, world.Provinces[1].ControllerTag);
        Assert.True(finalRokDef.Organization > 30.0f, $"ROK defender organization {finalRokDef.Organization} remained robust");
        Assert.True(finalRokDef.Strength > 0.95f, $"ROK defender strength {finalRokDef.Strength} took negligible losses");

        // 2. PRK massed assault suffered severe casualties attacking across river into mountains
        ref DivisionComponent finalPrk1 = ref world.EcsWorld.Get<DivisionComponent>(prk1);
        ref DivisionComponent finalPrk2 = ref world.EcsWorld.Get<DivisionComponent>(prk2);
        ref DivisionComponent finalPrk3 = ref world.EcsWorld.Get<DivisionComponent>(prk3);

        Assert.True(finalPrk1.Organization < 35.0f || finalPrk2.Organization < 35.0f || finalPrk3.Organization < 35.0f,
            "PRK attackers took significant return fire casualties from entrenched mountain defense");
    }

    [Fact]
    public void CombatSystem_PgmStockpileDepletion_GracefulDegradation()
    {
        using SimulationWorld world = new();

        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[1].OwnerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        // Start with exactly 15 PGM (enough for exactly 3 ticks at 5 units/tick)
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar, 15);

        // Spawn high-org defender so battle lasts at least 5 ticks
        Entity att = world.SpawnDivision(CreateRokMechanizedDivision(101, 0, pgm: true));
        DivisionComponent defComp = CreatePrkInfantryDivision(201, 1);
        defComp.MaxOrganization = 100f;
        defComp.Organization = 100f;
        Entity def = world.SpawnDivision(defComp);

        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 101, targetProvinceId: 1));

        // Tick 1: Consumes 5 PGM -> 10 remaining
        world.StepSixHours();
        Assert.Equal(10, world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar));

        // Tick 2: Consumes 5 PGM -> 5 remaining
        world.StepSixHours();
        Assert.Equal(5, world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar));

        // Tick 3: Consumes 5 PGM -> 0 remaining
        world.StepSixHours();
        Assert.Equal(0, world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar));

        // Tick 4: Stockpile exhausted -> Consumes 0 PGM, stays at 0, drops PGM multiplier without exception
        world.StepSixHours();
        Assert.Equal(0, world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar));

        // Tick 5: Continues fighting smoothly with 0 PGM
        world.StepSixHours();
        Assert.Equal(0, world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar));
    }

    [Fact]
    public void CombatSystem_CombatWidthAndReserveCommitment_Enforces80WidthCap()
    {
        using SimulationWorld world = new();

        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[1].OwnerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        // Spawn 5 ROK divisions (20 width each = 100 width total) attacking across 1 axis (base 80 width cap)
        Entity r1 = world.SpawnDivision(CreateRokInfantryDivision(101, 0));
        Entity r2 = world.SpawnDivision(CreateRokInfantryDivision(102, 0));
        Entity r3 = world.SpawnDivision(CreateRokInfantryDivision(103, 0));
        Entity r4 = world.SpawnDivision(CreateRokInfantryDivision(104, 0));
        Entity r5 = world.SpawnDivision(CreateRokInfantryDivision(105, 0));

        // High-org defender
        DivisionComponent defComp = CreatePrkInfantryDivision(201, 1);
        defComp.MaxOrganization = 100f;
        defComp.Organization = 100f;
        Entity def = world.SpawnDivision(defComp);

        for (int id = 101; id <= 105; id++)
        {
            world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: id, targetProvinceId: 1));
        }

        // Tick 1: Combat engages. Exactly 4 divisions (80 width) should fit into frontline; 1 in reserve
        world.StepSixHours();

        Assert.True(world.TryFindActiveCombatAtProvince(1, out Entity combatEntity));
        ref ActiveCombatComponent combat = ref world.EcsWorld.Get<ActiveCombatComponent>(combatEntity);

        Assert.Equal(1, combat.AttackDirectionCount);
        Assert.Equal(80, combat.TotalCombatWidth);
        Assert.Equal(80, combat.UsedAttackerWidth);
        Assert.Equal(4, combat.EngagedAttackerCount);

        // Verify that Division 105 was kept in reserve and took zero damage
        ref DivisionComponent div5 = ref world.EcsWorld.Get<DivisionComponent>(r5);
        Assert.Equal(55.0f, div5.Organization);
        Assert.Equal(1.0f, div5.Strength);

        // Now simulate a frontline division running out of organization
        ref DivisionComponent div1 = ref world.EcsWorld.Get<DivisionComponent>(r1);
        div1.Organization = 0.0f;

        // Tick 2: Frontline unit breaks off; Reserve unit (div5) commits into frontline
        world.StepSixHours();

        ref DivisionComponent postDiv1 = ref world.EcsWorld.Get<DivisionComponent>(r1);
        ref DivisionComponent postDiv5 = ref world.EcsWorld.Get<DivisionComponent>(r5);

        // Div 1 broke off to Idle
        Assert.Equal(DivisionState.Idle, postDiv1.State);
        Assert.Equal(DivisionComponent.NoCombat, postDiv1.ActiveCombatId);

        // Div 5 is now in combat and participating
        Assert.Equal(DivisionState.Attacking, postDiv5.State);
    }

    [Fact]
    public void CombatSystem_MultiDirectionFlanking_ExpandsWidthTo100()
    {
        using SimulationWorld world = new();

        // 2 Origin Provinces attacking Kaesong (Province 2):
        // Province 0 (Paju) & Province 1 (Yeoncheon) -> Province 2 (Kaesong)
        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[1].OwnerTag = CountryTag.ROK;
        world.Provinces[1].ControllerTag = CountryTag.ROK;
        world.Provinces[2].OwnerTag = CountryTag.PRK;
        world.Provinces[2].ControllerTag = CountryTag.PRK;

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(0, 2, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(2, 0, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 2, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(2, 1, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        // 3 divisions in Province 0 (60 width), 2 divisions in Province 1 (40 width) = 100 width total
        Entity r1 = world.SpawnDivision(CreateRokInfantryDivision(101, 0));
        Entity r2 = world.SpawnDivision(CreateRokInfantryDivision(102, 0));
        Entity r3 = world.SpawnDivision(CreateRokInfantryDivision(103, 0));
        Entity r4 = world.SpawnDivision(CreateRokInfantryDivision(104, 1));
        Entity r5 = world.SpawnDivision(CreateRokInfantryDivision(105, 1));

        DivisionComponent defComp = CreatePrkInfantryDivision(201, 2);
        defComp.MaxOrganization = 100f;
        defComp.Organization = 100f;
        Entity def = world.SpawnDivision(defComp);

        for (int id = 101; id <= 105; id++)
        {
            world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: id, targetProvinceId: 2));
        }

        // Tick 1: 2-direction attack should dynamically expand TotalCombatWidth from 80 to 100
        world.StepSixHours();

        Assert.True(world.TryFindActiveCombatAtProvince(2, out Entity combatEntity));
        ref ActiveCombatComponent combat = ref world.EcsWorld.Get<ActiveCombatComponent>(combatEntity);

        Assert.Equal(2, combat.AttackDirectionCount);
        Assert.Equal(100, combat.TotalCombatWidth); // 80 + 20 * (2 - 1)
        Assert.Equal(100, combat.UsedAttackerWidth);
        Assert.Equal(5, combat.EngagedAttackerCount); // All 5 divisions fit into 100 width!
    }

    [Fact]
    public void CombatSystem_EncircledDefender_ShattersWhenNoFriendlyProvince()
    {
        using SimulationWorld world = new();

        // Province 0 (ROK Paju) attacks Province 1 (PRK Kaesong).
        // Province 1 is an isolated pocket with NO friendly neighbors!
        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[1].OwnerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        Entity att = world.SpawnDivision(CreateRokMechanizedDivision(101, 0, pgm: false));

        // PRK defender starts with very low org (3.0f) so it breaks on Tick 1
        DivisionComponent prkDef = CreatePrkInfantryDivision(201, 1);
        prkDef.Organization = 3.0f;
        Entity def = world.SpawnDivision(prkDef);

        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 101, targetProvinceId: 1));

        for (int i = 0; i < 4; i++)
        {
            world.StepSixHours();
            ref DivisionComponent d = ref world.EcsWorld.Get<DivisionComponent>(def);
            if (d.State == DivisionState.Shattered)
            {
                break;
            }
        }

        ref DivisionComponent finalDef = ref world.EcsWorld.Get<DivisionComponent>(def);
        ref DivisionComponent finalAtt = ref world.EcsWorld.Get<DivisionComponent>(att);

        // Encircled unit shatters: cannot retreat into hostile Province 0!
        Assert.Equal(DivisionState.Shattered, finalDef.State);
        Assert.Equal(0.0f, finalDef.Strength);
        Assert.Equal(DivisionComponent.NoCombat, finalDef.ActiveCombatId);

        // Victorious ROK unit captured Province 1
        Assert.Equal((ushort)1, finalAtt.CurrentProvinceId);
        Assert.Equal(CountryTag.ROK, world.Provinces[1].ControllerTag);
    }

    [Fact]
    public void CombatSystem_MutualZeroOrg_DeterministicDefenderVictory()
    {
        using SimulationWorld world = new();

        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[1].OwnerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        // Both attacker and defender start with 0.5f org; both will drop below 0 on the first tick
        DivisionComponent attComp = CreateRokInfantryDivision(101, 0);
        attComp.Organization = 0.5f;
        Entity att = world.SpawnDivision(attComp);

        DivisionComponent defComp = CreatePrkInfantryDivision(201, 1);
        defComp.Organization = 0.5f;
        Entity def = world.SpawnDivision(defComp);

        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 101, targetProvinceId: 1));

        world.StepSixHours();

        ref DivisionComponent finalAtt = ref world.EcsWorld.Get<DivisionComponent>(att);

        // Attacker failed to dislodge defender: resolves as DefenderVictory (repulse)
        Assert.Equal((ushort)0, finalAtt.CurrentProvinceId);
        Assert.Equal(DivisionState.Idle, finalAtt.State);
        Assert.Equal(CountryTag.PRK, world.Provinces[1].ControllerTag);
    }

    [Fact]
    public void Simulate72HourBattle_FullKoreanPeninsulaMap_PajuToKaesongAssault()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        // On the real 120-province Korean Peninsula dataset:
        // Province 6 is Paju / Munsan (ROK)
        // Province 92 is Kaesong City (PRK, Urban terrain, connected via River + Railway)
        Assert.Equal(CountryTag.ROK, world.Provinces[6].OwnerTag);
        Assert.Equal(CountryTag.PRK, world.Provinces[92].OwnerTag);
        Assert.Equal(TerrainType.Urban, world.Provinces[92].Terrain);

        // Grant ROK PGM stockpile
        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar, 120);

        Entity rok = world.SpawnDivision(CreateRokMechanizedDivision(101, provinceId: 6, pgm: true));
        Entity prk = world.SpawnDivision(CreatePrkInfantryDivision(201, provinceId: 92));

        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 101, targetProvinceId: 92));

        // Run 12 ticks (72 hours of combat)
        for (int tick = 0; tick < 12; tick++)
        {
            world.StepSixHours();
        }

        ref DivisionComponent finalRok = ref world.EcsWorld.Get<DivisionComponent>(rok);
        ref DivisionComponent finalPrk = ref world.EcsWorld.Get<DivisionComponent>(prk);

        // Combat executed across the real map graph topology:
        // Urban terrain + River crossings properly tested against 1985 mechanized assault
        Assert.True(finalRok.Experience > 0.55f, "ROK division gained combat experience");
        Assert.True(finalPrk.Experience > 0.40f, "PRK division gained combat experience");
        Assert.True(world.TotalSixHourTicks == 12);
        Assert.True(world.CurrentDay == 4); // 1985-01-01 -> 1985-01-04 (72 hours elapsed)
    }

    [Fact]
    public void CombatSystem_72HourBattle_ZeroHeapAllocations_StrictEnforcement()
    {
        // Warm up JIT with a warmup world
        using (SimulationWorld warmupWorld = new())
        {
            warmupWorld.Provinces[0].OwnerTag = CountryTag.ROK;
            warmupWorld.Provinces[0].ControllerTag = CountryTag.ROK;
            warmupWorld.Provinces[1].OwnerTag = CountryTag.PRK;
            warmupWorld.Provinces[1].ControllerTag = CountryTag.PRK;
            warmupWorld.Provinces[2].OwnerTag = CountryTag.PRK;
            warmupWorld.Provinces[2].ControllerTag = CountryTag.PRK;

            ReadOnlySpan<DirectedEdge> warmupEdges =
            [
                new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
                new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard),
                new DirectedEdge(1, 2, AdjacencyEdgeFlags.Standard),
                new DirectedEdge(2, 1, AdjacencyEdgeFlags.Standard)
            ];
            warmupWorld.Graph.BuildCsr(warmupWorld.Provinces, warmupEdges);

            warmupWorld.SpawnDivision(CreateRokMechanizedDivision(1, 0, false));
            warmupWorld.SpawnDivision(CreatePrkInfantryDivision(2, 1));
            warmupWorld.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 1, targetProvinceId: 1));

            for (int i = 0; i < 12; i++)
            {
                warmupWorld.StepSixHours();
            }
        }

        // Test world for strict allocation measurement
        using SimulationWorld world = new();
        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[1].OwnerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;
        world.Provinces[2].OwnerTag = CountryTag.PRK;
        world.Provinces[2].ControllerTag = CountryTag.PRK;

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 2, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(2, 1, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar, 100);

        world.SpawnDivision(CreateRokMechanizedDivision(101, 0, true));
        DivisionComponent defComp = CreatePrkInfantryDivision(201, 1);
        defComp.MaxOrganization = 200f; // Ensure ongoing battle throughout all 12 ticks
        defComp.Organization = 200f;
        world.SpawnDivision(defComp);

        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 101, targetProvinceId: 1));

        // Tick 0 spawns the initial ActiveCombatComponent entity
        world.StepSixHours();

        // Measure steady-state combat ticks 1 to 12
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

        for (int tick = 0; tick < 11; tick++)
        {
            world.StepSixHours();
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        long diff = afterAlloc - beforeAlloc;

        Assert.Equal(0, diff);
    }

    [Fact]
    public void CombatSystem_HighThroughputBenchmark_1000BattlesUnder100Ms()
    {
        // Warmup
        using (SimulationWorld warmupWorld = new())
        {
            warmupWorld.Provinces[0].OwnerTag = CountryTag.ROK;
            warmupWorld.Provinces[0].ControllerTag = CountryTag.ROK;
            warmupWorld.Provinces[1].OwnerTag = CountryTag.PRK;
            warmupWorld.Provinces[1].ControllerTag = CountryTag.PRK;

            ReadOnlySpan<DirectedEdge> warmupEdges =
            [
                new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
                new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard)
            ];
            warmupWorld.Graph.BuildCsr(warmupWorld.Provinces, warmupEdges);

            warmupWorld.SpawnDivision(CreateRokMechanizedDivision(1, 0, false));
            warmupWorld.SpawnDivision(CreatePrkInfantryDivision(2, 1));
            warmupWorld.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 1, targetProvinceId: 1));
            for (int i = 0; i < 32; i++)
            {
                warmupWorld.StepSixHours();
            }
        }

        using SimulationWorld world = new();
        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[1].OwnerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        world.SpawnDivision(CreateRokMechanizedDivision(101, 0, false));
        DivisionComponent defComp = CreatePrkInfantryDivision(201, 1);
        defComp.MaxOrganization = 100_000f; // Infinite org so combat never concludes
        defComp.Organization = 100_000f;
        world.SpawnDivision(defComp);

        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 101, targetProvinceId: 1));
        world.StepSixHours(); // Initiate combat

        const int iterations = 1_000;
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            world.StepSixHours();
        }

        sw.Stop();
        double elapsedMs = sw.Elapsed.TotalMilliseconds;

        // 1,000 full simulation ticks with active combat must complete under 100ms (>10,000 ticks/sec)
        Assert.True(elapsedMs < 100.0, $"1,000 combat ticks completed in {elapsedMs:F2} ms (budget: < 100 ms)");
    }

    [Fact]
    public void CombatSystem_CombatWidthReserveFilling_SkipsLargeExceedingDivisionAndCommitsSmallerDivision()
    {
        using SimulationWorld world = new();

        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[1].OwnerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        // Attacker 1: Width 60 (Fits into 80 max width -> remaining 20)
        DivisionComponent att1Comp = CreateRokInfantryDivision(101, 0);
        att1Comp.CombatWidth = 60;
        Entity att1 = world.SpawnDivision(att1Comp);

        // Attacker 2: Width 40 (60 + 40 = 100 > 80 -> exceeds remaining width, must be skipped to reserve)
        DivisionComponent att2Comp = CreateRokInfantryDivision(102, 0);
        att2Comp.CombatWidth = 40;
        Entity att2 = world.SpawnDivision(att2Comp);

        // Attacker 3: Width 20 (60 + 20 = 80 <= 80 -> fits into remaining width, must be committed!)
        DivisionComponent att3Comp = CreateRokInfantryDivision(103, 0);
        att3Comp.CombatWidth = 20;
        Entity att3 = world.SpawnDivision(att3Comp);

        // Defender: High-org unit so combat continues
        DivisionComponent defComp = CreatePrkInfantryDivision(201, 1);
        defComp.MaxOrganization = 100f;
        defComp.Organization = 100f;
        Entity def = world.SpawnDivision(defComp);

        for (int id = 101; id <= 103; id++)
        {
            world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: id, targetProvinceId: 1));
        }

        // Tick 1: Combat engages
        world.StepSixHours();

        Assert.True(world.TryFindActiveCombatAtProvince(1, out Entity combatEntity));
        ref readonly ActiveCombatComponent combat = ref world.EcsWorld.Get<ActiveCombatComponent>(combatEntity);

        Assert.Equal(80, combat.TotalCombatWidth);
        Assert.Equal(80, combat.UsedAttackerWidth);
        Assert.Equal(2, combat.EngagedAttackerCount);

        // Verify Attacker 2 (width 40) remained in reserve with pristine stats
        ref readonly DivisionComponent div2 = ref world.EcsWorld.Get<DivisionComponent>(att2);
        Assert.Equal(55.0f, div2.Organization);
        Assert.Equal(1.0f, div2.Strength);

        // Verify Attacker 1 (width 60) and Attacker 3 (width 20) were engaged
        ref readonly DivisionComponent div1 = ref world.EcsWorld.Get<DivisionComponent>(att1);
        ref readonly DivisionComponent div3 = ref world.EcsWorld.Get<DivisionComponent>(att3);
        Assert.NotEqual(DivisionComponent.NoCombat, div1.ActiveCombatId);
        Assert.NotEqual(DivisionComponent.NoCombat, div3.ActiveCombatId);
    }

    [Fact]
    public void CombatSystem_MultiHopWaypointContinuation_ContinuesPastSixteenHopsUntilDestination()
    {
        using SimulationWorld world = new();
        const int totalProvinces = 25;
        DirectedEdge[] edges = new DirectedEdge[(totalProvinces - 1) * 2];
        int edgeIdx = 0;

        for (ushort i = 0; i < totalProvinces; i++)
        {
            world.Provinces[i].OwnerTag = CountryTag.ROK;
            world.Provinces[i].ControllerTag = CountryTag.ROK;
            world.Provinces[i].Terrain = TerrainType.Plains;
            world.Provinces[i].InfrastructureLevel = 5;
            world.Provinces[i].SupplyRatio = 1.0f;
            world.Provinces[i].CenterX = (short)(i * 10);
            world.Provinces[i].CenterY = 0;

            if (i < totalProvinces - 1)
            {
                edges[edgeIdx++] = new DirectedEdge(i, (ushort)(i + 1), AdjacencyEdgeFlags.Standard);
                edges[edgeIdx++] = new DirectedEdge((ushort)(i + 1), i, AdjacencyEdgeFlags.Standard);
            }
        }
        world.Graph.BuildCsr(world.Provinces, edges);

        DivisionComponent divComp = DivisionComponent.Create(
            divisionId: 301,
            countryTag: CountryTag.ROK,
            startingProvinceId: 0,
            combatWidth: 20,
            speed: 25.0f, // Fast speed so division traverses 1 hop per tick
            maxOrganization: 50.0f,
            softAttack: 20.0f,
            hardAttack: 5.0f,
            defense: 25.0f,
            breakthrough: 10.0f,
            armor: 5.0f,
            piercing: 10.0f,
            hardness: 0.1f);
        Entity divEntity = world.SpawnDivision(divComp);

        // Issue move order targeting province 20 (> 16 hops)
        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 301, targetProvinceId: 20));

        // Tick 1: Drains command queue, creates first 16 waypoints
        world.StepSixHours();

        ref DivisionComponent initialDiv = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
        Assert.Equal(DivisionState.Moving, initialDiv.State);
        Assert.Equal(20, initialDiv.DestinationProvinceId);
        Assert.Equal(16, initialDiv.WaypointCount);

        // Step simulation enough ticks (e.g. 35 ticks) for full traversal of 20 hops with re-pathing
        for (int step = 0; step < 35; step++)
        {
            world.StepSixHours();
            ref readonly DivisionComponent current = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
            if (current.CurrentProvinceId == 20 && current.State == DivisionState.Idle)
            {
                break;
            }
        }

        ref readonly DivisionComponent finalDiv = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
        Assert.Equal((ushort)20, finalDiv.CurrentProvinceId);
        Assert.Equal(DivisionState.Idle, finalDiv.State);
        Assert.Equal(0, finalDiv.WaypointCount);
        Assert.Equal(0.0f, finalDiv.MovementProgress);
    }

    [Fact]
    public void CombatSystem_RetreatStateConsistency_ResetsProgressAndWaypointsCleanly()
    {
        using SimulationWorld world = new();

        // Province 0 (Attacker ROK) -> Province 1 (Contested PRK) -> Province 2 (Friendly PRK retreat rear)
        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[1].OwnerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;
        world.Provinces[2].OwnerTag = CountryTag.PRK;
        world.Provinces[2].ControllerTag = CountryTag.PRK;

        ReadOnlySpan<DirectedEdge> edges =
        [
            new DirectedEdge(0, 1, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 0, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(1, 2, AdjacencyEdgeFlags.Standard),
            new DirectedEdge(2, 1, AdjacencyEdgeFlags.Standard)
        ];
        world.Graph.BuildCsr(world.Provinces, edges);

        Entity att = world.SpawnDivision(CreateRokMechanizedDivision(101, 0, pgm: false));

        // PRK defender starts with very low org (2.0f) so it breaks immediately on the first tick of combat
        DivisionComponent prkDef = CreatePrkInfantryDivision(201, 1);
        prkDef.Organization = 2.0f;
        Entity def = world.SpawnDivision(prkDef);

        world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 101, targetProvinceId: 1));

        // Step ticks to initiate combat and force defender retreat
        for (int i = 0; i < 4; i++)
        {
            world.StepSixHours();
            ref readonly DivisionComponent d = ref world.EcsWorld.Get<DivisionComponent>(def);
            if (d.CurrentProvinceId == 2)
            {
                break;
            }
        }

        ref readonly DivisionComponent finalDef = ref world.EcsWorld.Get<DivisionComponent>(def);

        // Verify clean retreat state consistency (no dirty progress or intermediate states)
        Assert.Equal((ushort)2, finalDef.CurrentProvinceId);
        Assert.Equal(DivisionComponent.NoProvince, finalDef.TargetProvinceId);
        Assert.Equal(DivisionComponent.NoProvince, finalDef.RetreatProvinceId);
        Assert.Equal(0.0f, finalDef.MovementProgress);
        Assert.Equal(DivisionState.Idle, finalDef.State);
        Assert.Equal(0, finalDef.WaypointCount);
        Assert.Equal(DivisionComponent.NoCombat, finalDef.ActiveCombatId);
        Assert.True(finalDef.Strength > 0.0f);
    }

    #endregion
}
