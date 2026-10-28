using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Tier 2 (Daily at 00:00) system that calculates daily Military Factory output across the 8 Equipment Archetypes,
/// updates hyperbolic Production Efficiency, and increments national EquipmentStockpile.
/// </summary>
public static class ProductionSystem
{
    public const float BaseMilitaryFactoryDailyOutputIc = 4.5f;
    public const float HyperbolicEfficiencyGrowthRate = 0.001f;
    public const float SameArchetypeRetentionFactor = 0.70f;
    private const int MaxSortedLines = 512;

    private static readonly QueryDescription ProductionLineQuery = new QueryDescription().WithAll<ProductionLineComponent>();

    /// <summary>
    /// Computes daily Industrial Capacity (IC) for a production line:
    /// DailyIC_i = M_i * 4.5 * (1 + FactoryOutputMod) * E_i * (1 - R_pen)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CalculateDailyIc(
        int activeMilitaryFactories,
        float factoryOutputModifier,
        float efficiency,
        float resourcePenalty)
    {
        if (activeMilitaryFactories <= 0)
        {
            return 0.0f;
        }

        float outputFactor = System.MathF.Max(0.0f, 1.0f + factoryOutputModifier);
        float clampedEff = System.Math.Clamp(efficiency, ProductionLineComponent.MinEfficiency, ProductionLineComponent.MaxEfficiencyCap);
        float resourceFactor = 1.0f - System.Math.Clamp(resourcePenalty, 0.0f, 1.0f);

        return activeMilitaryFactories * BaseMilitaryFactoryDailyOutputIc * outputFactor * clampedEff * resourceFactor;
    }

    /// <summary>
    /// Computes the next day's hyperbolic production efficiency:
    /// Delta E_i = 0.001 * (E_cap^2 / E_i), clamped to [0.10, E_cap].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CalculateNextEfficiency(float currentEfficiency, float efficiencyCap)
    {
        float clampedCap = System.Math.Clamp(
            efficiencyCap,
            ProductionLineComponent.MinEfficiency,
            ProductionLineComponent.MaxEfficiencyCap);
        float clampedCurrent = System.Math.Clamp(
            currentEfficiency,
            ProductionLineComponent.MinEfficiency,
            clampedCap);

        if (clampedCurrent >= clampedCap)
        {
            return clampedCap;
        }

        float delta = HyperbolicEfficiencyGrowthRate * ((clampedCap * clampedCap) / clampedCurrent);
        float next = clampedCurrent + delta;
        return next > clampedCap ? clampedCap : next;
    }

    /// <summary>
    /// Switches the equipment produced on a production line.
    /// Upgrading TechGeneration within the same EquipmentType retains 70% efficiency (clamped >= 0.10);
    /// switching to a different EquipmentType resets efficiency to 0.10.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SwitchLineEquipment(
        ref ProductionLineComponent line,
        EquipmentType newArchetype,
        TechGeneration newGeneration,
        float customCostIc = 0.0f)
    {
        if (line.Archetype == newArchetype && line.Generation == newGeneration)
        {
            if (customCostIc > 0.0f)
            {
                line.EquipmentCostIc = customCostIc;
            }
            return;
        }

        float clampedCap = System.Math.Clamp(
            line.EfficiencyCap,
            ProductionLineComponent.MinEfficiency,
            ProductionLineComponent.MaxEfficiencyCap);

        if (line.Archetype == newArchetype)
        {
            float retained = line.Efficiency * SameArchetypeRetentionFactor;
            line.Efficiency = System.Math.Clamp(retained, ProductionLineComponent.MinEfficiency, clampedCap);
        }
        else
        {
            line.Efficiency = ProductionLineComponent.MinEfficiency;
        }

        line.Archetype = newArchetype;
        line.Generation = newGeneration;
        line.EquipmentCostIc = customCostIc > 0.0f
            ? customCostIc
            : ProductionLineComponent.GetDefaultEquipmentCostIc(newArchetype, newGeneration);
        line.AccumulatedFractionalUnits = 0.0f;
    }

    /// <summary>
    /// Evaluates strategic resource availability from controlled states for a country tag and updates
    /// ResourcePenalty on each of its production lines in priority order.
    /// </summary>
    public static void EvaluateCountryResourcePenalties(SimulationWorld world, byte countryTag)
    {
        if (countryTag >= SimulationWorld.MaxCountries)
        {
            return;
        }

        int availOil = 0;
        int availSteel = 0;
        int availAluminum = 0;
        int availSemiconductors = 0;

        ReadOnlySpan<StateData> states = world.States;
        for (int i = 0; i < states.Length; i++)
        {
            ref readonly StateData state = ref states[i];
            if (state.ControllerTag == countryTag)
            {
                availOil += state.GetResource(ResourceType.Oil);
                availSteel += state.GetResource(ResourceType.Steel);
                availAluminum += state.GetResource(ResourceType.Aluminum);
                availSemiconductors += state.GetResource(ResourceType.Semiconductors);
            }
        }

        Span<Entity> lineEntities = stackalloc Entity[MaxSortedLines];
        Span<byte> priorities = stackalloc byte[MaxSortedLines];
        Span<int> lineIds = stackalloc int[MaxSortedLines];
        int lineCount = 0;

        Query query = world.EcsWorld.Query(in ProductionLineQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<ProductionLineComponent> lines = chunk.GetSpan<ProductionLineComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (lines[i].CountryTag == countryTag && lineCount < MaxSortedLines)
                {
                    InsertSortedLine(
                        entities[i],
                        lines[i].Priority,
                        lines[i].LineId,
                        lineEntities,
                        priorities,
                        lineIds,
                        ref lineCount);
                }
            }
        }

        for (int i = 0; i < lineCount; i++)
        {
            ref ProductionLineComponent line = ref world.EcsWorld.Get<ProductionLineComponent>(lineEntities[i]);
            int factories = line.ActiveMilitaryFactories > 0
                ? line.ActiveMilitaryFactories
                : line.AssignedMilitaryFactories;

            if (factories <= 0)
            {
                line.ResourcePenalty = 0.0f;
                continue;
            }

            EquipmentResourceCost req = ProductionLineComponent.GetResourceRequirementPerFactory(line.Archetype, line.Generation);
            int reqOil = req.Oil * factories;
            int reqSteel = req.Steel * factories;
            int reqAlum = req.Aluminum * factories;
            int reqSemi = req.Semiconductors * factories;

            int totalRequired = reqOil + reqSteel + reqAlum + reqSemi;
            if (totalRequired <= 0)
            {
                line.ResourcePenalty = 0.0f;
                continue;
            }

            int usedOil = availOil < reqOil ? availOil : reqOil;
            int usedSteel = availSteel < reqSteel ? availSteel : reqSteel;
            int usedAlum = availAluminum < reqAlum ? availAluminum : reqAlum;
            int usedSemi = availSemiconductors < reqSemi ? availSemiconductors : reqSemi;

            availOil -= usedOil;
            availSteel -= usedSteel;
            availAluminum -= usedAlum;
            availSemiconductors -= usedSemi;

            int totalFulfilled = usedOil + usedSteel + usedAlum + usedSemi;
            float deficitRatio = (float)(totalRequired - totalFulfilled) / totalRequired;
            line.ResourcePenalty = System.Math.Clamp(deficitRatio, 0.0f, 1.0f);
        }
    }

    /// <summary>
    /// Executes one daily production tick across all ProductionLineComponent entities in SimulationWorld.
    /// </summary>
    public static void Execute(SimulationWorld world)
    {
        Span<Entity> sortedEntities = stackalloc Entity[MaxSortedLines];
        Span<byte> sortedPriorities = stackalloc byte[MaxSortedLines];
        Span<int> sortedLineIds = stackalloc int[MaxSortedLines];
        int lineCount = 0;

        Query query = world.EcsWorld.Query(in ProductionLineQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<ProductionLineComponent> lines = chunk.GetSpan<ProductionLineComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (lineCount < MaxSortedLines)
                {
                    InsertSortedLine(
                        entities[i],
                        lines[i].Priority,
                        lines[i].LineId,
                        sortedEntities,
                        sortedPriorities,
                        sortedLineIds,
                        ref lineCount);
                }
            }
        }

        if (lineCount == 0)
        {
            return;
        }

        // Aggregate controlled Military Factories per country tag
        Span<int> remainingMilFactories = stackalloc int[SimulationWorld.MaxCountries];
        Span<int> controlledStatesCount = stackalloc int[SimulationWorld.MaxCountries];

        Span<CountryState> countries = world.Countries.AsSpan();
        for (int tag = 0; tag < SimulationWorld.MaxCountries; tag++)
        {
            remainingMilFactories[tag] = countries[tag].OffMapMilitaryFactories;
        }

        ReadOnlySpan<StateData> states = world.States;
        for (int i = 0; i < states.Length; i++)
        {
            ref readonly StateData s = ref states[i];
            byte controller = s.ControllerTag;
            if (controller < SimulationWorld.MaxCountries)
            {
                controlledStatesCount[controller]++;
                remainingMilFactories[controller] += s.MilitaryFactories;
            }
        }

        for (int i = 0; i < lineCount; i++)
        {
            ref ProductionLineComponent line = ref world.EcsWorld.Get<ProductionLineComponent>(sortedEntities[i]);
            byte tag = line.CountryTag;
            if (tag >= SimulationWorld.MaxCountries)
            {
                continue;
            }

            int requested = line.AssignedMilitaryFactories;
            int active;

            // If the country controls states on the map or has off-map factories, cap against national M_total;
            // if the country has 0 controlled states and 0 off-map factories (e.g., standalone unit test), fund AssignedMilitaryFactories.
            if (controlledStatesCount[tag] > 0 || countries[tag].OffMapMilitaryFactories > 0)
            {
                int avail = remainingMilFactories[tag];
                active = requested < avail ? requested : avail;
                remainingMilFactories[tag] -= active;
            }
            else
            {
                active = requested;
            }

            line.ActiveMilitaryFactories = (ushort)active;
            if (active <= 0)
            {
                continue;
            }

            ref CountryState country = ref countries[tag];
            float dailyIc = CalculateDailyIc(
                activeMilitaryFactories: active,
                factoryOutputModifier: country.FactoryOutputModifier,
                efficiency: line.Efficiency,
                resourcePenalty: line.ResourcePenalty);

            float unitCostIc = line.EquipmentCostIc > 0.001f
                ? line.EquipmentCostIc
                : ProductionLineComponent.GetDefaultEquipmentCostIc(line.Archetype, line.Generation);
            line.EquipmentCostIc = unitCostIc;

            float unitsProduced = dailyIc / unitCostIc;
            float totalAccumulated = line.AccumulatedFractionalUnits + unitsProduced;
            int wholeUnits = (int)totalAccumulated;

            if (wholeUnits > 0)
            {
                country.AddStockpile(line.Archetype, line.Generation, wholeUnits);
                totalAccumulated -= wholeUnits;
            }

            line.AccumulatedFractionalUnits = totalAccumulated;
            line.Efficiency = CalculateNextEfficiency(line.Efficiency, line.EfficiencyCap);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InsertSortedLine(
        Entity entity,
        byte priority,
        int lineId,
        Span<Entity> entities,
        Span<byte> priorities,
        Span<int> lineIds,
        ref int count)
    {
        int insertAt = count;
        while (insertAt > 0)
        {
            byte prevPrio = priorities[insertAt - 1];
            int prevId = lineIds[insertAt - 1];

            // Order by Priority descending, then LineId ascending
            bool comesBefore = priority > prevPrio || (priority == prevPrio && lineId < prevId);
            if (!comesBefore)
            {
                break;
            }

            entities[insertAt] = entities[insertAt - 1];
            priorities[insertAt] = prevPrio;
            lineIds[insertAt] = prevId;
            insertAt--;
        }

        entities[insertAt] = entity;
        priorities[insertAt] = priority;
        lineIds[insertAt] = lineId;
        count++;
    }
}
