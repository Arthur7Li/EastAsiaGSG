using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Components;

/// <summary>
/// Explicit 24-byte unmanaged record representing a division template's full-strength (Strength == 1.0f)
/// Table of Organization and Equipment (TOE) across the 6 land equipment archetypes (0..5) and TotalManpower.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public unsafe struct DivisionTemplateRecord
{
    public const int LandArchetypeCount = 6; // EquipmentType 0..5 (InfantryKit .. AttackHeli)

    public const ushort DefaultMechanizedTemplateId = 1;
    public const ushort DefaultInfantryTemplateId = 2;
    public const ushort DefaultArmoredTemplateId = 3;
    public const ushort DefaultAirAssaultTemplateId = 4;

    [FieldOffset(0)]  public ushort TemplateId;
    [FieldOffset(2)]  public byte CombatWidth;
    [FieldOffset(3)]  public byte IsConfigured;
    [FieldOffset(4)]  public int TotalManpower;
    [FieldOffset(8)]  public fixed ushort RequiredEquipment[LandArchetypeCount];
    [FieldOffset(20)] public int Reserved;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ushort GetRequired(EquipmentType type)
    {
        int idx = (int)type;
        if ((uint)idx >= LandArchetypeCount)
        {
            return 0;
        }
        return RequiredEquipment[idx];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRequired(EquipmentType type, ushort count)
    {
        int idx = (int)type;
        if ((uint)idx < LandArchetypeCount)
        {
            RequiredEquipment[idx] = count;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int GetTotalEquipmentUnits()
    {
        return RequiredEquipment[0]
             + RequiredEquipment[1]
             + RequiredEquipment[2]
             + RequiredEquipment[3]
             + RequiredEquipment[4]
             + RequiredEquipment[5];
    }

    /// <summary>
    /// Creates a standard 1985 Mechanized Infantry Division TOE (TemplateId = 1, 20 width, 10,000 men).
    /// Matches DATA_SCHEMAS.md: 3x IFV, 1x MBT, 1x Artillery, 1x InfantryKit + SupportVehicles, Artillery support.
    /// </summary>
    public static DivisionTemplateRecord CreateMechanizedDivision(ushort templateId = DefaultMechanizedTemplateId)
    {
        DivisionTemplateRecord tpl = new()
        {
            TemplateId = templateId,
            CombatWidth = 20,
            IsConfigured = 1,
            TotalManpower = 10_000
        };
        tpl.SetRequired(EquipmentType.InfantryKit, 250);
        tpl.SetRequired(EquipmentType.SupportVehicles, 40);
        tpl.SetRequired(EquipmentType.IFV, 150);
        tpl.SetRequired(EquipmentType.MBT, 40);
        tpl.SetRequired(EquipmentType.Artillery, 36);
        tpl.SetRequired(EquipmentType.AttackHeli, 0);
        return tpl;
    }

    /// <summary>
    /// Creates a standard 1985 Foot/Motorized Infantry Division TOE (TemplateId = 2, 20 width, 10,000 men).
    /// </summary>
    public static DivisionTemplateRecord CreateInfantryDivision(ushort templateId = DefaultInfantryTemplateId)
    {
        DivisionTemplateRecord tpl = new()
        {
            TemplateId = templateId,
            CombatWidth = 20,
            IsConfigured = 1,
            TotalManpower = 10_000
        };
        tpl.SetRequired(EquipmentType.InfantryKit, 500);
        tpl.SetRequired(EquipmentType.SupportVehicles, 30);
        tpl.SetRequired(EquipmentType.IFV, 0);
        tpl.SetRequired(EquipmentType.MBT, 0);
        tpl.SetRequired(EquipmentType.Artillery, 36);
        tpl.SetRequired(EquipmentType.AttackHeli, 0);
        return tpl;
    }

    /// <summary>
    /// Creates a standard 1985 Armored Spearhead Division TOE (TemplateId = 3, 20 width, 9,500 men).
    /// </summary>
    public static DivisionTemplateRecord CreateArmoredDivision(ushort templateId = DefaultArmoredTemplateId)
    {
        DivisionTemplateRecord tpl = new()
        {
            TemplateId = templateId,
            CombatWidth = 20,
            IsConfigured = 1,
            TotalManpower = 9_500
        };
        tpl.SetRequired(EquipmentType.InfantryKit, 150);
        tpl.SetRequired(EquipmentType.SupportVehicles, 50);
        tpl.SetRequired(EquipmentType.IFV, 100);
        tpl.SetRequired(EquipmentType.MBT, 120);
        tpl.SetRequired(EquipmentType.Artillery, 36);
        tpl.SetRequired(EquipmentType.AttackHeli, 0);
        return tpl;
    }

    /// <summary>
    /// Creates a standard 1985 Air-Mobile / Attack Helicopter Brigade TOE (TemplateId = 4, 20 width, 8,500 men).
    /// </summary>
    public static DivisionTemplateRecord CreateAirAssaultDivision(ushort templateId = DefaultAirAssaultTemplateId)
    {
        DivisionTemplateRecord tpl = new()
        {
            TemplateId = templateId,
            CombatWidth = 20,
            IsConfigured = 1,
            TotalManpower = 8_500
        };
        tpl.SetRequired(EquipmentType.InfantryKit, 350);
        tpl.SetRequired(EquipmentType.SupportVehicles, 40);
        tpl.SetRequired(EquipmentType.IFV, 0);
        tpl.SetRequired(EquipmentType.MBT, 0);
        tpl.SetRequired(EquipmentType.Artillery, 24);
        tpl.SetRequired(EquipmentType.AttackHeli, 36);
        return tpl;
    }

    /// <summary>
    /// Builds a DivisionTemplateRecord from combat battalion and support company archetype spans (DATA_SCHEMAS.md Section 3).
    /// </summary>
    public static DivisionTemplateRecord FromBattalions(
        ushort templateId,
        byte combatWidth,
        ReadOnlySpan<EquipmentType> battalions,
        ReadOnlySpan<EquipmentType> supportCompanies)
    {
        DivisionTemplateRecord tpl = new()
        {
            TemplateId = templateId,
            CombatWidth = combatWidth == 0 ? (byte)20 : combatWidth,
            IsConfigured = 1,
            TotalManpower = 0
        };

        for (int i = 0; i < battalions.Length; i++)
        {
            switch (battalions[i])
            {
                case EquipmentType.InfantryKit:
                    tpl.RequiredEquipment[(int)EquipmentType.InfantryKit] += 100;
                    tpl.TotalManpower += 1_600;
                    break;
                case EquipmentType.SupportVehicles:
                    tpl.RequiredEquipment[(int)EquipmentType.SupportVehicles] += 40;
                    tpl.RequiredEquipment[(int)EquipmentType.InfantryKit] += 50;
                    tpl.TotalManpower += 1_200;
                    break;
                case EquipmentType.IFV:
                    tpl.RequiredEquipment[(int)EquipmentType.IFV] += 50;
                    tpl.RequiredEquipment[(int)EquipmentType.InfantryKit] += 50;
                    tpl.TotalManpower += 1_500;
                    break;
                case EquipmentType.MBT:
                    tpl.RequiredEquipment[(int)EquipmentType.MBT] += 40;
                    tpl.TotalManpower += 1_200;
                    break;
                case EquipmentType.Artillery:
                    tpl.RequiredEquipment[(int)EquipmentType.Artillery] += 24;
                    tpl.TotalManpower += 1_000;
                    break;
                case EquipmentType.AttackHeli:
                    tpl.RequiredEquipment[(int)EquipmentType.AttackHeli] += 18;
                    tpl.TotalManpower += 900;
                    break;
                default:
                    break;
            }
        }

        for (int i = 0; i < supportCompanies.Length; i++)
        {
            switch (supportCompanies[i])
            {
                case EquipmentType.InfantryKit:
                    tpl.RequiredEquipment[(int)EquipmentType.InfantryKit] += 30;
                    tpl.TotalManpower += 400;
                    break;
                case EquipmentType.SupportVehicles:
                    tpl.RequiredEquipment[(int)EquipmentType.SupportVehicles] += 40;
                    tpl.TotalManpower += 500;
                    break;
                case EquipmentType.IFV:
                    tpl.RequiredEquipment[(int)EquipmentType.IFV] += 20;
                    tpl.TotalManpower += 400;
                    break;
                case EquipmentType.MBT:
                    tpl.RequiredEquipment[(int)EquipmentType.MBT] += 15;
                    tpl.TotalManpower += 400;
                    break;
                case EquipmentType.Artillery:
                    tpl.RequiredEquipment[(int)EquipmentType.Artillery] += 12;
                    tpl.TotalManpower += 500;
                    break;
                case EquipmentType.AttackHeli:
                    tpl.RequiredEquipment[(int)EquipmentType.AttackHeli] += 12;
                    tpl.TotalManpower += 400;
                    break;
                default:
                    break;
            }
        }

        if (tpl.TotalManpower <= 0)
        {
            tpl.TotalManpower = 10_000;
        }

        return tpl;
    }
}
