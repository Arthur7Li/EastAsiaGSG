using System.Runtime.CompilerServices;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Tier 2 Daily (00:00) system that validates National Focus prerequisites, mutual exclusivity,
/// and all 11 ConditionType rules, advances active focus progress (+1 day), and dispatches
/// FocusEffectExecutor upon focus completion.
/// </summary>
public static class FocusTreeSystem
{
    public const float DailyBasePoliticalPowerGain = 1.0f;

    /// <summary>
    /// Evaluates whether a country can start the specified focus index (0..127).
    /// Checks completion mask, prerequisites, mutually exclusive focuses, and all configured conditions.
    /// </summary>
    public static bool CanStartFocus(SimulationWorld world, byte countryTag, short focusIndex)
    {
        if (countryTag >= SimulationWorld.MaxCountries ||
            focusIndex < 0 ||
            focusIndex >= SimulationWorld.MaxFocusesPerCountry)
        {
            return false;
        }

        ref readonly CountryState country = ref world.Countries[countryTag];
        if (country.HasCompletedFocus(focusIndex) || country.ActiveFocusId == focusIndex)
        {
            return false;
        }

        ref readonly FocusNodeData focus = ref world.GetFocusNode(countryTag, focusIndex);
        if (focus.IsConfigured == 0)
        {
            return false;
        }

        // 1. All prerequisites must be completed
        int prereqCount = focus.PrerequisiteCount;
        for (int i = 0; i < prereqCount; i++)
        {
            short prereqIdx = focus.GetPrerequisite(i);
            if (prereqIdx >= 0 && !country.HasCompletedFocus(prereqIdx))
            {
                return false;
            }
        }

        // 2. No mutually exclusive focus may be completed or currently active
        int exclCount = focus.MutuallyExclusiveCount;
        for (int i = 0; i < exclCount; i++)
        {
            short exclIdx = focus.GetMutuallyExclusive(i);
            if (exclIdx >= 0 && (country.HasCompletedFocus(exclIdx) || country.ActiveFocusId == exclIdx))
            {
                return false;
            }
        }

        // 3. All availability conditions must evaluate to true
        int condCount = focus.ConditionCount;
        for (int i = 0; i < condCount; i++)
        {
            FocusCondition cond = focus.GetCondition(i);
            if (!EvaluateCondition(world, countryTag, in cond))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Evaluates a single FocusCondition against SimulationWorld with zero heap allocations.
    /// </summary>
    public static bool EvaluateCondition(SimulationWorld world, byte countryTag, in FocusCondition condition)
    {
        byte targetCountry = FocusEffectExecutor.ResolveCountryTag(countryTag, condition.TargetTag);

        switch (condition.Type)
        {
            case ConditionType.None:
                return true;

            case ConditionType.DateAfter:
                return world.GetCurrentDateInt() >= condition.IntParam;

            case ConditionType.DateBefore:
                return world.GetCurrentDateInt() < condition.IntParam;

            case ConditionType.AtWar:
                return world.IsAtWar(targetCountry);

            case ConditionType.AtPeace:
                return !world.IsAtWar(targetCountry);

            case ConditionType.ControlsState:
                return condition.TargetId < SimulationWorld.MaxStates
                    && world.States[condition.TargetId].ControllerTag == targetCountry;

            case ConditionType.OwnsState:
                return condition.TargetId < SimulationWorld.MaxStates
                    && world.States[condition.TargetId].OwnerTag == targetCountry;

            case ConditionType.StabilityGreaterThan:
            {
                float threshold = NormalizeThreshold(condition.Value);
                return world.Countries[targetCountry].Stability > threshold;
            }

            case ConditionType.WarSupportGreaterThan:
            {
                float threshold = NormalizeThreshold(condition.Value);
                return world.Countries[targetCountry].WarSupport > threshold;
            }

            case ConditionType.DefconAtMost:
            {
                int maxDefcon = (int)System.MathF.Round(condition.Value);
                return world.Countries[targetCountry].Defcon <= maxDefcon;
            }

            case ConditionType.HasCompletedFocus:
                return world.Countries[targetCountry].HasCompletedFocus(condition.TargetId);

            case ConditionType.HomelandEscalationGreaterThan:
            {
                byte escTag = condition.TargetTag < SimulationWorld.MaxCountries
                    ? condition.TargetTag
                    : CountryTag.USA;
                return world.Countries[escTag].HomelandEscalation > condition.Value;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to start a National Focus for the specified country.
    /// If the focus is registered in SimulationWorld.FocusTrees, validates CanStartFocus and uses its configured CostDays
    /// (unless fallbackCostDays > 0 overrides an unset cost). Also supports raw unconfigured focus IDs when fallbackCostDays > 0.
    /// </summary>
    public static bool TryStartFocus(
        SimulationWorld world,
        byte countryTag,
        short focusIndex,
        short fallbackCostDays = 0)
    {
        if (countryTag >= SimulationWorld.MaxCountries)
        {
            return false;
        }

        ref CountryState country = ref world.Countries[countryTag];
        if (focusIndex < 0)
        {
            country.ActiveFocusId = -1;
            country.FocusDaysRemaining = 0;
            return true;
        }

        if (focusIndex >= SimulationWorld.MaxFocusesPerCountry || country.HasCompletedFocus(focusIndex))
        {
            return false;
        }

        ref readonly FocusNodeData focus = ref world.GetFocusNode(countryTag, focusIndex);
        if (focus.IsConfigured != 0)
        {
            if (!CanStartFocus(world, countryTag, focusIndex))
            {
                return false;
            }

            short costDays = focus.CostDays > 0
                ? focus.CostDays
                : (fallbackCostDays > 0 ? fallbackCostDays : FocusNodeData.DefaultCostDays);

            country.ActiveFocusId = focusIndex;
            country.FocusDaysRemaining = costDays;
            return true;
        }

        if (fallbackCostDays > 0)
        {
            country.ActiveFocusId = focusIndex;
            country.FocusDaysRemaining = fallbackCostDays;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Executes daily focus progress and Political Power generation across all countries in SimulationWorld.
    /// Operates with zero heap allocations.
    /// </summary>
    public static void Execute(SimulationWorld world)
    {
        Span<CountryState> countries = world.Countries.AsSpan();
        for (int c = 0; c < countries.Length; c++)
        {
            ref CountryState country = ref countries[c];
            country.PoliticalPower += DailyBasePoliticalPowerGain;

            short activeFocusId = country.ActiveFocusId;
            if (activeFocusId >= 0 && country.FocusDaysRemaining > 0)
            {
                country.FocusDaysRemaining--;
                if (country.FocusDaysRemaining <= 0)
                {
                    country.MarkFocusCompleted(activeFocusId);
                    country.ActiveFocusId = -1;
                    country.FocusDaysRemaining = 0;

                    ref readonly FocusNodeData completedFocus = ref world.GetFocusNode((byte)c, activeFocusId);
                    if (completedFocus.IsConfigured != 0)
                    {
                        FocusEffectExecutor.ExecuteFocusEffects(world, (byte)c, in completedFocus);
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float NormalizeThreshold(float raw)
    {
        return raw > 1.0f ? raw / 100.0f : raw;
    }
}
