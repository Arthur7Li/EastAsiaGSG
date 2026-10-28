using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Core.Systems;

namespace EastAsiaGSG.SimTests;

/// <summary>
/// Headless xUnit test suite validating Phase 3, Task 3.2:
/// DivisionTemplateRecord layout, ReinforcementSystem equipment/manpower draw,
/// generation prioritization (Gen 3 -> Gen 0), experience dilution, supply/combat gates,
/// end-to-end Production -> Stockpile -> Division reinforcement loop, and zero-allocation invariants.
/// </summary>
public sealed class ReinforcementSystemTests
{
    [Fact]
    public unsafe void DivisionTemplateRecord_MaintainsExact24ByteBlittableLayout_AndMatchesOobSchemaBattalions()
    {
        Assert.Equal(24, sizeof(DivisionTemplateRecord));
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<DivisionTemplateRecord>());

        // Build ROKA Mechanized Infantry Division from DATA_SCHEMAS.md Section 3:
        // "battalions": ["IFV", "IFV", "IFV", "MBT", "Artillery", "InfantryKit"]
        // "supportCompanies": ["SupportVehicles", "Artillery"]
        ReadOnlySpan<EquipmentType> battalions =
        [
            EquipmentType.IFV,
            EquipmentType.IFV,
            EquipmentType.IFV,
            EquipmentType.MBT,
            EquipmentType.Artillery,
            EquipmentType.InfantryKit
        ];
        ReadOnlySpan<EquipmentType> support =
        [
            EquipmentType.SupportVehicles,
            EquipmentType.Artillery
        ];

        DivisionTemplateRecord tpl = DivisionTemplateRecord.FromBattalions(
            templateId: 1,
            combatWidth: 20,
            battalions: battalions,
            supportCompanies: support);

        Assert.Equal((ushort)1, tpl.TemplateId);
        Assert.Equal(20, tpl.CombatWidth);
        Assert.Equal(1, tpl.IsConfigured);

        // 3x IFV (150 IFV + 150 InfantryKit) + 1x InfantryKit (100 InfantryKit) = 250 InfantryKit, 150 IFV
        // 1x MBT = 40 MBT
        // 1x Artillery battalion (24) + 1x Artillery support (12) = 36 Artillery
        // 1x SupportVehicles support = 40 SupportVehicles
        Assert.Equal((ushort)250, tpl.GetRequired(EquipmentType.InfantryKit));
        Assert.Equal((ushort)40, tpl.GetRequired(EquipmentType.SupportVehicles));
        Assert.Equal((ushort)150, tpl.GetRequired(EquipmentType.IFV));
        Assert.Equal((ushort)40, tpl.GetRequired(EquipmentType.MBT));
        Assert.Equal((ushort)36, tpl.GetRequired(EquipmentType.Artillery));
        Assert.Equal((ushort)0, tpl.GetRequired(EquipmentType.AttackHeli));
        Assert.Equal(516, tpl.GetTotalEquipmentUnits());
        Assert.True(tpl.TotalManpower > 0);
    }

    [Fact]
    public void ReinforcementSystem_FullSupplyStrengthRecovery_DrawsEquipmentNewestGenFirst_AndDilutesExperience()
    {
        using SimulationWorld world = new();
        world.RegisterDivisionTemplate(DivisionTemplateRecord.CreateMechanizedDivision(templateId: 1));

        world.Provinces[10].OwnerTag = CountryTag.ROK;
        world.Provinces[10].ControllerTag = CountryTag.ROK;
        world.Provinces[10].SupplyRatio = 1.0f;

        ref CountryState rok = ref world.Countries[CountryTag.ROK];
        rok.Manpower = 50_000;

        // Populate ROK stockpile across multiple generations (Gen 2 + Gen 1) to verify newest-first (Gen 3 -> Gen 0) consumption:
        // For a +0.10f daily strength step on Mechanized Division (Total = 250 Inf, 40 Sup, 150 IFV, 40 MBT, 36 Art, 10,000 Manpower):
        // Daily 10% step needs: 25 InfantryKit, 4 SupportVehicles, 15 IFV, 4 MBT, 4 Artillery (round(3.6) = 4), and 1,000 Manpower.
        rok.SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen2_1988LateColdWar, 15);
        rok.SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline, 50);
        rok.SetStockpile(EquipmentType.SupportVehicles, TechGeneration.Gen1_1980Baseline, 20);
        rok.SetStockpile(EquipmentType.IFV, TechGeneration.Gen2_1988LateColdWar, 10);
        rok.SetStockpile(EquipmentType.IFV, TechGeneration.Gen1_1980Baseline, 30);
        rok.SetStockpile(EquipmentType.MBT, TechGeneration.Gen2_1988LateColdWar, 20);
        rok.SetStockpile(EquipmentType.Artillery, TechGeneration.Gen1_1980Baseline, 20);

        // Spawn a depleted veteran ROK Mechanized Division (Strength = 0.60f, Experience = 0.80f, Org = 40.0f / 60.0f)
        DivisionComponent divComp = DivisionComponent.Create(
            divisionId: 101,
            countryTag: CountryTag.ROK,
            startingProvinceId: 10,
            templateId: 1,
            hardness: 0.55f,
            maxOrganization: 60.0f,
            startingStrength: 0.60f,
            startingExperience: 0.80f);
        divComp.Organization = 40.0f;
        Entity divEntity = world.SpawnDivision(divComp);

        // Execute 1 daily reinforcement tick
        ReinforcementSystem.Execute(world);

        ref DivisionComponent reinforced = ref world.EcsWorld.Get<DivisionComponent>(divEntity);

        // 1. Strength increased by exact +0.10f (0.60 -> 0.70) and Organization recovered by +4.0f (40.0 -> 44.0)
        Assert.Equal(0.70f, reinforced.Strength, precision: 4);
        Assert.Equal(44.0f, reinforced.Organization, precision: 4);

        // 2. Experience diluted proportionally by green replacements (0.20f):
        // (0.80 * 0.60 + 0.20 * 0.10) / 0.70 = (0.48 + 0.02) / 0.70 = 0.50 / 0.70 = 0.7142857f
        Assert.Equal(0.50f / 0.70f, reinforced.Experience, precision: 4);

        // 3. Newest generation (Gen 2) consumed first before falling back to Gen 1:
        // InfantryKit needed 25: all 15 Gen 2 consumed (0 left) + 10 Gen 1 consumed (40 left)
        Assert.Equal(0, rok.GetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen2_1988LateColdWar));
        Assert.Equal(40, rok.GetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline));

        // IFV needed 15: all 10 Gen 2 consumed (0 left) + 5 Gen 1 consumed (25 left)
        Assert.Equal(0, rok.GetStockpile(EquipmentType.IFV, TechGeneration.Gen2_1988LateColdWar));
        Assert.Equal(25, rok.GetStockpile(EquipmentType.IFV, TechGeneration.Gen1_1980Baseline));

        // SupportVehicles needed 4 (16 left), MBT needed 4 (16 left), Artillery needed 4 (16 left)
        Assert.Equal(16, rok.GetStockpile(EquipmentType.SupportVehicles, TechGeneration.Gen1_1980Baseline));
        Assert.Equal(16, rok.GetStockpile(EquipmentType.MBT, TechGeneration.Gen2_1988LateColdWar));
        Assert.Equal(16, rok.GetStockpile(EquipmentType.Artillery, TechGeneration.Gen1_1980Baseline));

        // 4. Manpower consumed: 50,000 - 1,000 = 49,000
        Assert.Equal(49_000, rok.Manpower);
    }

    [Fact]
    public void ReinforcementSystem_PartialStockpile_ManpowerExhaustion_AndSupplyCutOffGates()
    {
        using SimulationWorld world = new();
        world.RegisterDivisionTemplate(DivisionTemplateRecord.CreateInfantryDivision(templateId: 2));

        world.Provinces[0].OwnerTag = CountryTag.PRK;
        world.Provinces[0].ControllerTag = CountryTag.PRK;
        world.Provinces[0].SupplyRatio = 1.0f;

        Entity divEntity = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 201,
            countryTag: CountryTag.PRK,
            startingProvinceId: 0,
            templateId: 2,
            hardness: 0.15f,
            startingStrength: 0.50f));

        ref CountryState prk = ref world.Countries[CountryTag.PRK];
        prk.Manpower = 20_000;

        // Infantry Division full TOE = 500 InfantryKit, 30 SupportVehicles, 36 Artillery (total 566).
        // A 10% daily step requires: 50 InfantryKit + 3 SupportVehicles + 4 Artillery = 57 total units.
        // Provide only 25 InfantryKit, 2 SupportVehicles, 1.5 -> 2 Artillery = 29 units (~50.88% of needed equipment).
        prk.SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline, 25);
        prk.SetStockpile(EquipmentType.SupportVehicles, TechGeneration.Gen1_1980Baseline, 2);
        prk.SetStockpile(EquipmentType.Artillery, TechGeneration.Gen1_1980Baseline, 2);

        ReinforcementSystem.Execute(world);

        ref DivisionComponent div = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
        float expectedPartialDelta = 0.10f * (29.0f / 57.0f);
        Assert.Equal(0.50f + expectedPartialDelta, div.Strength, precision: 4);
        Assert.Equal(0, prk.GetTotalArchetypeStockpile(EquipmentType.InfantryKit));

        // Gate 1: Refill equipment stockpile, but set Manpower = 0 -> zero strength reinforcement & zero equipment consumed
        prk.SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline, 500);
        prk.SetStockpile(EquipmentType.SupportVehicles, TechGeneration.Gen1_1980Baseline, 50);
        prk.SetStockpile(EquipmentType.Artillery, TechGeneration.Gen1_1980Baseline, 50);
        prk.Manpower = 0;

        float strengthBeforeZeroMp = div.Strength;
        ReinforcementSystem.Execute(world);
        Assert.Equal(strengthBeforeZeroMp, div.Strength, precision: 5);
        Assert.Equal(500, prk.GetTotalArchetypeStockpile(EquipmentType.InfantryKit));

        // Gate 2: Restore Manpower, but cut province supply below MinReinforcementSupplyRatio (0.05 < 0.15) -> zero reinforcement
        prk.Manpower = 20_000;
        world.Provinces[0].SupplyRatio = 0.05f;
        ReinforcementSystem.Execute(world);
        Assert.Equal(strengthBeforeZeroMp, div.Strength, precision: 5);

        // Gate 3: Restore supply, but division is actively engaged in combat (DivisionState.Attacking) -> zero reinforcement
        world.Provinces[0].SupplyRatio = 1.0f;
        div.State = DivisionState.Attacking;
        ReinforcementSystem.Execute(world);
        Assert.Equal(strengthBeforeZeroMp, div.Strength, precision: 5);
    }

    [Fact]
    public void EndToEndLoop_ProductionLines_FeedNationalStockpile_ToReinforceDepletedFieldDivisionAndResupplyPgm()
    {
        using SimulationWorld world = new();
        world.RegisterDivisionTemplate(DivisionTemplateRecord.CreateInfantryDivision(templateId: 2));

        // Configure ROK State 1 with 15 Military Factories and full supply in Province 1
        world.States[1].ControllerTag = CountryTag.ROK;
        world.States[1].MilitaryFactories = 15;

        world.Provinces[1].StateId = 1;
        world.Provinces[1].OwnerTag = CountryTag.ROK;
        world.Provinces[1].ControllerTag = CountryTag.ROK;
        world.Provinces[1].SupplyRatio = 1.0f;

        // Start ROK with 0 equipment in stockpile, and spawn Production Lines for InfantryKit, SupportVehicles, Artillery, and PGM
        world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 1,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.InfantryKit,
            assignedMilitaryFactories: 6,
            initialEfficiency: 0.70f,
            efficiencyCap: 0.85f));

        world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 2,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.SupportVehicles,
            assignedMilitaryFactories: 3,
            initialEfficiency: 0.70f,
            efficiencyCap: 0.85f));

        world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 3,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.Artillery,
            assignedMilitaryFactories: 3,
            initialEfficiency: 0.70f,
            efficiencyCap: 0.85f));

        world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 4,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.PGM,
            generation: TechGeneration.Gen2_1988LateColdWar,
            assignedMilitaryFactories: 3,
            initialEfficiency: 0.70f,
            efficiencyCap: 0.85f));

        // Spawn a heavily depleted ROK Infantry Division at 50% Strength (0.50f) and 20 Organization
        DivisionComponent depletedComp = DivisionComponent.Create(
            divisionId: 101,
            countryTag: CountryTag.ROK,
            startingProvinceId: 1,
            templateId: 2,
            hardness: 0.15f,
            maxOrganization: 60.0f,
            startingStrength: 0.50f,
            pgmAuthorized: true);
        depletedComp.Organization = 20.0f;
        Entity divEntity = world.SpawnDivision(depletedComp);

        // Advance 20 days (80 six-hour ticks):
        // Each day at 00:00, ProductionSystem manufactures equipment into ROK stockpile,
        // and ReinforcementSystem immediately transfers it to Division 101 until Strength reaches 1.00f!
        for (int tick = 0; tick < 20 * 4; tick++)
        {
            world.StepSixHours();
        }

        ref DivisionComponent recoveredDiv = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
        Assert.Equal(1.0f, recoveredDiv.Strength, precision: 3);
        Assert.Equal(60.0f, recoveredDiv.Organization, precision: 3);

        // And PGM production line accumulated a surplus in national stockpile ready for combat strikes
        int pgmStockpile = world.Countries[CountryTag.ROK].GetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar);
        Assert.True(pgmStockpile >= 40, $"Expected PGM stockpile >= 40 after 20 days of production, was {pgmStockpile}");
    }

    [Fact]
    public void ReinforcementSystem_ZeroHeapAllocations_InHotDailyLoop()
    {
        using SimulationWorld world = new();
        world.RegisterDivisionTemplate(DivisionTemplateRecord.CreateMechanizedDivision(templateId: 1));

        world.Provinces[0].OwnerTag = CountryTag.ROK;
        world.Provinces[0].ControllerTag = CountryTag.ROK;
        world.Provinces[0].SupplyRatio = 1.0f;

        ref CountryState rok = ref world.Countries[CountryTag.ROK];
        rok.Manpower = 1_000_000;
        rok.SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline, 100_000);
        rok.SetStockpile(EquipmentType.SupportVehicles, TechGeneration.Gen1_1980Baseline, 100_000);
        rok.SetStockpile(EquipmentType.IFV, TechGeneration.Gen1_1980Baseline, 100_000);
        rok.SetStockpile(EquipmentType.MBT, TechGeneration.Gen1_1980Baseline, 100_000);
        rok.SetStockpile(EquipmentType.Artillery, TechGeneration.Gen1_1980Baseline, 100_000);

        Entity divEntity = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 101,
            countryTag: CountryTag.ROK,
            startingProvinceId: 0,
            templateId: 1,
            startingStrength: 0.20f));

        // Warm up JIT
        ReinforcementSystem.Execute(world);

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            ref DivisionComponent div = ref world.EcsWorld.Get<DivisionComponent>(divEntity);
            div.Strength = 0.50f;
            div.Organization = 30.0f;
            ReinforcementSystem.Execute(world);
        }
        long afterBytes = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0L, afterBytes - beforeBytes);
    }
}
