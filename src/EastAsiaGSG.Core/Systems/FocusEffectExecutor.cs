using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Deterministic, zero-allocation executor for all 15 National Focus EffectType actions defined in DATA_SCHEMAS.md.
/// </summary>
public static class FocusEffectExecutor
{
    private static readonly QueryDescription ProductionLineQuery = new QueryDescription().WithAll<ProductionLineComponent>();

    /// <summary>
    /// Executes all configured effects on a completed FocusNodeData for the specified country.
    /// </summary>
    public static void ExecuteFocusEffects(SimulationWorld world, byte countryTag, in FocusNodeData focus)
    {
        if (countryTag >= SimulationWorld.MaxCountries || focus.IsConfigured == 0)
        {
            return;
        }

        int count = focus.EffectCount;
        for (int i = 0; i < count; i++)
        {
            FocusEffect effect = focus.GetEffect(i);
            ExecuteEffect(world, countryTag, in effect);
        }
    }

    /// <summary>
    /// Executes a single FocusEffect action against SimulationWorld with zero heap allocations.
    /// </summary>
    public static void ExecuteEffect(SimulationWorld world, byte countryTag, in FocusEffect effect)
    {
        if (countryTag >= SimulationWorld.MaxCountries)
        {
            return;
        }

        byte targetCountry = ResolveCountryTag(countryTag, effect.TargetTag);

        switch (effect.Type)
        {
            case EffectType.AddPoliticalPower:
            {
                world.Countries[targetCountry].PoliticalPower += effect.Value;
                break;
            }
            case EffectType.AddStability:
            {
                ref CountryState country = ref world.Countries[targetCountry];
                float delta = NormalizeRatioDelta(effect.Value);
                country.Stability = System.Math.Clamp(country.Stability + delta, 0.0f, 1.0f);
                break;
            }
            case EffectType.AddWarSupport:
            {
                ref CountryState country = ref world.Countries[targetCountry];
                float delta = NormalizeRatioDelta(effect.Value);
                country.WarSupport = System.Math.Clamp(country.WarSupport + delta, 0.0f, 1.0f);
                break;
            }
            case EffectType.ModifyConsumerGoods:
            {
                ref CountryState country = ref world.Countries[targetCountry];
                float delta = NormalizeRatioDelta(effect.Value);
                country.ConsumerGoodsRatio = System.Math.Clamp(country.ConsumerGoodsRatio + delta, 0.05f, 0.80f);
                break;
            }
            case EffectType.AddCivilianFactories:
            {
                if (TryResolveTargetState(world, targetCountry, effect.TargetId, out ushort stateId))
                {
                    ref StateData state = ref world.States[stateId];
                    int updated = state.CivilianFactories + (int)System.MathF.Round(effect.Value);
                    state.CivilianFactories = (byte)System.Math.Clamp(updated, 0, byte.MaxValue);
                }
                break;
            }
            case EffectType.AddMilitaryFactories:
            {
                if (TryResolveTargetState(world, targetCountry, effect.TargetId, out ushort stateId))
                {
                    ref StateData state = ref world.States[stateId];
                    int updated = state.MilitaryFactories + (int)System.MathF.Round(effect.Value);
                    state.MilitaryFactories = (byte)System.Math.Clamp(updated, 0, byte.MaxValue);
                }
                break;
            }
            case EffectType.AddNavalDockyards:
            {
                if (TryResolveTargetState(world, targetCountry, effect.TargetId, out ushort stateId))
                {
                    ref StateData state = ref world.States[stateId];
                    int updated = state.NavalDockyards + (int)System.MathF.Round(effect.Value);
                    state.NavalDockyards = (byte)System.Math.Clamp(updated, 0, byte.MaxValue);
                }
                break;
            }
            case EffectType.AddRadarSamLevel:
            {
                if (TryResolveTargetState(world, targetCountry, effect.TargetId, out ushort stateId))
                {
                    ref StateData state = ref world.States[stateId];
                    int updated = state.RadarSamLevel + (int)System.MathF.Round(effect.Value);
                    state.RadarSamLevel = (byte)System.Math.Clamp(updated, 0, ConstructionSystem.MaxProvincialFacilityLevel);
                }
                break;
            }
            case EffectType.AddEquipmentStockpile:
            {
                EquipmentType archetype = effect.TargetId < CountryState.ArchetypeCount
                    ? (EquipmentType)effect.TargetId
                    : EquipmentType.InfantryKit;

                TechGeneration gen = effect.IntParam is >= 0 and < CountryState.TechGenerationCount
                    ? (TechGeneration)effect.IntParam
                    : world.GetUnlockedTechGeneration(targetCountry, archetype);

                int delta = (int)System.MathF.Round(effect.Value);
                world.Countries[targetCountry].AddStockpile(archetype, gen, delta);
                break;
            }
            case EffectType.SpawnDivisionTemplate:
            {
                ExecuteSpawnDivisionTemplate(world, targetCountry, in effect);
                break;
            }
            case EffectType.GrantTechBonus:
            {
                ExecuteGrantTechBonus(world, targetCountry, in effect);
                break;
            }
            case EffectType.CreateWargoal:
            {
                byte enemyTag = effect.TargetTag < SimulationWorld.MaxCountries
                    ? effect.TargetTag
                    : (effect.TargetId < SimulationWorld.MaxCountries ? (byte)effect.TargetId : CountryTag.None);

                if (enemyTag < SimulationWorld.MaxCountries && enemyTag != countryTag)
                {
                    world.GrantWargoal(countryTag, enemyTag);
                    if (effect.Value >= 1.0f)
                    {
                        world.DeclareWar(countryTag, enemyTag);
                    }
                }
                break;
            }
            case EffectType.AddHomelandEscalation:
            {
                byte escTag = effect.TargetTag < SimulationWorld.MaxCountries ? effect.TargetTag : CountryTag.USA;
                float currentEsc = world.CountryHomelandEscalation[escTag];
                float updatedEsc = System.Math.Clamp(
                    currentEsc + effect.Value,
                    SuperpowerEscalationSystem.MinEscalation,
                    SuperpowerEscalationSystem.MaxEscalation);

                world.CountryHomelandEscalation[escTag] = updatedEsc;
                byte finalEsc = (byte)System.Math.Clamp(
                    (int)System.MathF.Round(updatedEsc),
                    SuperpowerEscalationSystem.MinEscalation,
                    SuperpowerEscalationSystem.MaxEscalation);
                world.Countries[escTag].HomelandEscalation = finalEsc;

                if (escTag == CountryTag.USA)
                {
                    ref CountryState usa = ref world.Countries[CountryTag.USA];
                    usa.Defcon = SuperpowerEscalationSystem.GetDefconForEscalation(finalEsc);
                    usa.OffMapMilitaryFactories = SuperpowerEscalationSystem.CalculateOffMapMilitaryFactories(finalEsc);
                }
                break;
            }
            case EffectType.ShiftDefcon:
            {
                ref CountryState country = ref world.Countries[targetCountry];
                int rounded = (int)System.MathF.Round(effect.Value);
                byte newDefcon;
                if (effect.IntParam != 0 || rounded <= 0)
                {
                    newDefcon = (byte)System.Math.Clamp(country.Defcon + rounded, 1, 5);
                }
                else
                {
                    newDefcon = (byte)System.Math.Clamp(rounded, 1, 5);
                }

                country.Defcon = newDefcon;
                if (targetCountry == CountryTag.USA)
                {
                    byte minEsc = SuperpowerEscalationSystem.GetMinEscalationForDefcon(newDefcon);
                    if (world.CountryHomelandEscalation[CountryTag.USA] < minEsc)
                    {
                        world.CountryHomelandEscalation[CountryTag.USA] = minEsc;
                        country.HomelandEscalation = minEsc;
                        country.OffMapMilitaryFactories = SuperpowerEscalationSystem.CalculateOffMapMilitaryFactories(minEsc);
                    }
                }
                break;
            }
            case EffectType.TriggerNewsEvent:
            {
                int eventHash = effect.IntParam != 0 ? effect.IntParam : effect.TargetId;
                world.RecordNewsEvent(eventHash, countryTag);
                break;
            }
            case EffectType.None:
            default:
                break;
        }
    }

    private static void ExecuteSpawnDivisionTemplate(SimulationWorld world, byte countryTag, in FocusEffect effect)
    {
        ushort templateId = effect.TargetId == 0
            ? DivisionTemplateRecord.DefaultMechanizedTemplateId
            : effect.TargetId;
        if (templateId >= SimulationWorld.MaxDivisionTemplates)
        {
            templateId = DivisionTemplateRecord.DefaultMechanizedTemplateId;
        }

        if (world.Templates[templateId].IsConfigured == 0)
        {
            DivisionTemplateRecord fallback = templateId switch
            {
                DivisionTemplateRecord.DefaultInfantryTemplateId => DivisionTemplateRecord.CreateInfantryDivision(templateId),
                DivisionTemplateRecord.DefaultArmoredTemplateId => DivisionTemplateRecord.CreateArmoredDivision(templateId),
                DivisionTemplateRecord.DefaultAirAssaultTemplateId => DivisionTemplateRecord.CreateAirAssaultDivision(templateId),
                _ => DivisionTemplateRecord.CreateMechanizedDivision(templateId)
            };
            world.RegisterDivisionTemplate(in fallback);
        }

        ref readonly DivisionTemplateRecord tpl = ref world.Templates[templateId];
        ushort spawnProvinceId = ResolveSpawnProvinceId(world, countryTag, effect.IntParam);
        int spawnCount = System.Math.Clamp((int)System.MathF.Round(effect.Value), 1, 32);

        // Derive combat stats from template TOE composition
        ushort infReq = tpl.GetRequired(EquipmentType.InfantryKit);
        ushort ifvReq = tpl.GetRequired(EquipmentType.IFV);
        ushort mbtReq = tpl.GetRequired(EquipmentType.MBT);
        ushort artReq = tpl.GetRequired(EquipmentType.Artillery);
        ushort heliReq = tpl.GetRequired(EquipmentType.AttackHeli);

        float softAttack = 25.0f + (infReq * 0.05f) + (ifvReq * 0.12f) + (artReq * 0.45f) + (heliReq * 0.35f);
        float hardAttack = 10.0f + (ifvReq * 0.10f) + (mbtReq * 0.35f) + (heliReq * 0.50f);
        float defense = 35.0f + (infReq * 0.06f) + (ifvReq * 0.10f) + (artReq * 0.15f);
        float breakthrough = 20.0f + (ifvReq * 0.12f) + (mbtReq * 0.35f) + (heliReq * 0.30f);
        float armor = mbtReq > 0 ? 45.0f + (mbtReq * 0.15f) : (ifvReq > 0 ? 22.0f : 5.0f);
        float piercing = mbtReq > 0 ? 55.0f : (ifvReq > 0 || heliReq > 0 ? 38.0f : 22.0f);
        float hardness = mbtReq >= 80 ? 0.65f : (ifvReq >= 80 ? 0.45f : 0.15f);
        float speed = heliReq > 0 ? 42.0f : (ifvReq > 0 || mbtReq > 0 ? 32.0f : 22.0f);

        for (int i = 0; i < spawnCount; i++)
        {
            int divId = world.AllocateNextDivisionId();
            DivisionComponent div = DivisionComponent.Create(
                divisionId: divId,
                countryTag: countryTag,
                startingProvinceId: spawnProvinceId,
                templateId: templateId,
                combatWidth: tpl.CombatWidth,
                softAttack: softAttack,
                hardAttack: hardAttack,
                defense: defense,
                breakthrough: breakthrough,
                armor: armor,
                piercing: piercing,
                hardness: hardness,
                maxOrganization: 60.0f,
                startingStrength: 1.0f,
                startingExperience: 0.45f,
                speed: speed,
                pgmAuthorized: mbtReq > 0 || ifvReq > 0 || heliReq > 0);
            world.SpawnDivision(in div);
        }
    }

    private static void ExecuteGrantTechBonus(SimulationWorld world, byte countryTag, in FocusEffect effect)
    {
        int startArch = 0;
        int endArch = CountryState.ArchetypeCount - 1;
        if (effect.TargetId < CountryState.ArchetypeCount)
        {
            startArch = effect.TargetId;
            endArch = effect.TargetId;
        }

        byte bonusSteps = (byte)System.Math.Clamp((int)System.MathF.Round(System.MathF.Max(1.0f, effect.Value)), 1, 4);
        float capBoost = effect.Value is > 0.0f and < 1.0f ? effect.Value : 0.10f;

        for (int a = startArch; a <= endArch; a++)
        {
            EquipmentType archetype = (EquipmentType)a;
            world.IncrementTechBonusCount(countryTag, archetype, bonusSteps);

            TechGeneration currentGen = world.GetUnlockedTechGeneration(countryTag, archetype);
            TechGeneration nextGen;
            if (effect.IntParam is >= 0 and < CountryState.TechGenerationCount)
            {
                nextGen = (TechGeneration)System.Math.Max((int)currentGen, effect.IntParam);
            }
            else
            {
                nextGen = (TechGeneration)System.Math.Min((int)currentGen + 1, (int)TechGeneration.Gen3_1995DigitalStealth);
            }

            world.SetUnlockedTechGeneration(countryTag, archetype, nextGen);
        }

        // Apply efficiency cap bonus and generation upgrade to matching active production lines
        Query query = world.EcsWorld.Query(in ProductionLineQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<ProductionLineComponent> lines = chunk.GetSpan<ProductionLineComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                ref ProductionLineComponent line = ref lines[i];
                if (line.CountryTag != countryTag)
                {
                    continue;
                }

                int archIdx = (int)line.Archetype;
                if (archIdx < startArch || archIdx > endArch)
                {
                    continue;
                }

                line.EfficiencyCap = System.Math.Clamp(
                    line.EfficiencyCap + capBoost,
                    ProductionLineComponent.MinEfficiency,
                    ProductionLineComponent.MaxEfficiencyCap);

                TechGeneration unlockedGen = world.GetUnlockedTechGeneration(countryTag, line.Archetype);
                if (unlockedGen > line.Generation)
                {
                    ProductionSystem.SwitchLineEquipment(ref line, line.Archetype, unlockedGen);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte ResolveCountryTag(byte selfTag, byte effectOrConditionTargetTag)
    {
        return effectOrConditionTargetTag < SimulationWorld.MaxCountries
            ? effectOrConditionTargetTag
            : selfTag;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float NormalizeRatioDelta(float raw)
    {
        return System.MathF.Abs(raw) > 1.0f ? raw / 100.0f : raw;
    }

    internal static bool TryResolveTargetState(
        SimulationWorld world,
        byte countryTag,
        ushort requestedStateId,
        out ushort resolvedStateId)
    {
        if (requestedStateId < SimulationWorld.MaxStates)
        {
            resolvedStateId = requestedStateId;
            return true;
        }

        // Fallback 1: State containing the country's capital province
        ushort capitalProv = world.GetCapitalProvince(countryTag);
        if (capitalProv < SimulationWorld.MaxProvinces)
        {
            ushort capState = world.Provinces[capitalProv].StateId;
            if (capState < SimulationWorld.MaxStates)
            {
                resolvedStateId = capState;
                return true;
            }
        }

        // Fallback 2: First controlled or owned state
        ReadOnlySpan<StateData> states = world.States.AsSpan();
        for (ushort s = 0; s < states.Length; s++)
        {
            if (states[s].ControllerTag == countryTag || states[s].OwnerTag == countryTag)
            {
                resolvedStateId = s;
                return true;
            }
        }

        resolvedStateId = 0;
        return true;
    }

    private static ushort ResolveSpawnProvinceId(SimulationWorld world, byte countryTag, int requestedProvinceId)
    {
        if (requestedProvinceId >= 0 && requestedProvinceId < SimulationWorld.MaxProvinces)
        {
            return (ushort)requestedProvinceId;
        }

        ushort capitalProv = world.GetCapitalProvince(countryTag);
        if (capitalProv < SimulationWorld.MaxProvinces)
        {
            return capitalProv;
        }

        ReadOnlySpan<ProvinceData> provinces = world.Provinces.AsSpan();
        for (ushort p = 0; p < provinces.Length; p++)
        {
            if (provinces[p].ControllerTag == countryTag || provinces[p].OwnerTag == countryTag)
            {
                return p;
            }
        }

        return 0;
    }
}
