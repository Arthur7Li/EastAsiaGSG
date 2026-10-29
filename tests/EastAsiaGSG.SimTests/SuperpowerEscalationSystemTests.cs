using System.Diagnostics;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;
using EastAsiaGSG.Data.Loaders;

namespace EastAsiaGSG.SimTests;

/// <summary>
/// Headless xUnit test suite validating Phase 4, Task 4.3:
/// SuperpowerEscalationSystem threat assessment, US PACOM HomelandEscalation dynamics (15..100),
/// DEFCON thresholds (5..1), CONUS off-map military factories scaling (10..200),
/// homeland stockpile convoys, Carrier Strike Group (CSG) presence, and zero-allocation invariants.
/// </summary>
public sealed class SuperpowerEscalationSystemTests
{
    private static string FindRepositoryRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "EastAsiaGSG.sln")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
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

    [Fact]
    public void SuperpowerEscalationSystem_ZeroHeapAllocations_InHotDailyLoop()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        // Warm up JIT
        SuperpowerEscalationSystem.Execute(world);
        SuperpowerEscalationSystem.Execute(world);

        long startBytes = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 50; i++)
        {
            SuperpowerEscalationSystem.Execute(world);
        }

        long endBytes = GC.GetAllocatedBytesForCurrentThread();
        long allocated = endBytes - startBytes;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SuperpowerEscalationSystem_HighThroughputBenchmark_UnderOneMillisecond()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        // Warm up
        for (int i = 0; i < 10; i++)
        {
            SuperpowerEscalationSystem.Execute(world);
        }

        const int iterations = 1000;
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            SuperpowerEscalationSystem.Execute(world);
        }

        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 0.05, $"SuperpowerEscalationSystem average tick was {avgMs:F4} ms, exceeding 0.05 ms threshold!");
    }

    [Fact]
    public void DmzIncursion_RaisesHomelandEscalation_AndShiftsDefconToHeightenedAlert()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        ref CountryState usa = ref world.Countries[CountryTag.USA];
        Assert.Equal((byte)15, usa.HomelandEscalation);
        Assert.Equal((byte)5, usa.Defcon);

        // Hostile forces (PRK) cross DMZ and occupy 3 provinces in Seoul Metropolitan Area (State 0)
        world.Provinces[0].ControllerTag = CountryTag.PRK;
        world.Provinces[1].ControllerTag = CountryTag.PRK;
        world.Provinces[2].ControllerTag = CountryTag.PRK;

        // 3 Seoul provinces occupied = 3 * 5.0 = +15.0 threat
        SuperpowerEscalationSystem.Execute(world);

        // 15 baseline + 15 threat = 30 escalation -> crosses Defcon 4 threshold (25)
        Assert.Equal(30, usa.HomelandEscalation);
        Assert.Equal(4, usa.Defcon);

        // Advance 3 more days of ongoing occupation
        for (int day = 0; day < 3; day++)
        {
            SuperpowerEscalationSystem.Execute(world);
        }

        // 30 + (3 * 15) = 75 escalation -> crosses Defcon 2 threshold (70)
        Assert.Equal(75, usa.HomelandEscalation);
        Assert.Equal(2, usa.Defcon);
    }

    [Fact]
    public void EscalationThresholds_ScaleOffMapMilitaryFactories_AndMobilizationModifiers()
    {
        using SimulationWorld world = new();
        ref CountryState usa = ref world.Countries[CountryTag.USA];

        // 1. Peacetime Baseline (Escalation 15)
        usa.HomelandEscalation = 15;
        SuperpowerEscalationSystem.Execute(world);

        Assert.Equal(5, usa.Defcon);
        Assert.Equal(10, usa.OffMapMilitaryFactories);
        Assert.Equal(0.35f, usa.ConsumerGoodsRatio, precision: 2);
        Assert.Equal(0.50f, usa.WarSupport, precision: 2);
        Assert.Equal(0.00f, usa.FactoryOutputModifier, precision: 2);

        // 2. High Crisis (Escalation 70 -> DEFCON 2)
        usa.HomelandEscalation = 70;
        usa.Defcon = 2; // Sync with escalation
        SuperpowerEscalationSystem.Execute(world);

        Assert.Equal(2, usa.Defcon);
        Assert.Equal(133, usa.OffMapMilitaryFactories);
        Assert.True(usa.ConsumerGoodsRatio < 0.25f);
        Assert.True(usa.WarSupport > 0.75f);
        Assert.True(usa.FactoryOutputModifier > 0.15f);

        // 3. Maximum Mobilization (Escalation 100 -> DEFCON 1)
        usa.HomelandEscalation = 100;
        usa.Defcon = 1;
        SuperpowerEscalationSystem.Execute(world);

        Assert.Equal(1, usa.Defcon);
        Assert.Equal(200, usa.OffMapMilitaryFactories);
        Assert.Equal(0.15f, usa.ConsumerGoodsRatio, precision: 2);
        Assert.Equal(0.95f, usa.WarSupport, precision: 2);
        Assert.Equal(0.25f, usa.FactoryOutputModifier, precision: 2);
    }

    [Fact]
    public void PeacetimeDecay_ReturnsEscalationToBaseline15_WhenNoHostilitiesExist()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();
        ref CountryState usa = ref world.Countries[CountryTag.USA];

        // Post-crisis elevated escalation without active threats
        usa.HomelandEscalation = 20;

        SuperpowerEscalationSystem.Execute(world);

        // Decays by 0.25 per day towards baseline 15: round(20 - 0.25) = 20
        // Run 4 days: 20 - (4 * 0.25) = 19
        for (int i = 0; i < 3; i++)
        {
            SuperpowerEscalationSystem.Execute(world);
        }

        Assert.Equal(19, usa.HomelandEscalation);

        // Run 16 more days (total 20 days -> -5.0)
        for (int i = 0; i < 16; i++)
        {
            SuperpowerEscalationSystem.Execute(world);
        }

        Assert.Equal(15, usa.HomelandEscalation);

        // Does not decay below baseline 15
        for (int i = 0; i < 10; i++)
        {
            SuperpowerEscalationSystem.Execute(world);
        }
        Assert.Equal(15, usa.HomelandEscalation);
    }

    [Fact]
    public void HomelandEquipmentConvoys_ShipMaterielToPacomStockpile_AtDefcon3OrBelow()
    {
        using SimulationWorld world = new();
        ref CountryState usa = ref world.Countries[CountryTag.USA];

        // 1. At DEFCON 5: zero homeland convoy shipments
        usa.HomelandEscalation = 15;
        SuperpowerEscalationSystem.Execute(world);

        Assert.Equal(0, usa.GetTotalArchetypeStockpile(EquipmentType.TacticalAir));
        Assert.Equal(0, usa.GetTotalArchetypeStockpile(EquipmentType.MBT));

        // 2. Shift to DEFCON 3 (Roundhouse readiness)
        usa.HomelandEscalation = 50;
        SuperpowerEscalationSystem.Execute(world);

        Assert.Equal(3, usa.Defcon);
        // Receives DEFCON 3 package: +2 TacticalAir, +5 MBT, +10 IFV, +15 InfantryKit, +5 PGM
        Assert.Equal(2, usa.GetTotalArchetypeStockpile(EquipmentType.TacticalAir));
        Assert.Equal(5, usa.GetTotalArchetypeStockpile(EquipmentType.MBT));
        Assert.Equal(10, usa.GetTotalArchetypeStockpile(EquipmentType.IFV));
        Assert.Equal(15, usa.GetTotalArchetypeStockpile(EquipmentType.InfantryKit));
        Assert.Equal(5, usa.GetTotalArchetypeStockpile(EquipmentType.PGM));

        // 3. Shift to DEFCON 1 (Cocked Pistol readiness)
        usa.HomelandEscalation = 95;
        SuperpowerEscalationSystem.Execute(world);

        Assert.Equal(1, usa.Defcon);
        // Receives DEFCON 1 package: +10 TacticalAir, +25 MBT, +50 IFV, +75 InfantryKit, +30 PGM
        Assert.Equal(12, usa.GetTotalArchetypeStockpile(EquipmentType.TacticalAir));
        Assert.Equal(30, usa.GetTotalArchetypeStockpile(EquipmentType.MBT));
    }

    [Fact]
    public void CarrierStrikeGroups_DeployToSeaZone_WhenEscalationCrossesThresholds()
    {
        using SimulationWorld world = new();

        const byte seaRegionId = 1;
        world.Regions[seaRegionId].RegionId = seaRegionId;
        world.Regions[seaRegionId].IsSeaZone = 1;

        world.Countries[CountryTag.USA].Faction = FactionId.BlueforPacom;
        world.Countries[CountryTag.SOV].Faction = FactionId.RedforSocialist;

        // 1. Peacetime: 0 CSGs
        world.Countries[CountryTag.USA].HomelandEscalation = 15;
        AirAndNavalRegionSystem.Execute(world);

        Assert.Equal(0.0f, world.Regions[seaRegionId].GetNavalControl(FactionId.BlueforPacom));

        // 2. Heightened Escalation (Escalation 65 -> 2 CSGs)
        // Redfor has small coastal patrol (60 naval power)
        world.States[0].StateId = 0;
        world.States[0].ControllerTag = CountryTag.SOV;
        world.Provinces[0].ProvinceId = 0;
        world.Provinces[0].StateId = 0;
        world.Provinces[0].ControllerTag = CountryTag.SOV;
        world.Provinces[0].StrategicRegionId = seaRegionId;
        world.Provinces[0].PortLevel = 5; // 5 * 12 = 60 naval power

        world.Countries[CountryTag.USA].HomelandEscalation = 65;
        AirAndNavalRegionSystem.Execute(world);

        // 2 CSGs provide 2 * 150 = 300 naval power and 2 * 80 = 160 air power
        // Bluefor air superiority is 100% -> air support factor = 1.0 -> 300 naval power
        // Total naval power = 300 (Bluefor) + 60 (Redfor) = 360 -> Bluefor = 300 / 360 = 83.3%
        float blueNaval = world.Regions[seaRegionId].GetNavalControl(FactionId.BlueforPacom);
        Assert.True(blueNaval > 0.80f, $"Expected Bluefor naval control > 80%, but got {blueNaval:F2}");
        Assert.True(world.Regions[seaRegionId].CanCrossStraitOrInvade(FactionId.BlueforPacom));
    }

    [Fact]
    public void ManualShiftDefconCommand_SynchronizesEscalationFloor()
    {
        using SimulationWorld world = CreateLoadedKoreanPeninsulaWorld();

        ref CountryState usa = ref world.Countries[CountryTag.USA];
        Assert.Equal(5, usa.Defcon);
        Assert.Equal((byte)15, usa.HomelandEscalation);

        // Queue manual player command to shift DEFCON to 2 (Fast Pace)
        world.EnqueueCommand(SimulationCommand.CreateShiftDefcon(CountryTag.USA, 2));

        // Advance 6 hours to drain command and advance day
        world.StepSixHours(); // 00:00 -> 06:00 (drains command, sets defcon=2)
        world.StepSixHours(); // 06:00 -> 12:00
        world.StepSixHours(); // 12:00 -> 18:00
        world.StepSixHours(); // 18:00 -> 1985-01-02 00:00 (fires daily tier)

        // SuperpowerEscalationSystem synchronizes escalation to DEFCON 2 floor (70)
        Assert.Equal(2, usa.Defcon);
        Assert.True(usa.HomelandEscalation >= 70, $"Expected escalation >= 70, but got {usa.HomelandEscalation}");
        Assert.True(usa.OffMapMilitaryFactories >= 133);
    }
}
