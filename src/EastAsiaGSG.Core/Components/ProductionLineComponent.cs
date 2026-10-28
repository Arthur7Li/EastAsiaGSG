using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Components;

/// <summary>
/// Unmanaged 8-byte strategic resource requirement per active military factory on a production line.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly record struct EquipmentResourceCost(
    ushort Oil,
    ushort Steel,
    ushort Aluminum,
    ushort Semiconductors)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort GetRequirement(ResourceType type) => type switch
    {
        ResourceType.Oil => Oil,
        ResourceType.Steel => Steel,
        ResourceType.Aluminum => Aluminum,
        ResourceType.Semiconductors => Semiconductors,
        _ => 0
    };
}

/// <summary>
/// Explicit 32-byte blittable ECS component representing a military factory production line.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct ProductionLineComponent
{
    public const float MinEfficiency = 0.10f;
    public const float DefaultStartingEfficiency = 0.25f;
    public const float DefaultEfficiencyCap = 0.80f;
    public const float MaxEfficiencyCap = 1.00f;
    public const ushort MaxFactoriesPerLine = 150;

    [FieldOffset(0)] public int LineId;
    [FieldOffset(4)] public byte CountryTag;
    [FieldOffset(5)] public EquipmentType Archetype;
    [FieldOffset(6)] public TechGeneration Generation;
    [FieldOffset(7)] public byte Priority;
    [FieldOffset(8)] public ushort AssignedMilitaryFactories;
    [FieldOffset(10)] public ushort ActiveMilitaryFactories;
    [FieldOffset(12)] public float Efficiency;
    [FieldOffset(16)] public float EfficiencyCap;
    [FieldOffset(20)] public float EquipmentCostIc;
    [FieldOffset(24)] public float ResourcePenalty;
    [FieldOffset(28)] public float AccumulatedFractionalUnits;

    /// <summary>
    /// Creates a deterministic ProductionLineComponent with clamped efficiency bounds and baseline IC cost.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ProductionLineComponent Create(
        int lineId,
        byte countryTag,
        EquipmentType archetype,
        TechGeneration generation = TechGeneration.Gen1_1980Baseline,
        ushort assignedMilitaryFactories = 1,
        byte priority = 100,
        float initialEfficiency = DefaultStartingEfficiency,
        float efficiencyCap = DefaultEfficiencyCap,
        float customEquipmentCostIc = 0.0f,
        float resourcePenalty = 0.0f)
    {
        float clampedCap = System.Math.Clamp(efficiencyCap, MinEfficiency, MaxEfficiencyCap);
        float clampedEff = System.Math.Clamp(initialEfficiency, MinEfficiency, clampedCap);
        ushort clampedFactories = assignedMilitaryFactories > MaxFactoriesPerLine
            ? MaxFactoriesPerLine
            : assignedMilitaryFactories;
        float costIc = customEquipmentCostIc > 0.0f
            ? customEquipmentCostIc
            : GetDefaultEquipmentCostIc(archetype, generation);

        return new ProductionLineComponent
        {
            LineId = lineId,
            CountryTag = countryTag,
            Archetype = archetype,
            Generation = generation,
            Priority = priority,
            AssignedMilitaryFactories = clampedFactories,
            ActiveMilitaryFactories = clampedFactories,
            Efficiency = clampedEff,
            EfficiencyCap = clampedCap,
            EquipmentCostIc = costIc,
            ResourcePenalty = System.Math.Clamp(resourcePenalty, 0.0f, 1.0f),
            AccumulatedFractionalUnits = 0.0f
        };
    }

    /// <summary>
    /// Returns the baseline Industrial Capacity (IC) cost per unit for the given archetype and generation tier.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetDefaultEquipmentCostIc(
        EquipmentType archetype,
        TechGeneration generation = TechGeneration.Gen1_1980Baseline)
    {
        float baseCost = archetype switch
        {
            EquipmentType.InfantryKit => 0.50f,
            EquipmentType.SupportVehicles => 2.50f,
            EquipmentType.IFV => 8.00f,
            EquipmentType.MBT => 14.00f,
            EquipmentType.Artillery => 4.50f,
            EquipmentType.AttackHeli => 22.00f,
            EquipmentType.TacticalAir => 36.00f,
            EquipmentType.PGM => 3.00f,
            _ => 1.00f
        };

        float genMultiplier = generation switch
        {
            TechGeneration.Gen0_1970Reserve => 0.80f,
            TechGeneration.Gen1_1980Baseline => 1.00f,
            TechGeneration.Gen2_1988LateColdWar => 1.25f,
            TechGeneration.Gen3_1995DigitalStealth => 1.60f,
            _ => 1.00f
        };

        return baseCost * genMultiplier;
    }

    /// <summary>
    /// Returns the strategic resource demand per active military factory for the given archetype and generation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EquipmentResourceCost GetResourceRequirementPerFactory(
        EquipmentType archetype,
        TechGeneration generation = TechGeneration.Gen1_1980Baseline)
    {
        ushort semiBonus = generation >= TechGeneration.Gen2_1988LateColdWar
            ? (ushort)(generation == TechGeneration.Gen3_1995DigitalStealth ? 2 : 1)
            : (ushort)0;

        return archetype switch
        {
            EquipmentType.InfantryKit => new EquipmentResourceCost(Oil: 0, Steel: 1, Aluminum: 0, Semiconductors: 0),
            EquipmentType.SupportVehicles => new EquipmentResourceCost(Oil: 1, Steel: 1, Aluminum: 0, Semiconductors: semiBonus),
            EquipmentType.IFV => new EquipmentResourceCost(Oil: 1, Steel: 2, Aluminum: 1, Semiconductors: semiBonus),
            EquipmentType.MBT => new EquipmentResourceCost(Oil: 1, Steel: 3, Aluminum: 0, Semiconductors: semiBonus),
            EquipmentType.Artillery => new EquipmentResourceCost(Oil: 0, Steel: 2, Aluminum: 0, Semiconductors: semiBonus),
            EquipmentType.AttackHeli => new EquipmentResourceCost(Oil: 1, Steel: 1, Aluminum: 2, Semiconductors: (ushort)(1 + semiBonus)),
            EquipmentType.TacticalAir => new EquipmentResourceCost(Oil: 2, Steel: 1, Aluminum: 3, Semiconductors: (ushort)(2 + semiBonus)),
            EquipmentType.PGM => new EquipmentResourceCost(Oil: 1, Steel: 1, Aluminum: 1, Semiconductors: (ushort)(2 + semiBonus)),
            _ => default
        };
    }
}
