using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Unmanaged snapshot of a nation's industrial factory totals and available civilian construction capacity.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct CountryFactorySummary(
    int TotalCivilianFactories,
    int TotalMilitaryFactories,
    int TotalNavalDockyards,
    int TotalFactories,
    int ConsumerGoodsFactories,
    int TradeBalanceFactories,
    int FreeCivilianFactories);

/// <summary>
/// Tier 2 (Daily at 00:00) system that calculates available Civilian Factories (C_free) after Consumer Goods
/// and advances active State construction projects (up to 15 C_free per State).
/// </summary>
public static class ConstructionSystem
{
    public const int MaxCivFactoriesPerState = 15;
    public const float BaseCivFactoryDailyOutputIc = 5.0f;

    public const float CivilianFactoryCostIc = 10800.0f;
    public const float MilitaryFactoryCostIc = 7200.0f;
    public const float NavalDockyardCostIc = 6400.0f;
    public const float RadarSamCostIc = 3500.0f;
    public const float InfrastructureCostIc = 3000.0f;
    public const float AirbaseCostIc = 1250.0f;

    public const byte MaxBuildingSlotLevel = 250;
    public const byte MaxProvincialFacilityLevel = 10;

    /// <summary>
    /// Returns the Industrial Capacity (IC) required to construct one level of the specified building type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetBuildingCostIc(BuildingType buildingType) => buildingType switch
    {
        BuildingType.CivilianFactory => CivilianFactoryCostIc,
        BuildingType.MilitaryFactory => MilitaryFactoryCostIc,
        BuildingType.NavalDockyard => NavalDockyardCostIc,
        BuildingType.Infrastructure => InfrastructureCostIc,
        BuildingType.Airbase => AirbaseCostIc,
        BuildingType.RadarSam => RadarSamCostIc,
        _ => 0.0f
    };

    /// <summary>
    /// Calculates the exact factory summary and available Civilian Factories (C_free) for a country tag:
    /// TotalFactories = C_total + M_total + N_dockyards
    /// C_consumer = ceil(TotalFactories * ConsumerGoodsRatio)
    /// C_free = max(0, C_total - C_consumer + C_trade_balance)
    /// </summary>
    public static CountryFactorySummary CalculateFactorySummary(SimulationWorld world, byte countryTag)
    {
        if (countryTag >= SimulationWorld.MaxCountries)
        {
            return default;
        }

        int cTotal = 0;
        int mTotal = world.Countries[countryTag].OffMapMilitaryFactories;
        int nDockyards = 0;

        ReadOnlySpan<StateData> states = world.States;
        for (int i = 0; i < states.Length; i++)
        {
            ref readonly StateData state = ref states[i];
            if (state.ControllerTag == countryTag)
            {
                cTotal += state.CivilianFactories;
                mTotal += state.MilitaryFactories;
                nDockyards += state.NavalDockyards;
            }
        }

        int totalFactories = cTotal + mTotal + nDockyards;
        float consumerRatio = System.Math.Clamp(world.Countries[countryTag].ConsumerGoodsRatio, 0.0f, 1.0f);
        int cConsumer = (int)System.MathF.Ceiling(totalFactories * consumerRatio);
        int cTrade = world.Countries[countryTag].TradeBalanceFactories;
        int cFree = System.Math.Max(0, cTotal - cConsumer + cTrade);

        return new CountryFactorySummary(
            TotalCivilianFactories: cTotal,
            TotalMilitaryFactories: mTotal,
            TotalNavalDockyards: nDockyards,
            TotalFactories: totalFactories,
            ConsumerGoodsFactories: cConsumer,
            TradeBalanceFactories: cTrade,
            FreeCivilianFactories: cFree);
    }

    /// <summary>
    /// Queues or updates a state building construction project, clamping assigned civilian factories to 15.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool QueueConstruction(
        ref StateData state,
        BuildingType buildingType,
        byte levels = 1,
        ushort requestedCivFactories = MaxCivFactoriesPerState)
    {
        if (buildingType == BuildingType.None || levels == 0)
        {
            state.ActiveBuildingType = BuildingType.None;
            state.QueuedBuildingLevels = 0;
            state.AssignedCivFactories = 0;
            state.ConstructionProgress = 0.0f;
            return false;
        }

        if (state.ActiveBuildingType != buildingType)
        {
            state.ActiveBuildingType = buildingType;
            state.QueuedBuildingLevels = levels;
            state.ConstructionProgress = 0.0f;
        }
        else
        {
            int newLevels = state.QueuedBuildingLevels + levels;
            state.QueuedBuildingLevels = (byte)(newLevels > byte.MaxValue ? byte.MaxValue : newLevels);
        }

        state.AssignedCivFactories = requestedCivFactories > MaxCivFactoriesPerState
            ? (ushort)MaxCivFactoriesPerState
            : requestedCivFactories;
        return true;
    }

    /// <summary>
    /// Executes one daily construction tick across all states in SimulationWorld.
    /// </summary>
    public static void Execute(SimulationWorld world)
    {
        Span<StateData> states = world.States.AsSpan();

        // Fast-path check: determine if any state has an active construction project
        bool anyActiveProject = false;
        for (int i = 0; i < states.Length; i++)
        {
            ref readonly StateData s = ref states[i];
            if (s.ActiveBuildingType != BuildingType.None && s.QueuedBuildingLevels > 0 && s.AssignedCivFactories > 0)
            {
                anyActiveProject = true;
                break;
            }
        }

        if (!anyActiveProject)
        {
            return;
        }

        // Single-pass aggregation of controlled factories per country tag
        Span<int> cTotal = stackalloc int[SimulationWorld.MaxCountries];
        Span<int> mTotal = stackalloc int[SimulationWorld.MaxCountries];
        Span<int> nDockyards = stackalloc int[SimulationWorld.MaxCountries];
        Span<int> remainingFreeCiv = stackalloc int[SimulationWorld.MaxCountries];

        ReadOnlySpan<CountryState> countries = world.Countries;
        for (int tag = 0; tag < SimulationWorld.MaxCountries; tag++)
        {
            mTotal[tag] = countries[tag].OffMapMilitaryFactories;
        }

        for (int i = 0; i < states.Length; i++)
        {
            ref readonly StateData s = ref states[i];
            byte controller = s.ControllerTag;
            if (controller < SimulationWorld.MaxCountries)
            {
                cTotal[controller] += s.CivilianFactories;
                mTotal[controller] += s.MilitaryFactories;
                nDockyards[controller] += s.NavalDockyards;
            }
        }

        for (int tag = 0; tag < SimulationWorld.MaxCountries; tag++)
        {
            int totalFactories = cTotal[tag] + mTotal[tag] + nDockyards[tag];
            float cgRatio = System.Math.Clamp(countries[tag].ConsumerGoodsRatio, 0.0f, 1.0f);
            int cConsumer = (int)System.MathF.Ceiling(totalFactories * cgRatio);
            int cTrade = countries[tag].TradeBalanceFactories;
            remainingFreeCiv[tag] = System.Math.Max(0, cTotal[tag] - cConsumer + cTrade);
        }

        // Advance construction progress across all states with queued projects
        for (int i = 0; i < states.Length; i++)
        {
            ref StateData state = ref states[i];
            if (state.ActiveBuildingType == BuildingType.None || state.QueuedBuildingLevels == 0)
            {
                continue;
            }

            if (state.AssignedCivFactories > MaxCivFactoriesPerState)
            {
                state.AssignedCivFactories = MaxCivFactoriesPerState;
            }

            byte controller = state.ControllerTag;
            if (controller >= SimulationWorld.MaxCountries || state.AssignedCivFactories == 0)
            {
                continue;
            }

            int requestedCiv = state.AssignedCivFactories;
            int availableCiv = remainingFreeCiv[controller];
            int effectiveCiv = requestedCiv < availableCiv ? requestedCiv : availableCiv;
            if (effectiveCiv <= 0)
            {
                continue;
            }

            remainingFreeCiv[controller] -= effectiveCiv;

            float speedMod = countries[controller].ConstructionSpeedModifier;
            float speedMultiplier = System.MathF.Max(0.0f, 1.0f + speedMod);
            float dailyProgressIc = effectiveCiv * BaseCivFactoryDailyOutputIc * speedMultiplier;

            state.ConstructionProgress += dailyProgressIc;

            float costIc = GetBuildingCostIc(state.ActiveBuildingType);
            while (costIc > 0.0f && state.ConstructionProgress >= costIc && state.QueuedBuildingLevels > 0)
            {
                state.ConstructionProgress -= costIc;
                IncrementBuildingLevel(ref state, state.ActiveBuildingType);
                state.QueuedBuildingLevels--;
            }

            if (state.QueuedBuildingLevels == 0)
            {
                state.ActiveBuildingType = BuildingType.None;
                state.ConstructionProgress = 0.0f;
                state.AssignedCivFactories = 0;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IncrementBuildingLevel(ref StateData state, BuildingType buildingType)
    {
        switch (buildingType)
        {
            case BuildingType.CivilianFactory:
                if (state.CivilianFactories < MaxBuildingSlotLevel)
                {
                    state.CivilianFactories++;
                }
                break;
            case BuildingType.MilitaryFactory:
                if (state.MilitaryFactories < MaxBuildingSlotLevel)
                {
                    state.MilitaryFactories++;
                }
                break;
            case BuildingType.NavalDockyard:
                if (state.NavalDockyards < MaxBuildingSlotLevel)
                {
                    state.NavalDockyards++;
                }
                break;
            case BuildingType.Infrastructure:
                if (state.InfrastructureLevel < MaxProvincialFacilityLevel)
                {
                    state.InfrastructureLevel++;
                }
                break;
            case BuildingType.Airbase:
                if (state.AirbaseLevel < MaxProvincialFacilityLevel)
                {
                    state.AirbaseLevel++;
                }
                break;
            case BuildingType.RadarSam:
                if (state.RadarSamLevel < MaxProvincialFacilityLevel)
                {
                    state.RadarSamLevel++;
                }
                break;
            case BuildingType.None:
            default:
                break;
        }
    }
}
