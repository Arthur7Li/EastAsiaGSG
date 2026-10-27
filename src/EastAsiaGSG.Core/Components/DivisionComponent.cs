using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Components;

/// <summary>
/// Tactical execution state of a field division entity.
/// </summary>
public enum DivisionState : byte
{
    Idle = 0,
    Moving = 1,
    Attacking = 2,
    Defending = 3,
    Retreating = 4,
    Shattered = 5
}

/// <summary>
/// Explicit 128-byte unmanaged ECS component representing a field division/brigade in Arch.Core.World.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
public unsafe struct DivisionComponent
{
    public const int MaxWaypoints = 16;
    public const ushort NoProvince = ushort.MaxValue;
    public const int NoCombat = -1;

    [FieldOffset(0)]  public int DivisionId;
    [FieldOffset(4)]  public ushort TemplateId;
    [FieldOffset(6)]  public byte CountryTag;
    [FieldOffset(7)]  public byte CombatWidth;
    [FieldOffset(8)]  public ushort CurrentProvinceId;
    [FieldOffset(10)] public ushort TargetProvinceId;
    [FieldOffset(12)] public ushort DestinationProvinceId;
    [FieldOffset(14)] public ushort RetreatProvinceId;

    [FieldOffset(16)] public DivisionState State;
    [FieldOffset(17)] public byte PgmAuthorized;
    [FieldOffset(18)] public byte WaypointCount;
    [FieldOffset(19)] public byte CurrentWaypointIndex;
    [FieldOffset(20)] public int ActiveCombatId;

    [FieldOffset(24)] public float MovementProgress;
    [FieldOffset(28)] public float CurrentEdgeCost;
    [FieldOffset(32)] public float Speed;

    [FieldOffset(36)] public float Organization;
    [FieldOffset(40)] public float MaxOrganization;
    [FieldOffset(44)] public float Strength;
    [FieldOffset(48)] public float Experience;

    [FieldOffset(52)] public float SoftAttack;
    [FieldOffset(56)] public float HardAttack;
    [FieldOffset(60)] public float Defense;
    [FieldOffset(64)] public float Breakthrough;
    [FieldOffset(68)] public float Armor;
    [FieldOffset(72)] public float Piercing;
    [FieldOffset(76)] public float Hardness;
    [FieldOffset(80)] public float Entrenchment;

    [FieldOffset(84)] public fixed ushort Waypoints[MaxWaypoints];
    [FieldOffset(116)] public int Reserved0;
    [FieldOffset(120)] public int Reserved1;
    [FieldOffset(124)] public int Reserved2;

    public readonly bool IsPgmAuthorized
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => PgmAuthorized != 0;
    }

    public readonly bool IsCombatCapable
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => State != DivisionState.Shattered && Organization > 0.0f && Strength > 0.0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ushort GetWaypoint(int index)
    {
        if ((uint)index >= MaxWaypoints)
        {
            return NoProvince;
        }
        return Waypoints[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetWaypoint(int index, ushort provinceId)
    {
        if ((uint)index < MaxWaypoints)
        {
            Waypoints[index] = provinceId;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearWaypoints()
    {
        WaypointCount = 0;
        CurrentWaypointIndex = 0;
        TargetProvinceId = NoProvince;
        DestinationProvinceId = NoProvince;
        MovementProgress = 0.0f;
        CurrentEdgeCost = 0.0f;
    }

    /// <summary>
    /// Creates a baseline division component with initialized combat statistics.
    /// </summary>
    public static DivisionComponent Create(
        int divisionId,
        byte countryTag,
        ushort startingProvinceId,
        ushort templateId = 1,
        byte combatWidth = 20,
        float softAttack = 45.0f,
        float hardAttack = 25.0f,
        float defense = 50.0f,
        float breakthrough = 35.0f,
        float armor = 20.0f,
        float piercing = 30.0f,
        float hardness = 0.25f,
        float maxOrganization = 60.0f,
        float startingStrength = 1.0f,
        float startingExperience = 0.50f,
        float speed = 28.0f,
        bool pgmAuthorized = false)
    {
        return new DivisionComponent
        {
            DivisionId = divisionId,
            TemplateId = templateId,
            CountryTag = countryTag,
            CombatWidth = combatWidth == 0 ? (byte)20 : combatWidth,
            CurrentProvinceId = startingProvinceId,
            TargetProvinceId = NoProvince,
            DestinationProvinceId = NoProvince,
            RetreatProvinceId = NoProvince,
            State = DivisionState.Idle,
            PgmAuthorized = pgmAuthorized ? (byte)1 : (byte)0,
            WaypointCount = 0,
            CurrentWaypointIndex = 0,
            ActiveCombatId = NoCombat,
            MovementProgress = 0.0f,
            CurrentEdgeCost = 0.0f,
            Speed = speed,
            Organization = maxOrganization,
            MaxOrganization = maxOrganization,
            Strength = System.Math.Clamp(startingStrength, 0.0f, 1.0f),
            Experience = System.Math.Clamp(startingExperience, 0.0f, 1.0f),
            SoftAttack = softAttack,
            HardAttack = hardAttack,
            Defense = defense,
            Breakthrough = breakthrough,
            Armor = armor,
            Piercing = piercing,
            Hardness = System.Math.Clamp(hardness, 0.0f, 1.0f),
            Entrenchment = 0.0f
        };
    }
}
