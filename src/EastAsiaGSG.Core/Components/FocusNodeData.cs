using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Components;

/// <summary>
/// Focus availability condition types matching DATA_SCHEMAS.md Section 1.
/// </summary>
public enum ConditionType : byte
{
    None = 0,
    DateAfter = 1,
    DateBefore = 2,
    AtWar = 3,
    AtPeace = 4,
    ControlsState = 5,
    OwnsState = 6,
    StabilityGreaterThan = 7,
    WarSupportGreaterThan = 8,
    DefconAtMost = 9,
    HasCompletedFocus = 10,
    HomelandEscalationGreaterThan = 11
}

/// <summary>
/// Focus completion effect types matching all 15 actions in DATA_SCHEMAS.md Section 1.
/// </summary>
public enum EffectType : byte
{
    None = 0,
    AddPoliticalPower = 1,
    AddStability = 2,
    AddWarSupport = 3,
    ModifyConsumerGoods = 4,
    AddCivilianFactories = 5,
    AddMilitaryFactories = 6,
    AddNavalDockyards = 7,
    AddRadarSamLevel = 8,
    AddEquipmentStockpile = 9,
    SpawnDivisionTemplate = 10,
    GrantTechBonus = 11,
    CreateWargoal = 12,
    AddHomelandEscalation = 13,
    ShiftDefcon = 14,
    TriggerNewsEvent = 15
}

/// <summary>
/// Explicit 12-byte unmanaged representation of a focus availability condition.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 12)]
public struct FocusCondition
{
    [FieldOffset(0)] public ConditionType Type;
    [FieldOffset(1)] public byte TargetTag;
    [FieldOffset(2)] public ushort TargetId;
    [FieldOffset(4)] public int IntParam;
    [FieldOffset(8)] public float Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateDateAfter(int yearMonthDay) => new()
    {
        Type = ConditionType.DateAfter,
        TargetTag = CountryTag.None,
        IntParam = yearMonthDay
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateDateAfter(int year, int month, int day) =>
        CreateDateAfter((year * 10000) + (month * 100) + day);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateDateBefore(int yearMonthDay) => new()
    {
        Type = ConditionType.DateBefore,
        TargetTag = CountryTag.None,
        IntParam = yearMonthDay
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateDateBefore(int year, int month, int day) =>
        CreateDateBefore((year * 10000) + (month * 100) + day);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateAtWar(byte targetTag = CountryTag.None) => new()
    {
        Type = ConditionType.AtWar,
        TargetTag = targetTag
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateAtPeace(byte targetTag = CountryTag.None) => new()
    {
        Type = ConditionType.AtPeace,
        TargetTag = targetTag
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateControlsState(ushort stateId, byte targetTag = CountryTag.None) => new()
    {
        Type = ConditionType.ControlsState,
        TargetTag = targetTag,
        TargetId = stateId
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateOwnsState(ushort stateId, byte targetTag = CountryTag.None) => new()
    {
        Type = ConditionType.OwnsState,
        TargetTag = targetTag,
        TargetId = stateId
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateStabilityGreaterThan(float threshold, byte targetTag = CountryTag.None) => new()
    {
        Type = ConditionType.StabilityGreaterThan,
        TargetTag = targetTag,
        Value = threshold
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateWarSupportGreaterThan(float threshold, byte targetTag = CountryTag.None) => new()
    {
        Type = ConditionType.WarSupportGreaterThan,
        TargetTag = targetTag,
        Value = threshold
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateDefconAtMost(byte maxDefcon, byte targetTag = CountryTag.None) => new()
    {
        Type = ConditionType.DefconAtMost,
        TargetTag = targetTag,
        Value = maxDefcon
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateHasCompletedFocus(ushort focusIndex, byte targetTag = CountryTag.None) => new()
    {
        Type = ConditionType.HasCompletedFocus,
        TargetTag = targetTag,
        TargetId = focusIndex
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusCondition CreateHomelandEscalationGreaterThan(float threshold, byte targetTag = CountryTag.USA) => new()
    {
        Type = ConditionType.HomelandEscalationGreaterThan,
        TargetTag = targetTag,
        Value = threshold
    };
}

/// <summary>
/// Explicit 12-byte unmanaged representation of a focus completion effect.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 12)]
public struct FocusEffect
{
    public const ushort AllArchetypesTarget = ushort.MaxValue;
    public const ushort DefaultStateFallback = ushort.MaxValue;

    [FieldOffset(0)] public EffectType Type;
    [FieldOffset(1)] public byte TargetTag;
    [FieldOffset(2)] public ushort TargetId;
    [FieldOffset(4)] public int IntParam;
    [FieldOffset(8)] public float Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateAddPoliticalPower(float amount, byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.AddPoliticalPower,
        TargetTag = targetTag,
        Value = amount
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateAddStability(float delta, byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.AddStability,
        TargetTag = targetTag,
        Value = delta
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateAddWarSupport(float delta, byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.AddWarSupport,
        TargetTag = targetTag,
        Value = delta
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateModifyConsumerGoods(float delta, byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.ModifyConsumerGoods,
        TargetTag = targetTag,
        Value = delta
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateAddCivilianFactories(int count, ushort stateId = DefaultStateFallback, byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.AddCivilianFactories,
        TargetTag = targetTag,
        TargetId = stateId,
        Value = count
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateAddMilitaryFactories(int count, ushort stateId = DefaultStateFallback, byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.AddMilitaryFactories,
        TargetTag = targetTag,
        TargetId = stateId,
        Value = count
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateAddNavalDockyards(int count, ushort stateId = DefaultStateFallback, byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.AddNavalDockyards,
        TargetTag = targetTag,
        TargetId = stateId,
        Value = count
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateAddRadarSamLevel(int levels, ushort stateId = DefaultStateFallback, byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.AddRadarSamLevel,
        TargetTag = targetTag,
        TargetId = stateId,
        Value = levels
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateAddEquipmentStockpile(
        EquipmentType archetype,
        int amount,
        int generationOrAuto = -1,
        byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.AddEquipmentStockpile,
        TargetTag = targetTag,
        TargetId = (ushort)archetype,
        IntParam = generationOrAuto,
        Value = amount
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateSpawnDivisionTemplate(
        ushort templateId,
        int count = 1,
        int spawnProvinceId = -1,
        byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.SpawnDivisionTemplate,
        TargetTag = targetTag,
        TargetId = templateId,
        IntParam = spawnProvinceId,
        Value = count
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateGrantTechBonus(
        ushort archetypeOrAll = AllArchetypesTarget,
        float bonusValue = 1.0f,
        int targetGenerationOrAuto = -1,
        byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.GrantTechBonus,
        TargetTag = targetTag,
        TargetId = archetypeOrAll,
        IntParam = targetGenerationOrAuto,
        Value = bonusValue
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateWargoal(byte targetEnemyTag, float declareWarImmediatelyFlag = 0.0f) => new()
    {
        Type = EffectType.CreateWargoal,
        TargetTag = targetEnemyTag,
        Value = declareWarImmediatelyFlag
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateAddHomelandEscalation(float delta, byte targetTag = CountryTag.USA) => new()
    {
        Type = EffectType.AddHomelandEscalation,
        TargetTag = targetTag,
        Value = delta
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateShiftDefcon(float defconLevelOrDelta, int isRelativeDelta = 0, byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.ShiftDefcon,
        TargetTag = targetTag,
        IntParam = isRelativeDelta,
        Value = defconLevelOrDelta
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FocusEffect CreateTriggerNewsEvent(int eventIdHash, float value = 0.0f, byte targetTag = CountryTag.None) => new()
    {
        Type = EffectType.TriggerNewsEvent,
        TargetTag = targetTag,
        IntParam = eventIdHash,
        Value = value
    };
}

/// <summary>
/// Explicit 160-byte unmanaged representation of a single National Focus node in SimulationWorld.
/// Supports up to 4 prerequisites, 4 mutually exclusive nodes, 4 availability conditions, and 6 completion effects.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 160)]
public unsafe struct FocusNodeData
{
    public const int MaxPrerequisites = 4;
    public const int MaxMutuallyExclusive = 4;
    public const int MaxConditions = 4;
    public const int MaxEffects = 6;
    public const short DefaultCostDays = 35;

    [FieldOffset(0)]  public short FocusIndex;
    [FieldOffset(2)]  public byte CountryTag;
    [FieldOffset(3)]  public byte IsConfigured;
    [FieldOffset(4)]  public short GridX;
    [FieldOffset(6)]  public short GridY;
    [FieldOffset(8)]  public short CostDays;
    [FieldOffset(10)] public ushort AiBaseWeight;
    [FieldOffset(12)] public byte PrerequisiteCount;
    [FieldOffset(13)] public byte MutuallyExclusiveCount;
    [FieldOffset(14)] public byte ConditionCount;
    [FieldOffset(15)] public byte EffectCount;
    [FieldOffset(16)] public int FocusIdHash;
    [FieldOffset(20)] public int ReservedHeader;

    [FieldOffset(24)] public fixed short Prerequisites[MaxPrerequisites];
    [FieldOffset(32)] public fixed short MutuallyExclusive[MaxMutuallyExclusive];

    [FieldOffset(40)] public fixed byte ConditionTypes[MaxConditions];
    [FieldOffset(44)] public fixed byte ConditionTargetTags[MaxConditions];
    [FieldOffset(48)] public fixed ushort ConditionTargetIds[MaxConditions];
    [FieldOffset(56)] public fixed int ConditionIntParams[MaxConditions];
    [FieldOffset(72)] public fixed float ConditionValues[MaxConditions];

    [FieldOffset(88)]  public fixed byte EffectTypes[MaxEffects];
    [FieldOffset(94)]  public fixed byte EffectTargetTags[MaxEffects];
    [FieldOffset(100)] public fixed ushort EffectTargetIds[MaxEffects];
    [FieldOffset(112)] public fixed int EffectIntParams[MaxEffects];
    [FieldOffset(136)] public fixed float EffectValues[MaxEffects];

    /// <summary>
    /// Creates a configured FocusNodeData instance with default empty prerequisite, condition, and effect lists.
    /// </summary>
    public static FocusNodeData Create(
        byte countryTag,
        short focusIndex,
        short costDays = DefaultCostDays,
        short gridX = 0,
        short gridY = 0,
        ushort aiBaseWeight = 100,
        int focusIdHash = 0)
    {
        FocusNodeData node = new()
        {
            FocusIndex = focusIndex,
            CountryTag = countryTag,
            IsConfigured = 1,
            GridX = gridX,
            GridY = gridY,
            CostDays = costDays <= 0 ? DefaultCostDays : costDays,
            AiBaseWeight = aiBaseWeight,
            FocusIdHash = focusIdHash
        };

        for (int i = 0; i < MaxPrerequisites; i++)
        {
            node.Prerequisites[i] = -1;
        }

        for (int i = 0; i < MaxMutuallyExclusive; i++)
        {
            node.MutuallyExclusive[i] = -1;
        }

        return node;
    }

    /// <summary>
    /// Computes a deterministic case-sensitive 32-bit FNV-1a hash for focus or event string identifiers.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ComputeIdHash(ReadOnlySpan<char> id)
    {
        uint hash = 2166136261u;
        for (int i = 0; i < id.Length; i++)
        {
            hash ^= id[i];
            hash *= 16777619u;
        }
        return unchecked((int)hash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AddPrerequisite(short prerequisiteFocusIndex)
    {
        if (prerequisiteFocusIndex < 0 || PrerequisiteCount >= MaxPrerequisites)
        {
            return false;
        }

        Prerequisites[PrerequisiteCount++] = prerequisiteFocusIndex;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly short GetPrerequisite(int slot)
    {
        if ((uint)slot >= PrerequisiteCount)
        {
            return -1;
        }
        return Prerequisites[slot];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AddMutuallyExclusive(short exclusiveFocusIndex)
    {
        if (exclusiveFocusIndex < 0 || MutuallyExclusiveCount >= MaxMutuallyExclusive)
        {
            return false;
        }

        MutuallyExclusive[MutuallyExclusiveCount++] = exclusiveFocusIndex;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly short GetMutuallyExclusive(int slot)
    {
        if ((uint)slot >= MutuallyExclusiveCount)
        {
            return -1;
        }
        return MutuallyExclusive[slot];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AddCondition(in FocusCondition condition)
    {
        if (condition.Type == ConditionType.None || ConditionCount >= MaxConditions)
        {
            return false;
        }

        int slot = ConditionCount++;
        ConditionTypes[slot] = (byte)condition.Type;
        ConditionTargetTags[slot] = condition.TargetTag;
        ConditionTargetIds[slot] = condition.TargetId;
        ConditionIntParams[slot] = condition.IntParam;
        ConditionValues[slot] = condition.Value;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly FocusCondition GetCondition(int slot)
    {
        if ((uint)slot >= ConditionCount)
        {
            return default;
        }

        return new FocusCondition
        {
            Type = (ConditionType)ConditionTypes[slot],
            TargetTag = ConditionTargetTags[slot],
            TargetId = ConditionTargetIds[slot],
            IntParam = ConditionIntParams[slot],
            Value = ConditionValues[slot]
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AddEffect(in FocusEffect effect)
    {
        if (effect.Type == EffectType.None || EffectCount >= MaxEffects)
        {
            return false;
        }

        int slot = EffectCount++;
        EffectTypes[slot] = (byte)effect.Type;
        EffectTargetTags[slot] = effect.TargetTag;
        EffectTargetIds[slot] = effect.TargetId;
        EffectIntParams[slot] = effect.IntParam;
        EffectValues[slot] = effect.Value;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly FocusEffect GetEffect(int slot)
    {
        if ((uint)slot >= EffectCount)
        {
            return default;
        }

        return new FocusEffect
        {
            Type = (EffectType)EffectTypes[slot],
            TargetTag = EffectTargetTags[slot],
            TargetId = EffectTargetIds[slot],
            IntParam = EffectIntParams[slot],
            Value = EffectValues[slot]
        };
    }
}
