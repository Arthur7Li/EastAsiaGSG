using System.Globalization;
using System.Text.Json;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Schemas;

namespace EastAsiaGSG.Data.Loaders;

/// <summary>
/// Metadata and compiled index mapping returned after loading a National Focus Tree into SimulationWorld.
/// Used by UI (FocusTreeViewer) and tests to map string focus IDs to unmanaged FocusIndex (0..127).
/// </summary>
public sealed class LoadedFocusTree
{
    public required byte CountryTag { get; init; }
    public required string CountryCode { get; init; }
    public required string Era { get; init; }
    public required IReadOnlyList<FocusNodeDto> Focuses { get; init; }
    public required IReadOnlyDictionary<string, short> FocusIndexById { get; init; }

    public short GetFocusIndex(string focusId)
    {
        return FocusIndexById.TryGetValue(focusId, out short index) ? index : (short)-1;
    }
}

/// <summary>
/// Deserializes, validates, and compiles National Focus Trees from JSON (/data/focus_trees/{TAG}_1985.json)
/// into unmanaged FocusNodeData entries in SimulationWorld.
/// </summary>
public static class JsonFocusTreeLoader
{
    private static readonly Dictionary<string, ushort> SymbolicStateAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Korean Peninsula (Consolidated into 6 states: 0, 1, 2, 5, 6, 7)
        ["STATE_SEOUL"] = 0,
        ["STATE_GYEONGGI"] = 0,
        ["STATE_GANGWON_SOUTH"] = 0,
        ["STATE_YEONGNAM"] = 1,
        ["STATE_GYEONGSANG"] = 1,
        ["STATE_BUSAN"] = 1,
        ["STATE_CHUNGCHEONG"] = 2,
        ["STATE_JEOLLA"] = 2,
        ["STATE_HOSEO"] = 2,
        ["STATE_HONAM"] = 2,
        ["STATE_PYONGYANG"] = 5,
        ["STATE_HWANGHAE"] = 5,
        ["STATE_KAESONG"] = 5,
        ["STATE_PYONGAN"] = 6,
        ["STATE_CHAGANG"] = 6,
        ["STATE_GANGWON_NORTH"] = 7,
        ["STATE_HAMGYONG"] = 7,
        ["STATE_WONSAN"] = 7,

        // Soviet Far East & Trans-Siberian Artery
        ["STATE_IRKUTSK"] = 10,
        ["STATE_TRANS_SIBERIAN"] = 10,
        ["STATE_KHABAROVSK"] = 10,
        ["STATE_VLADIVOSTOK"] = 11,
        ["STATE_PRIMORYE"] = 11,
        ["STATE_SAKHALIN"] = 12,

        // People's Republic of China (10 Geo-Specific Administrative Provinces)
        ["STATE_BEIJING"] = 20,
        ["STATE_TIANJIN"] = 20,
        ["STATE_HEBEI"] = 21,
        ["STATE_LIAONING"] = 22,
        ["STATE_MANCHURIA"] = 22,
        ["STATE_SHENYANG"] = 22,
        ["STATE_JILIN"] = 23,
        ["STATE_HEILONGJIANG"] = 23,
        ["STATE_SHANDONG"] = 24,
        ["STATE_JIANGSU"] = 25,
        ["STATE_SHANGHAI"] = 25,
        ["STATE_ZHEJIANG"] = 26,
        ["STATE_FUJIAN"] = 27,
        ["STATE_GUANGDONG"] = 28,
        ["STATE_SHENZHEN"] = 28,
        ["STATE_HAINAN"] = 29,
        ["STATE_LEIZHOU"] = 29,

        // Japan
        ["STATE_KANTO"] = 30,
        ["STATE_TOKYO"] = 30,
        ["STATE_HOKKAIDO"] = 31,
        ["STATE_KANSAI"] = 32,
        ["STATE_OSAKA"] = 32,
        ["STATE_KYUSHU"] = 33,

        // Taiwan (ROC)
        ["STATE_TAIPEI"] = 40,
        ["STATE_HSINCHU"] = 40,
        ["STATE_KAOHSIUNG"] = 41,
        ["STATE_KINMEN"] = 42,
        ["STATE_PENGHU"] = 42,

        // United States Pacific Command (USPACOM)
        ["STATE_OKINAWA"] = 50,
        ["STATE_GUAM"] = 51,
        ["STATE_SUBIC"] = 61,

        // Southeast Asia Regional Minors
        ["STATE_TONKIN"] = 60,
        ["STATE_HANOI"] = 60,
        ["STATE_LUZON"] = 61,
        ["STATE_MANILA"] = 61,
        ["STATE_MONGOLIA"] = 62,
        ["STATE_BANGKOK"] = 63,
        ["STATE_THAILAND"] = 63,
        ["STATE_LAOS"] = 64,
        ["STATE_CAMBODIA"] = 65,
        ["STATE_MYANMAR"] = 66,
        ["STATE_MALAYSIA"] = 67,
        ["STATE_ANNAM"] = 68,
        ["STATE_DANANG"] = 68,
        ["STATE_COCHINCHINA"] = 69,
        ["STATE_CAMRANH"] = 69,
        ["STATE_VISAYAS"] = 70,
        ["STATE_KHORAT"] = 71,
        ["STATE_MANDALAY"] = 72
    };

    /// <summary>
    /// Loads and registers a National Focus Tree from a JSON file path on disk.
    /// </summary>
    public static LoadedFocusTree LoadFocusTree(
        SimulationWorld world,
        string jsonFilePath,
        byte? overrideCountryTag = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);

        if (!File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException($"Focus tree JSON file not found at '{jsonFilePath}'.", jsonFilePath);
        }

        using FileStream stream = File.OpenRead(jsonFilePath);
        return LoadFocusTree(world, stream, overrideCountryTag);
    }

    /// <summary>
    /// Loads and registers a National Focus Tree from a readable Stream.
    /// </summary>
    public static LoadedFocusTree LoadFocusTree(
        SimulationWorld world,
        Stream stream,
        byte? overrideCountryTag = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(stream);

        FocusTreeDto? dto = JsonSerializer.Deserialize(stream, MapJsonContext.Default.FocusTreeDto);
        return CompileAndRegister(world, dto, overrideCountryTag);
    }

    /// <summary>
    /// Loads and registers a National Focus Tree from an in-memory UTF-8 JSON byte span.
    /// </summary>
    public static LoadedFocusTree LoadFocusTree(
        SimulationWorld world,
        ReadOnlySpan<byte> utf8Json,
        byte? overrideCountryTag = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        FocusTreeDto? dto = JsonSerializer.Deserialize(utf8Json, MapJsonContext.Default.FocusTreeDto);
        return CompileAndRegister(world, dto, overrideCountryTag);
    }

    /// <summary>
    /// Validates and compiles a deserialized FocusTreeDto into SimulationWorld.
    /// </summary>
    public static LoadedFocusTree CompileAndRegister(
        SimulationWorld world,
        FocusTreeDto? dto,
        byte? overrideCountryTag = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (dto == null)
        {
            throw new MapValidationException("Focus tree JSON could not be deserialized.");
        }

        byte countryTag;
        if (overrideCountryTag.HasValue)
        {
            if (overrideCountryTag.Value >= SimulationWorld.MaxCountries)
            {
                throw new MapValidationException($"Override CountryTag {overrideCountryTag.Value} exceeds MaxCountries.");
            }
            countryTag = overrideCountryTag.Value;
        }
        else if (!CountryTag.TryParse(dto.CountryTag, out countryTag))
        {
            throw new MapValidationException($"Focus tree has invalid or unknown countryTag '{dto.CountryTag}'.");
        }

        if (dto.Focuses == null || dto.Focuses.Count == 0)
        {
            throw new MapValidationException($"Focus tree for '{dto.CountryTag}' contains zero focuses.");
        }

        if (dto.Focuses.Count > SimulationWorld.MaxFocusesPerCountry)
        {
            throw new MapValidationException(
                $"Focus tree for '{dto.CountryTag}' has {dto.Focuses.Count} focuses, exceeding MaxFocusesPerCountry ({SimulationWorld.MaxFocusesPerCountry}).");
        }

        // Pass 1: Index all Focus IDs and validate uniqueness
        var indexById = new Dictionary<string, short>(dto.Focuses.Count, StringComparer.Ordinal);
        for (short i = 0; i < dto.Focuses.Count; i++)
        {
            FocusNodeDto nodeDto = dto.Focuses[i];
            if (string.IsNullOrWhiteSpace(nodeDto.Id))
            {
                throw new MapValidationException($"Focus at index {i} in '{dto.CountryTag}' has an empty id.");
            }

            if (!indexById.TryAdd(nodeDto.Id, i))
            {
                throw new MapValidationException(
                    $"Duplicate focus id '{nodeDto.Id}' detected in focus tree for '{dto.CountryTag}'.");
            }

            if (nodeDto.CostDays <= 0)
            {
                throw new MapValidationException(
                    $"Focus '{nodeDto.Id}' has invalid costDays ({nodeDto.CostDays}); must be > 0.");
            }
        }

        // Validate (GridX, GridY) coordinate uniqueness across all nodes
        var gridCoords = new HashSet<long>(dto.Focuses.Count);
        for (int i = 0; i < dto.Focuses.Count; i++)
        {
            FocusNodeDto nodeDto = dto.Focuses[i];
            long coordKey = ((long)nodeDto.GridX << 32) | ((long)nodeDto.GridY & 0xFFFFFFFFL);
            if (!gridCoords.Add(coordKey))
            {
                throw new MapValidationException(
                    $"Duplicate grid coordinates (GridX={nodeDto.GridX}, GridY={nodeDto.GridY}) detected on focus '{nodeDto.Id}' in '{dto.CountryTag}'.");
            }
        }

        // Validate DAG acyclicity using 3-color DFS (0 = unvisited, 1 = visiting, 2 = visited)
        Span<byte> visitState = stackalloc byte[dto.Focuses.Count];
        for (int i = 0; i < dto.Focuses.Count; i++)
        {
            if (visitState[i] == 0)
            {
                CheckCycleDfs(i, dto.Focuses, indexById, visitState);
            }
        }

        // Clear any existing focuses for this country before registering the new tree
        int baseSlot = countryTag * SimulationWorld.MaxFocusesPerCountry;
        for (int i = 0; i < SimulationWorld.MaxFocusesPerCountry; i++)
        {
            world.FocusTrees[baseSlot + i] = default;
        }
        world.CountryFocusCounts[countryTag] = 0;

        // Pass 2: Validate foreign keys (prerequisites, mutuallyExclusive, conditions, effects) and compile
        for (short i = 0; i < dto.Focuses.Count; i++)
        {
            FocusNodeDto nodeDto = dto.Focuses[i];
            FocusNodeData compiled = FocusNodeData.Create(
                countryTag: countryTag,
                focusIndex: i,
                costDays: nodeDto.CostDays,
                gridX: nodeDto.GridX,
                gridY: nodeDto.GridY,
                aiBaseWeight: nodeDto.AiBaseWeight,
                focusIdHash: FocusNodeData.ComputeIdHash(nodeDto.Id));

            // Prerequisites
            if (nodeDto.Prerequisites != null)
            {
                if (nodeDto.Prerequisites.Count > FocusNodeData.MaxPrerequisites)
                {
                    throw new MapValidationException(
                        $"Focus '{nodeDto.Id}' has {nodeDto.Prerequisites.Count} prerequisites (max {FocusNodeData.MaxPrerequisites}).");
                }

                foreach (string prereqId in nodeDto.Prerequisites)
                {
                    if (!indexById.TryGetValue(prereqId, out short prereqIndex))
                    {
                        throw new MapValidationException(
                            $"Focus '{nodeDto.Id}' references unknown prerequisite focus id '{prereqId}'.");
                    }

                    if (prereqIndex == i)
                    {
                        throw new MapValidationException(
                            $"Focus '{nodeDto.Id}' cannot list itself as a prerequisite.");
                    }

                    compiled.AddPrerequisite(prereqIndex);
                }
            }

            // Mutually Exclusive
            if (nodeDto.MutuallyExclusive != null)
            {
                if (nodeDto.MutuallyExclusive.Count > FocusNodeData.MaxMutuallyExclusive)
                {
                    throw new MapValidationException(
                        $"Focus '{nodeDto.Id}' has {nodeDto.MutuallyExclusive.Count} mutuallyExclusive entries (max {FocusNodeData.MaxMutuallyExclusive}).");
                }

                foreach (string exclId in nodeDto.MutuallyExclusive)
                {
                    if (!indexById.TryGetValue(exclId, out short exclIndex))
                    {
                        throw new MapValidationException(
                            $"Focus '{nodeDto.Id}' references unknown mutuallyExclusive focus id '{exclId}'.");
                    }

                    if (exclIndex == i)
                    {
                        throw new MapValidationException(
                            $"Focus '{nodeDto.Id}' cannot be mutually exclusive with itself.");
                    }

                    compiled.AddMutuallyExclusive(exclIndex);
                }
            }

            // Conditions
            if (nodeDto.Conditions != null)
            {
                if (nodeDto.Conditions.Count > FocusNodeData.MaxConditions)
                {
                    throw new MapValidationException(
                        $"Focus '{nodeDto.Id}' has {nodeDto.Conditions.Count} conditions (max {FocusNodeData.MaxConditions}).");
                }

                foreach (FocusConditionDto condDto in nodeDto.Conditions)
                {
                    FocusCondition compiledCond = CompileCondition(nodeDto.Id, condDto, indexById);
                    compiled.AddCondition(in compiledCond);
                }
            }

            // Effects
            if (nodeDto.Effects != null)
            {
                if (nodeDto.Effects.Count > FocusNodeData.MaxEffects)
                {
                    throw new MapValidationException(
                        $"Focus '{nodeDto.Id}' has {nodeDto.Effects.Count} effects (max {FocusNodeData.MaxEffects}).");
                }

                foreach (FocusEffectDto effDto in nodeDto.Effects)
                {
                    FocusEffect compiledEff = CompileEffect(nodeDto.Id, effDto);
                    compiled.AddEffect(in compiledEff);
                }
            }

            world.RegisterFocusNode(in compiled);
        }

        return new LoadedFocusTree
        {
            CountryTag = countryTag,
            CountryCode = CountryTag.GetCode(countryTag),
            Era = dto.Era ?? "1985_LateColdWar",
            Focuses = dto.Focuses,
            FocusIndexById = indexById
        };
    }

    private static FocusCondition CompileCondition(
        string focusId,
        FocusConditionDto dto,
        Dictionary<string, short> indexById)
    {
        if (string.IsNullOrWhiteSpace(dto.Type) ||
            !Enum.TryParse<ConditionType>(dto.Type, ignoreCase: true, out ConditionType condType) ||
            condType == ConditionType.None)
        {
            throw new MapValidationException(
                $"Focus '{focusId}' has invalid or unsupported condition type '{dto.Type}'.");
        }

        switch (condType)
        {
            case ConditionType.DateAfter:
            case ConditionType.DateBefore:
            {
                int dateInt = ParseDateStringToInt(focusId, dto.Target, dto.Value);
                return new FocusCondition
                {
                    Type = condType,
                    TargetTag = CountryTag.None,
                    IntParam = dateInt,
                    Value = dto.Value
                };
            }

            case ConditionType.AtWar:
            case ConditionType.AtPeace:
            case ConditionType.StabilityGreaterThan:
            case ConditionType.WarSupportGreaterThan:
            case ConditionType.DefconAtMost:
            {
                byte targetTag = ParseOptionalCountryTarget(focusId, dto.Target);
                return new FocusCondition
                {
                    Type = condType,
                    TargetTag = targetTag,
                    Value = dto.Value
                };
            }

            case ConditionType.HomelandEscalationGreaterThan:
            {
                byte targetTag = string.IsNullOrWhiteSpace(dto.Target) ||
                                 dto.Target.Equals("SELF", StringComparison.OrdinalIgnoreCase) ||
                                 dto.Target.Equals("USA", StringComparison.OrdinalIgnoreCase)
                    ? CountryTag.USA
                    : ParseOptionalCountryTarget(focusId, dto.Target);

                return new FocusCondition
                {
                    Type = condType,
                    TargetTag = targetTag,
                    Value = dto.Value
                };
            }

            case ConditionType.ControlsState:
            case ConditionType.OwnsState:
            {
                ushort stateId = ParseStateTarget(focusId, dto.Target, allowDefaultFallback: false);
                return new FocusCondition
                {
                    Type = condType,
                    TargetTag = CountryTag.None,
                    TargetId = stateId,
                    Value = dto.Value
                };
            }

            case ConditionType.HasCompletedFocus:
            {
                if (string.IsNullOrWhiteSpace(dto.Target))
                {
                    throw new MapValidationException(
                        $"Focus '{focusId}' condition HasCompletedFocus is missing a target focus id.");
                }

                if (indexById.TryGetValue(dto.Target, out short refFocusIdx))
                {
                    return FocusCondition.CreateHasCompletedFocus((ushort)refFocusIdx);
                }

                if (ushort.TryParse(dto.Target, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort numericIdx) &&
                    numericIdx < SimulationWorld.MaxFocusesPerCountry)
                {
                    return FocusCondition.CreateHasCompletedFocus(numericIdx);
                }

                throw new MapValidationException(
                    $"Focus '{focusId}' condition HasCompletedFocus references unknown focus '{dto.Target}'.");
            }

            default:
                throw new MapValidationException($"Unhandled condition type '{condType}' in focus '{focusId}'.");
        }
    }

    private static FocusEffect CompileEffect(string focusId, FocusEffectDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Type) ||
            !Enum.TryParse<EffectType>(dto.Type, ignoreCase: true, out EffectType effType) ||
            effType == EffectType.None)
        {
            throw new MapValidationException(
                $"Focus '{focusId}' has invalid or unsupported effect type '{dto.Type}'.");
        }

        switch (effType)
        {
            case EffectType.AddPoliticalPower:
            case EffectType.AddStability:
            case EffectType.AddWarSupport:
            case EffectType.ModifyConsumerGoods:
            {
                byte targetTag = ParseOptionalCountryTarget(focusId, dto.Target);
                return new FocusEffect
                {
                    Type = effType,
                    TargetTag = targetTag,
                    Value = dto.Value
                };
            }

            case EffectType.AddCivilianFactories:
            case EffectType.AddMilitaryFactories:
            case EffectType.AddNavalDockyards:
            case EffectType.AddRadarSamLevel:
            {
                ushort stateId = ParseStateTarget(focusId, dto.Target, allowDefaultFallback: true);
                return new FocusEffect
                {
                    Type = effType,
                    TargetTag = CountryTag.None,
                    TargetId = stateId,
                    Value = dto.Value
                };
            }

            case EffectType.AddEquipmentStockpile:
            {
                ParseEquipmentTarget(focusId, dto.Target, allowAll: false, out ushort archetypeId, out int genParam);
                return new FocusEffect
                {
                    Type = EffectType.AddEquipmentStockpile,
                    TargetTag = CountryTag.None,
                    TargetId = archetypeId,
                    IntParam = genParam,
                    Value = dto.Value
                };
            }

            case EffectType.SpawnDivisionTemplate:
            {
                ParseDivisionTemplateTarget(focusId, dto.Target, out ushort templateId, out int spawnProvId);
                return new FocusEffect
                {
                    Type = EffectType.SpawnDivisionTemplate,
                    TargetTag = CountryTag.None,
                    TargetId = templateId,
                    IntParam = spawnProvId,
                    Value = dto.Value <= 0 ? 1.0f : dto.Value
                };
            }

            case EffectType.GrantTechBonus:
            {
                ParseEquipmentTarget(focusId, dto.Target, allowAll: true, out ushort archetypeId, out int genParam);
                return new FocusEffect
                {
                    Type = EffectType.GrantTechBonus,
                    TargetTag = CountryTag.None,
                    TargetId = archetypeId,
                    IntParam = genParam,
                    Value = dto.Value <= 0 ? 1.0f : dto.Value
                };
            }

            case EffectType.CreateWargoal:
            {
                if (string.IsNullOrWhiteSpace(dto.Target) || !CountryTag.TryParse(dto.Target, out byte enemyTag))
                {
                    throw new MapValidationException(
                        $"Focus '{focusId}' effect CreateWargoal requires a valid target country tag, got '{dto.Target}'.");
                }

                return new FocusEffect
                {
                    Type = EffectType.CreateWargoal,
                    TargetTag = enemyTag,
                    Value = dto.Value
                };
            }

            case EffectType.AddHomelandEscalation:
            {
                byte targetTag = string.IsNullOrWhiteSpace(dto.Target) ||
                                 dto.Target.Equals("SELF", StringComparison.OrdinalIgnoreCase) ||
                                 dto.Target.Equals("USA", StringComparison.OrdinalIgnoreCase)
                    ? CountryTag.USA
                    : ParseOptionalCountryTarget(focusId, dto.Target);

                return new FocusEffect
                {
                    Type = EffectType.AddHomelandEscalation,
                    TargetTag = targetTag,
                    Value = dto.Value
                };
            }

            case EffectType.ShiftDefcon:
            {
                byte targetTag = ParseOptionalCountryTarget(focusId, dto.Target);
                int isRelative = dto.Value <= 0.0f ? 1 : 0;
                return new FocusEffect
                {
                    Type = EffectType.ShiftDefcon,
                    TargetTag = targetTag,
                    IntParam = isRelative,
                    Value = dto.Value
                };
            }

            case EffectType.TriggerNewsEvent:
            {
                string eventKey = string.IsNullOrWhiteSpace(dto.Target) ? focusId : dto.Target;
                int eventHash = FocusNodeData.ComputeIdHash(eventKey);
                return new FocusEffect
                {
                    Type = EffectType.TriggerNewsEvent,
                    TargetTag = CountryTag.None,
                    IntParam = eventHash,
                    Value = dto.Value
                };
            }

            default:
                throw new MapValidationException($"Unhandled effect type '{effType}' in focus '{focusId}'.");
        }
    }

    public static int ParseDateStringToInt(string focusId, string? targetDateStr, float fallbackNumericDate = 0f)
    {
        if (!string.IsNullOrWhiteSpace(targetDateStr))
        {
            ReadOnlySpan<char> span = targetDateStr.AsSpan().Trim();
            // Expected format: "YYYY.MM.DD" or "YYYY-MM-DD" or "YYYY/MM/DD"
            if (span.Length == 10 &&
                (span[4] == '.' || span[4] == '-' || span[4] == '/') &&
                (span[7] == '.' || span[7] == '-' || span[7] == '/') &&
                int.TryParse(span.Slice(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year) &&
                int.TryParse(span.Slice(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int month) &&
                int.TryParse(span.Slice(8, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int day) &&
                year is >= 1900 and <= 2100 &&
                month is >= 1 and <= 12 &&
                day is >= 1 and <= 31)
            {
                return (year * 10000) + (month * 100) + day;
            }

            if (int.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out int directInt) &&
                directInt is >= 19000101 and <= 21001231)
            {
                return directInt;
            }

            throw new MapValidationException(
                $"Focus '{focusId}' has invalid date string '{targetDateStr}'. Expected 'YYYY.MM.DD'.");
        }

        int fallbackInt = (int)fallbackNumericDate;
        if (fallbackInt is >= 19000101 and <= 21001231)
        {
            return fallbackInt;
        }

        throw new MapValidationException(
            $"Focus '{focusId}' date condition requires a valid 'YYYY.MM.DD' target string.");
    }

    private static byte ParseOptionalCountryTarget(string focusId, string? target)
    {
        if (string.IsNullOrWhiteSpace(target) ||
            target.Equals("SELF", StringComparison.OrdinalIgnoreCase))
        {
            return CountryTag.None;
        }

        if (CountryTag.TryParse(target, out byte parsedTag))
        {
            return parsedTag;
        }

        throw new MapValidationException(
            $"Focus '{focusId}' references invalid country tag target '{target}'.");
    }

    private static ushort ParseStateTarget(string focusId, string? target, bool allowDefaultFallback)
    {
        if (string.IsNullOrWhiteSpace(target) ||
            target.Equals("SELF", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("CAPITAL", StringComparison.OrdinalIgnoreCase))
        {
            if (allowDefaultFallback)
            {
                return FocusEffect.DefaultStateFallback;
            }
            throw new MapValidationException($"Focus '{focusId}' requires an explicit state target.");
        }

        if (ushort.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort stateId))
        {
            if (stateId >= SimulationWorld.MaxStates)
            {
                throw new MapValidationException(
                    $"Focus '{focusId}' references StateId {stateId} exceeding MaxStates ({SimulationWorld.MaxStates}).");
            }
            return stateId;
        }

        if (target.StartsWith("STATE_", StringComparison.OrdinalIgnoreCase))
        {
            ReadOnlySpan<char> suffix = target.AsSpan(6);
            if (ushort.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort prefixedId))
            {
                if (prefixedId >= SimulationWorld.MaxStates)
                {
                    throw new MapValidationException(
                        $"Focus '{focusId}' references StateId {prefixedId} exceeding MaxStates ({SimulationWorld.MaxStates}).");
                }
                return prefixedId;
            }
        }

        if (SymbolicStateAliases.TryGetValue(target, out ushort aliasStateId))
        {
            return aliasStateId;
        }

        throw new MapValidationException($"Focus '{focusId}' references unknown state target '{target}'.");
    }

    private static void ParseEquipmentTarget(
        string focusId,
        string? target,
        bool allowAll,
        out ushort archetypeId,
        out int genParam)
    {
        genParam = -1;

        if (string.IsNullOrWhiteSpace(target) ||
            target.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("SELF", StringComparison.OrdinalIgnoreCase))
        {
            if (allowAll)
            {
                archetypeId = FocusEffect.AllArchetypesTarget;
                return;
            }
            throw new MapValidationException($"Focus '{focusId}' requires an EquipmentType target.");
        }

        string archToken = target;
        int colonIdx = target.IndexOf(':');
        if (colonIdx > 0)
        {
            archToken = target[..colonIdx];
            string genToken = target[(colonIdx + 1)..];
            if (Enum.TryParse<TechGeneration>(genToken, ignoreCase: true, out TechGeneration parsedGen))
            {
                genParam = (int)parsedGen;
            }
            else if (genToken.Equals("Gen0", StringComparison.OrdinalIgnoreCase) || genToken == "0")
            {
                genParam = (int)TechGeneration.Gen0_1970Reserve;
            }
            else if (genToken.Equals("Gen1", StringComparison.OrdinalIgnoreCase) || genToken == "1")
            {
                genParam = (int)TechGeneration.Gen1_1980Baseline;
            }
            else if (genToken.Equals("Gen2", StringComparison.OrdinalIgnoreCase) || genToken == "2")
            {
                genParam = (int)TechGeneration.Gen2_1988LateColdWar;
            }
            else if (genToken.Equals("Gen3", StringComparison.OrdinalIgnoreCase) || genToken == "3")
            {
                genParam = (int)TechGeneration.Gen3_1995DigitalStealth;
            }
            else
            {
                throw new MapValidationException(
                    $"Focus '{focusId}' has invalid TechGeneration specifier '{genToken}' in target '{target}'.");
            }
        }

        if (Enum.TryParse<EquipmentType>(archToken, ignoreCase: true, out EquipmentType eqType))
        {
            archetypeId = (ushort)eqType;
            return;
        }

        if (allowAll && archToken.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            archetypeId = FocusEffect.AllArchetypesTarget;
            return;
        }

        throw new MapValidationException(
            $"Focus '{focusId}' references unknown EquipmentType '{archToken}'.");
    }

    private static void ParseDivisionTemplateTarget(
        string focusId,
        string? target,
        out ushort templateId,
        out int spawnProvinceId)
    {
        spawnProvinceId = -1;

        if (string.IsNullOrWhiteSpace(target) ||
            target.Equals("SELF", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("Mechanized", StringComparison.OrdinalIgnoreCase))
        {
            templateId = DivisionTemplateRecord.DefaultMechanizedTemplateId;
            return;
        }

        string tplToken = target;
        int atIdx = target.IndexOf('@');
        if (atIdx > 0)
        {
            tplToken = target[..atIdx];
            string provToken = target[(atIdx + 1)..];
            if (!int.TryParse(provToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out spawnProvinceId) ||
                spawnProvinceId < 0 ||
                spawnProvinceId >= SimulationWorld.MaxProvinces)
            {
                throw new MapValidationException(
                    $"Focus '{focusId}' has invalid spawn province '{provToken}' in target '{target}'.");
            }
        }

        if (ushort.TryParse(tplToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort parsedTplId) &&
            parsedTplId > 0 &&
            parsedTplId < SimulationWorld.MaxDivisionTemplates)
        {
            templateId = parsedTplId;
            return;
        }

        if (tplToken.Equals("Mechanized", StringComparison.OrdinalIgnoreCase))
        {
            templateId = DivisionTemplateRecord.DefaultMechanizedTemplateId;
            return;
        }
        if (tplToken.Equals("Infantry", StringComparison.OrdinalIgnoreCase))
        {
            templateId = DivisionTemplateRecord.DefaultInfantryTemplateId;
            return;
        }
        if (tplToken.Equals("Armored", StringComparison.OrdinalIgnoreCase))
        {
            templateId = DivisionTemplateRecord.DefaultArmoredTemplateId;
            return;
        }
        if (tplToken.Equals("AirAssault", StringComparison.OrdinalIgnoreCase))
        {
            templateId = DivisionTemplateRecord.DefaultAirAssaultTemplateId;
            return;
        }

        throw new MapValidationException(
            $"Focus '{focusId}' has invalid division template target '{target}'.");
    }

    private static void CheckCycleDfs(
        int current,
        List<FocusNodeDto> focuses,
        Dictionary<string, short> indexById,
        Span<byte> visitState)
    {
        visitState[current] = 1; // visiting
        List<string>? prereqs = focuses[current].Prerequisites;
        if (prereqs != null)
        {
            for (int p = 0; p < prereqs.Count; p++)
            {
                if (indexById.TryGetValue(prereqs[p], out short nextIdx))
                {
                    if (visitState[nextIdx] == 1)
                    {
                        throw new MapValidationException(
                            $"Prerequisite cycle detected involving focus '{focuses[current].Id}' and '{prereqs[p]}'.");
                    }
                    if (visitState[nextIdx] == 0)
                    {
                        CheckCycleDfs(nextIdx, focuses, indexById, visitState);
                    }
                }
            }
        }
        visitState[current] = 2; // visited
    }
}
