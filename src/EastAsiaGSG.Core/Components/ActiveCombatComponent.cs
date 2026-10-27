using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Components;

/// <summary>
/// Resolution status of a land combat engagement.
/// </summary>
public enum CombatOutcome : byte
{
    Ongoing = 0,
    AttackerVictory = 1,
    DefenderVictory = 2
}

/// <summary>
/// Explicit 64-byte unmanaged ECS component tracking an active land battle at a contested province.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public unsafe struct ActiveCombatComponent
{
    public const int BaseCombatWidth = 80;
    public const int AdditionalDirectionWidth = 20;
    public const int MaxAttackDirections = 8;

    [FieldOffset(0)]  public int CombatId;
    [FieldOffset(4)]  public ushort TargetProvinceId;
    [FieldOffset(6)]  public ushort PrimaryOriginProvinceId;
    [FieldOffset(8)]  public byte AttackerTag;
    [FieldOffset(9)]  public byte DefenderTag;
    [FieldOffset(10)] public byte AttackDirectionCount;
    [FieldOffset(11)] public CombatOutcome Outcome;

    [FieldOffset(12)] public ushort TotalCombatWidth;
    [FieldOffset(14)] public ushort UsedAttackerWidth;
    [FieldOffset(16)] public ushort UsedDefenderWidth;
    [FieldOffset(18)] public byte EngagedAttackerCount;
    [FieldOffset(19)] public byte EngagedDefenderCount;

    [FieldOffset(20)] public int ElapsedSixHourTicks;
    [FieldOffset(24)] public float BattleProgress; // 0..100 HOI4 battle indicator (>50 favors attacker)
    [FieldOffset(28)] public float LastTickAttackerOrgDamage;
    [FieldOffset(32)] public float LastTickDefenderOrgDamage;
    [FieldOffset(36)] public float LastTickAttackerStrengthDamage;
    [FieldOffset(40)] public float LastTickDefenderStrengthDamage;

    [FieldOffset(44)] public fixed ushort AttackOriginProvinces[MaxAttackDirections];
    [FieldOffset(60)] public int Reserved;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort CalculateCombatWidth(int attackDirectionCount)
    {
        if (attackDirectionCount <= 1)
        {
            return BaseCombatWidth;
        }
        return (ushort)(BaseCombatWidth + AdditionalDirectionWidth * (attackDirectionCount - 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearOriginProvinces()
    {
        AttackDirectionCount = 0;
        TotalCombatWidth = BaseCombatWidth;
        for (int i = 0; i < MaxAttackDirections; i++)
        {
            AttackOriginProvinces[i] = DivisionComponent.NoProvince;
        }
    }

    /// <summary>
    /// Registers an attacking origin province and updates AttackDirectionCount and TotalCombatWidth.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RegisterAttackOrigin(ushort originProvinceId)
    {
        for (int i = 0; i < AttackDirectionCount; i++)
        {
            if (AttackOriginProvinces[i] == originProvinceId)
            {
                return;
            }
        }

        if (AttackDirectionCount < MaxAttackDirections)
        {
            AttackOriginProvinces[AttackDirectionCount] = originProvinceId;
            AttackDirectionCount++;
            TotalCombatWidth = CalculateCombatWidth(AttackDirectionCount);
        }
    }

    public static ActiveCombatComponent Create(
        int combatId,
        ushort targetProvinceId,
        ushort primaryOriginProvinceId,
        byte attackerTag,
        byte defenderTag)
    {
        ActiveCombatComponent combat = new()
        {
            CombatId = combatId,
            TargetProvinceId = targetProvinceId,
            PrimaryOriginProvinceId = primaryOriginProvinceId,
            AttackerTag = attackerTag,
            DefenderTag = defenderTag,
            AttackDirectionCount = 0,
            Outcome = CombatOutcome.Ongoing,
            TotalCombatWidth = BaseCombatWidth,
            UsedAttackerWidth = 0,
            UsedDefenderWidth = 0,
            EngagedAttackerCount = 0,
            EngagedDefenderCount = 0,
            ElapsedSixHourTicks = 0,
            BattleProgress = 50.0f,
            LastTickAttackerOrgDamage = 0.0f,
            LastTickDefenderOrgDamage = 0.0f,
            LastTickAttackerStrengthDamage = 0.0f,
            LastTickDefenderStrengthDamage = 0.0f
        };
        combat.ClearOriginProvinces();
        combat.RegisterAttackOrigin(primaryOriginProvinceId);
        return combat;
    }
}
