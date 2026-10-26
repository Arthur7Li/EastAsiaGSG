using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Components;

/// <summary>
/// Explicit 184-byte blittable representation of a country's macro state (Index == TagId, 0..15).
/// EquipmentStockpile[32] flattens 8 EquipmentTypes x 4 TechGenerations.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 184)]
public unsafe struct CountryState
{
    public const int ArchetypeCount = 8;
    public const int TechGenerationCount = 4;
    public const int StockpileSlotCount = ArchetypeCount * TechGenerationCount; // 32

    [FieldOffset(0)] public byte TagId;
    [FieldOffset(1)] public FactionId Faction;
    [FieldOffset(2)] public byte Defcon;
    [FieldOffset(3)] public byte HomelandEscalation;
    [FieldOffset(4)] public float PoliticalPower;
    [FieldOffset(8)] public float Stability;
    [FieldOffset(12)] public float WarSupport;
    [FieldOffset(16)] public float ConsumerGoodsRatio;
    [FieldOffset(20)] public float FactoryOutputModifier;
    [FieldOffset(24)] public float ConstructionSpeedModifier;
    [FieldOffset(28)] public int Manpower;
    [FieldOffset(32)] public short ActiveFocusId;
    [FieldOffset(34)] public short FocusDaysRemaining;
    [FieldOffset(36)] public short TradeBalanceFactories;
    [FieldOffset(38)] public ushort OffMapMilitaryFactories;
    [FieldOffset(40)] public ulong CompletedFocusMaskLow;
    [FieldOffset(48)] public ulong CompletedFocusMaskHigh;
    [FieldOffset(56)] public fixed int EquipmentStockpile[StockpileSlotCount];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetStockpileIndex(EquipmentType type, TechGeneration generation = TechGeneration.Gen1_1980Baseline)
    {
        return ((int)type << 2) | ((int)generation & 0x3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int GetStockpile(EquipmentType type, TechGeneration generation = TechGeneration.Gen1_1980Baseline)
    {
        int idx = GetStockpileIndex(type, generation);
        return EquipmentStockpile[idx];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetStockpile(EquipmentType type, TechGeneration generation, int amount)
    {
        int idx = GetStockpileIndex(type, generation);
        EquipmentStockpile[idx] = amount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddStockpile(EquipmentType type, TechGeneration generation, int delta)
    {
        int idx = GetStockpileIndex(type, generation);
        int updated = EquipmentStockpile[idx] + delta;
        EquipmentStockpile[idx] = updated < 0 ? 0 : updated;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int GetTotalArchetypeStockpile(EquipmentType type)
    {
        int baseIdx = (int)type << 2;
        return EquipmentStockpile[baseIdx]
             + EquipmentStockpile[baseIdx + 1]
             + EquipmentStockpile[baseIdx + 2]
             + EquipmentStockpile[baseIdx + 3];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool HasCompletedFocus(int focusIndex)
    {
        if ((uint)focusIndex < 64)
        {
            return (CompletedFocusMaskLow & (1UL << focusIndex)) != 0;
        }
        if ((uint)focusIndex < 128)
        {
            return (CompletedFocusMaskHigh & (1UL << (focusIndex - 64))) != 0;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkFocusCompleted(int focusIndex)
    {
        if ((uint)focusIndex < 64)
        {
            CompletedFocusMaskLow |= 1UL << focusIndex;
        }
        else if ((uint)focusIndex < 128)
        {
            CompletedFocusMaskHigh |= 1UL << (focusIndex - 64);
        }
    }
}
