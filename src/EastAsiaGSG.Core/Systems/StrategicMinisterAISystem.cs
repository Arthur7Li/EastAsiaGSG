using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Tier 3 (Monthly on the 1st at 00:00) Strategic Minister Utility AI.
/// Evaluates geopolitical, industrial, and military utility scores for AI-controlled countries to:
/// 1. Select the highest-utility available National Focus (`SelectFocusCommand`).
/// 2. Queue State building constructions using available Civilian Factories (`QueueConstructionCommand`).
/// 3. Allocate Military Factories across active Production Lines (`AssignProductionFactoriesCommand`).
/// Zero heap allocations in hot loop.
/// </summary>
public static class StrategicMinisterAISystem
{
    private static readonly QueryDescription ProductionLineQuery = new QueryDescription().WithAll<ProductionLineComponent>();

    /// <summary>
    /// Executes monthly strategic AI evaluation for all countries enabled in <see cref="SimulationWorld.AiControlledCountryMask"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Execute(SimulationWorld world)
    {
        ushort aiMask = world.AiControlledCountryMask;
        if (aiMask == 0)
        {
            return;
        }

        for (byte tag = 0; tag < CountryTag.ActiveCount; tag++)
        {
            if ((aiMask & (1 << tag)) == 0)
            {
                continue;
            }

            EvaluateCountry(world, tag);
        }
    }

    /// <summary>
    /// Evaluates and enqueues strategic focus, construction, and military production commands for a single country tag.
    /// Can be invoked directly by tests even if <see cref="SimulationWorld.AiControlledCountryMask"/> is not set.
    /// </summary>
    public static void EvaluateCountry(SimulationWorld world, byte countryTag)
    {
        if (countryTag >= CountryTag.ActiveCount)
        {
            return;
        }

        EvaluateAndSelectFocus(world, countryTag);
        EvaluateAndQueueConstruction(world, countryTag);
        EvaluateAndAllocateProduction(world, countryTag);
        EvaluateAndRecruitDivisions(world, countryTag);
        EvaluateAndExecuteWargoals(world, countryTag);
    }

    /// <summary>
    /// Scores all available National Focuses for <paramref name="countryTag"/> and enqueues <see cref="CommandType.SelectFocus"/>
    /// for the highest-utility node when no focus is currently active.
    /// </summary>
    public static unsafe short EvaluateAndSelectFocus(SimulationWorld world, byte countryTag)
    {
        ref readonly CountryState country = ref world.Countries[countryTag];
        if (country.ActiveFocusId >= 0)
        {
            return country.ActiveFocusId;
        }

        ushort focusCount = world.CountryFocusCounts[countryTag];
        if (focusCount == 0)
        {
            return -1;
        }

        bool atWar = world.IsAtWar(countryTag);
        byte defcon = country.Defcon is >= 1 and <= 5 ? country.Defcon : (byte)5;
        bool highTension = atWar || defcon <= 3 || country.WarSupport >= 0.70f;
        bool lowStability = country.Stability < 0.55f;

        short bestFocusIndex = -1;
        float bestUtilityScore = float.MinValue;
        short bestCostDays = FocusNodeData.DefaultCostDays;

        for (short i = 0; i < focusCount; i++)
        {
            if (!FocusTreeSystem.CanStartFocus(world, countryTag, i))
            {
                continue;
            }

            ref readonly FocusNodeData node = ref world.GetFocusNode(countryTag, i);
            float score = node.AiBaseWeight > 0 ? node.AiBaseWeight : 100.0f;

            // Prefer earlier tier focuses slightly to complete upper tree foundations first
            score -= node.GridY * 3.0f;

            // Evaluate completion effects to weight utility by strategic context
            for (int e = 0; e < node.EffectCount; e++)
            {
                EffectType effType = (EffectType)node.EffectTypes[e];
                float val = node.EffectValues[e];

                switch (effType)
                {
                    case EffectType.AddCivilianFactories:
                        score += highTension ? val * 8.0f : val * 16.0f;
                        break;
                    case EffectType.AddMilitaryFactories:
                        score += highTension ? val * 18.0f : val * 10.0f;
                        break;
                    case EffectType.AddNavalDockyards:
                        score += val * 9.0f;
                        break;
                    case EffectType.AddRadarSamLevel:
                        score += highTension ? val * 12.0f : val * 6.0f;
                        break;
                    case EffectType.AddEquipmentStockpile:
                        score += highTension ? 22.0f : 10.0f;
                        break;
                    case EffectType.SpawnDivisionTemplate:
                        score += highTension ? 35.0f : 14.0f;
                        break;
                    case EffectType.GrantTechBonus:
                        score += 18.0f;
                        break;
                    case EffectType.ModifyConsumerGoods:
                        if (val < 0.0f)
                        {
                            score += System.MathF.Abs(val) * 450.0f;
                        }
                        break;
                    case EffectType.AddStability:
                        score += lowStability ? val * 300.0f : val * 120.0f;
                        break;
                    case EffectType.AddWarSupport:
                        score += highTension ? val * 180.0f : val * 80.0f;
                        break;
                    case EffectType.AddPoliticalPower:
                        score += val * 0.15f;
                        break;
                    default:
                        break;
                }
            }

            if (score > bestUtilityScore)
            {
                bestUtilityScore = score;
                bestFocusIndex = i;
                bestCostDays = node.CostDays;
            }
        }

        if (bestFocusIndex >= 0)
        {
            world.EnqueueCommand(SimulationCommand.CreateSelectFocus(countryTag, bestFocusIndex, bestCostDays));
        }

        return bestFocusIndex;
    }

    /// <summary>
    /// Evaluates controlled states and queues building construction projects when the AI country has unassigned
    /// free Civilian Factories (<c>C_free</c>).
    /// </summary>
    public static void EvaluateAndQueueConstruction(SimulationWorld world, byte countryTag)
    {
        CountryFactorySummary summary = ConstructionSystem.CalculateFactorySummary(world, countryTag);
        if (summary.FreeCivilianFactories <= 0)
        {
            return;
        }

        ReadOnlySpan<StateData> states = world.States.AsSpan();
        int activeAssignedCiv = 0;

        for (ushort s = 0; s < states.Length; s++)
        {
            ref readonly StateData st = ref states[s];
            if (st.ControllerTag == countryTag &&
                st.ActiveBuildingType != BuildingType.None &&
                st.QueuedBuildingLevels > 0)
            {
                activeAssignedCiv += st.AssignedCivFactories;
            }
        }

        // If all free civilian factories are already committed to active state projects, nothing to queue
        int uncommittedCiv = summary.FreeCivilianFactories - activeAssignedCiv;
        if (uncommittedCiv <= 0)
        {
            return;
        }

        bool atWar = world.IsAtWar(countryTag);
        ref readonly CountryState country = ref world.Countries[countryTag];
        bool favorMilitary = atWar ||
                             country.WarSupport >= 0.65f ||
                             world.CurrentYear >= 1989 ||
                             summary.TotalMilitaryFactories < summary.TotalCivilianFactories;

        // Find up to 2 idle controlled states to start construction projects
        for (ushort s = 0; s < states.Length && uncommittedCiv > 0; s++)
        {
            ref readonly StateData st = ref states[s];
            if (st.ControllerTag != countryTag ||
                (st.Population <= 0 && st.CivilianFactories == 0 && st.MilitaryFactories == 0))
            {
                continue;
            }

            if (st.ActiveBuildingType != BuildingType.None && st.QueuedBuildingLevels > 0)
            {
                continue;
            }

            BuildingType chosenBuilding;
            if (st.InfrastructureLevel > 0 && st.InfrastructureLevel < 4)
            {
                chosenBuilding = BuildingType.Infrastructure;
            }
            else if (atWar && st.RadarSamLevel < 3)
            {
                chosenBuilding = BuildingType.RadarSam;
            }
            else if (favorMilitary && st.MilitaryFactories < 40)
            {
                chosenBuilding = BuildingType.MilitaryFactory;
            }
            else if (st.CivilianFactories < 45)
            {
                chosenBuilding = BuildingType.CivilianFactory;
            }
            else
            {
                chosenBuilding = BuildingType.MilitaryFactory;
            }

            ushort assignCiv = (ushort)System.Math.Min(uncommittedCiv, ConstructionSystem.MaxCivFactoriesPerState);
            world.EnqueueCommand(SimulationCommand.CreateQueueConstruction(
                countryTag: countryTag,
                stateId: s,
                buildingType: chosenBuilding,
                levels: 1,
                assignedCivFactories: assignCiv));

            uncommittedCiv -= assignCiv;
        }
    }

    /// <summary>
    /// Ensures the AI country has active Military Production Lines and distributes all available
    /// Military Factories across lines based on equipment stockpile needs.
    /// </summary>
    public static void EvaluateAndAllocateProduction(SimulationWorld world, byte countryTag)
    {
        CountryFactorySummary summary = ConstructionSystem.CalculateFactorySummary(world, countryTag);
        int totalMil = summary.TotalMilitaryFactories;
        if (totalMil <= 0)
        {
            return;
        }

        const int MaxCountryLines = 16;
        Span<int> lineIds = stackalloc int[MaxCountryLines];
        Span<ushort> assignedCounts = stackalloc ushort[MaxCountryLines];
        Span<EquipmentType> archetypes = stackalloc EquipmentType[MaxCountryLines];
        int countryLineCount = 0;
        int totalAssigned = 0;

        Query query = world.EcsWorld.Query(in ProductionLineQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<ProductionLineComponent> lines = chunk.GetSpan<ProductionLineComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                ref readonly ProductionLineComponent line = ref lines[i];
                if (line.CountryTag != countryTag)
                {
                    continue;
                }

                totalAssigned += line.AssignedMilitaryFactories;
                if (countryLineCount < MaxCountryLines)
                {
                    lineIds[countryLineCount] = line.LineId;
                    assignedCounts[countryLineCount] = line.AssignedMilitaryFactories;
                    archetypes[countryLineCount] = line.Archetype;
                    countryLineCount++;
                }
            }
        }

        // If the AI country has Military Factories but zero production lines, bootstrap default lines
        if (countryLineCount == 0)
        {
            BootstrapDefaultProductionLines(world, countryTag, totalMil);
            return;
        }

        if (totalAssigned == totalMil)
        {
            return;
        }

        // Score each line by equipment stockpile urgency to assign surplus (or trim excess) factories
        ref readonly CountryState country = ref world.Countries[countryTag];
        int bestLineSlot = 0;
        float bestNeedScore = float.MinValue;

        for (int i = 0; i < countryLineCount; i++)
        {
            EquipmentType arch = archetypes[i];
            int currentStock = country.GetTotalArchetypeStockpile(arch);
            float targetStock = arch switch
            {
                EquipmentType.InfantryKit => 3000.0f,
                EquipmentType.SupportVehicles => 600.0f,
                EquipmentType.IFV => 500.0f,
                EquipmentType.MBT => 400.0f,
                EquipmentType.Artillery => 500.0f,
                EquipmentType.AttackHeli => 120.0f,
                EquipmentType.TacticalAir => 180.0f,
                EquipmentType.PGM => 250.0f,
                _ => 500.0f
            };

            float deficitRatio = (targetStock - currentStock) / targetStock;
            float score = (deficitRatio * 100.0f) - (assignedCounts[i] * 1.5f);
            if (score > bestNeedScore)
            {
                bestNeedScore = score;
                bestLineSlot = i;
            }
        }

        int delta = totalMil - totalAssigned;
        int newAssigned = System.Math.Clamp(
            assignedCounts[bestLineSlot] + delta,
            1,
            ProductionLineComponent.MaxFactoriesPerLine);

        if (newAssigned != assignedCounts[bestLineSlot])
        {
            world.EnqueueCommand(SimulationCommand.CreateAssignProductionFactories(
                countryTag: countryTag,
                lineId: lineIds[bestLineSlot],
                militaryFactories: (ushort)newAssigned));
        }
    }

    private static void BootstrapDefaultProductionLines(SimulationWorld world, byte countryTag, int totalMil)
    {
        int baseLineId = (countryTag + 1) * 1000;
        int infFactories = System.Math.Max(1, (totalMil * 35) / 100);
        int artFactories = System.Math.Max(1, (totalMil * 25) / 100);
        int ifvFactories = System.Math.Max(0, (totalMil * 20) / 100);
        int mbtFactories = System.Math.Max(0, totalMil - infFactories - artFactories - ifvFactories);

        TechGeneration infGen = world.GetUnlockedTechGeneration(countryTag, EquipmentType.InfantryKit);
        TechGeneration artGen = world.GetUnlockedTechGeneration(countryTag, EquipmentType.Artillery);

        world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: baseLineId + 1,
            countryTag: countryTag,
            archetype: EquipmentType.InfantryKit,
            generation: infGen,
            assignedMilitaryFactories: (ushort)infFactories,
            priority: 200,
            initialEfficiency: 0.60f,
            efficiencyCap: 0.85f));

        world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: baseLineId + 2,
            countryTag: countryTag,
            archetype: EquipmentType.Artillery,
            generation: artGen,
            assignedMilitaryFactories: (ushort)artFactories,
            priority: 180,
            initialEfficiency: 0.55f,
            efficiencyCap: 0.85f));

        if (ifvFactories > 0)
        {
            TechGeneration ifvGen = world.GetUnlockedTechGeneration(countryTag, EquipmentType.IFV);
            world.SpawnProductionLine(ProductionLineComponent.Create(
                lineId: baseLineId + 3,
                countryTag: countryTag,
                archetype: EquipmentType.IFV,
                generation: ifvGen,
                assignedMilitaryFactories: (ushort)ifvFactories,
                priority: 160,
                initialEfficiency: 0.50f,
                efficiencyCap: 0.85f));
        }

        if (mbtFactories > 0)
        {
            TechGeneration mbtGen = world.GetUnlockedTechGeneration(countryTag, EquipmentType.MBT);
            world.SpawnProductionLine(ProductionLineComponent.Create(
                lineId: baseLineId + 4,
                countryTag: countryTag,
                archetype: EquipmentType.MBT,
                generation: mbtGen,
                assignedMilitaryFactories: (ushort)mbtFactories,
                priority: 140,
                initialEfficiency: 0.50f,
                efficiencyCap: 0.85f));
        }
    }

    /// <summary>
    /// Evaluates manpower and equipment stockpiles to recruit new field divisions autonomously.
    /// </summary>
    public static void EvaluateAndRecruitDivisions(SimulationWorld world, byte countryTag)
    {
        if (countryTag >= CountryTag.ActiveCount)
        {
            return;
        }

        ref readonly CountryState country = ref world.Countries[countryTag];
        if (country.Manpower < 15_000)
        {
            return;
        }

        int infStockpile = country.GetTotalArchetypeStockpile(EquipmentType.InfantryKit);
        if (infStockpile < 400)
        {
            return;
        }

        // Determine template: check if armored/mechanized equipment is plentiful
        int mbtStockpile = country.GetTotalArchetypeStockpile(EquipmentType.MBT);
        int ifvStockpile = country.GetTotalArchetypeStockpile(EquipmentType.IFV);
        ushort chosenTemplateId = DivisionTemplateRecord.DefaultInfantryTemplateId;

        if (mbtStockpile >= 40 && ifvStockpile >= 50)
        {
            for (ushort t = 0; t < SimulationWorld.MaxDivisionTemplates; t++)
            {
                ref readonly DivisionTemplateRecord tpl = ref world.Templates[t];
                if (tpl.IsConfigured != 0 && (tpl.GetRequired(EquipmentType.MBT) > 0 || tpl.GetRequired(EquipmentType.IFV) > 0))
                {
                    chosenTemplateId = t;
                    break;
                }
            }
        }
        else
        {
            for (ushort t = 0; t < SimulationWorld.MaxDivisionTemplates; t++)
            {
                ref readonly DivisionTemplateRecord tpl = ref world.Templates[t];
                if (tpl.IsConfigured != 0 && tpl.GetRequired(EquipmentType.InfantryKit) > 0)
                {
                    chosenTemplateId = t;
                    break;
                }
            }
        }

        ushort spawnProv = world.GetCapitalProvince(countryTag);
        if (spawnProv >= SimulationWorld.MaxProvinces || world.Provinces[spawnProv].ControllerTag != countryTag)
        {
            for (ushort p = 0; p < SimulationWorld.MaxProvinces; p++)
            {
                if (world.Provinces[p].ControllerTag == countryTag && world.Provinces[p].Terrain != TerrainType.Ocean)
                {
                    spawnProv = p;
                    break;
                }
            }
        }

        if (spawnProv < SimulationWorld.MaxProvinces)
        {
            world.EnqueueCommand(SimulationCommand.CreateRecruitDivision(countryTag, chosenTemplateId, spawnProv));
        }
    }

    /// <summary>
    /// Evaluates outstanding wargoals, military readiness, and geopolitical tension to execute declarations of war.
    /// In Cold War deterrence doctrine, nations only pull the trigger on offensive wargoals if DEFCON has escalated
    /// to DEFCON 1 (general war authorization) or if they hold overwhelming conventional superiority (>= 2x divisions)
    /// with high war support (>= 0.80).
    /// </summary>
    public static void EvaluateAndExecuteWargoals(SimulationWorld world, byte countryTag)
    {
        if (countryTag >= CountryTag.ActiveCount || world.CountryWargoalMasks[countryTag] == 0)
        {
            return;
        }

        ref readonly CountryState country = ref world.Countries[countryTag];
        ReadOnlySpan<SpatialDivisionItem> allDivs = world.SpatialIndex.GetAllActiveDivisions();
        int ourDivisionCount = 0;
        for (int i = 0; i < allDivs.Length; i++)
        {
            if (allDivs[i].CountryTag == countryTag && allDivs[i].State != DivisionState.Shattered)
            {
                ourDivisionCount++;
            }
        }

        for (byte targetTag = 0; targetTag < CountryTag.ActiveCount; targetTag++)
        {
            if (targetTag == countryTag || !world.HasWargoal(countryTag, targetTag) || world.IsAtWarWith(countryTag, targetTag))
            {
                continue;
            }

            int targetDivisionCount = 0;
            for (int i = 0; i < allDivs.Length; i++)
            {
                if (allDivs[i].CountryTag == targetTag && allDivs[i].State != DivisionState.Shattered)
                {
                    targetDivisionCount++;
                }
            }

            bool generalWarAuthorization = country.Defcon == 1;
            bool overwhelmingSuperiority = ourDivisionCount >= (targetDivisionCount * 2) && country.WarSupport >= 0.80f;
            bool targetDefenseless = targetDivisionCount == 0 && country.WarSupport >= 0.60f;

            bool readinessOk = generalWarAuthorization || overwhelmingSuperiority || targetDefenseless;

            if (readinessOk)
            {
                world.EnqueueCommand(SimulationCommand.CreateDeclareWar(countryTag, targetTag));
                world.CountryWargoalMasks[countryTag] &= (ushort)~(1 << targetTag);
            }
        }
    }
}
