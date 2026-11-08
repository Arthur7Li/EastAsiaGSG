using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Tier 2 (Daily at 00:00) Operational Planning Utility AI.
/// Concentrates armored and mechanized spearhead divisions into <see cref="OperationalOrderStance.Schwerpunkt"/>
/// formations aimed at enemy strategic objectives (capitals, major supply hubs, ports, and high Victory Point cities),
/// while designating line infantry to <see cref="OperationalOrderStance.DefensiveScreen"/> and surplus units to
/// <see cref="OperationalOrderStance.OperationalReserve"/>.
/// Zero heap allocations in hot loop.
/// </summary>
public static class OperationalPlanSystem
{
    private const int MaxCandidateObjectives = 4;
    private const float MinSpearheadHardness = 0.25f;
    private const float MinSpearheadBreakthrough = 28.0f;
    private const float MinSpearheadArmor = 18.0f;

    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();

    /// <summary>
    /// Executes daily operational plan evaluation for all AI-controlled countries in <see cref="SimulationWorld.AiControlledCountryMask"/>.
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

            EvaluateCountryOperationalPlan(world, tag);
        }
    }

    /// <summary>
    /// Evaluates operational stances, Schwerpunkt spearhead assignments, and deep objectives for a single country tag.
    /// Can be invoked directly by tests even if <see cref="SimulationWorld.AiControlledCountryMask"/> is not set.
    /// </summary>
    public static void EvaluateCountryOperationalPlan(SimulationWorld world, byte countryTag)
    {
        if (countryTag >= CountryTag.ActiveCount)
        {
            return;
        }

        bool atWar = world.IsAtWar(countryTag);
        ushort primaryObjective = DivisionComponent.NoProvince;

        if (atWar)
        {
            primaryObjective = DiscoverPrimaryOperationalObjective(world, countryTag);
        }

        // Iterate through all divisions belonging to countryTag and assign operational stances
        Query query = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            int count = chunk.Count;

            for (int i = 0; i < count; i++)
            {
                ref DivisionComponent div = ref divs[i];
                if (div.CountryTag != countryTag || !div.IsCombatCapable)
                {
                    continue;
                }

                if (!atWar || primaryObjective == DivisionComponent.NoProvince)
                {
                    // Peacetime posture: defensive screen on borders/capitals, balanced elsewhere
                    div.ObjectiveProvinceId = DivisionComponent.NoProvince;
                    div.OperationalStance = OperationalOrderStance.DefensiveScreen;
                    continue;
                }

                // Wartime role classification
                bool isSpearhead = div.Hardness >= MinSpearheadHardness ||
                                   div.Breakthrough >= MinSpearheadBreakthrough ||
                                   div.Armor >= MinSpearheadArmor;

                if (isSpearhead)
                {
                    div.OperationalStance = OperationalOrderStance.Schwerpunkt;
                    div.ObjectiveProvinceId = primaryObjective;
                }
                else
                {
                    int stationCount = world.SpatialIndex.GetDivisionCountInProvince(div.CurrentProvinceId);
                    if (stationCount > 3)
                    {
                        div.OperationalStance = OperationalOrderStance.OperationalReserve;
                        div.ObjectiveProvinceId = DivisionComponent.NoProvince;
                    }
                    else
                    {
                        div.OperationalStance = OperationalOrderStance.DefensiveScreen;
                        div.ObjectiveProvinceId = DivisionComponent.NoProvince;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Discovers the highest-priority operational objective in enemy territory for countryTag.
    /// Evaluates enemy capitals, supply hubs, ports, and Victory Point cities in O(1) heap allocation.
    /// </summary>
    private static ushort DiscoverPrimaryOperationalObjective(SimulationWorld world, byte countryTag)
    {
        Span<ushort> candidateProvinces = stackalloc ushort[MaxCandidateObjectives];
        Span<float> candidateScores = stackalloc float[MaxCandidateObjectives];
        int candidateCount = 0;

        ReadOnlySpan<ProvinceData> provinces = world.Provinces.AsSpan();

        // 1. First priority: Check enemy national capitals
        for (byte enemyTag = 0; enemyTag < CountryTag.ActiveCount; enemyTag++)
        {
            if (!world.IsAtWarWith(countryTag, enemyTag))
            {
                continue;
            }

            ushort capitalProvId = world.GetCapitalProvince(enemyTag);
            if (capitalProvId < SimulationWorld.MaxProvinces)
            {
                ref readonly ProvinceData capProv = ref provinces[capitalProvId];
                if (capProv.ControllerTag != countryTag && capProv.Terrain != TerrainType.Ocean)
                {
                    candidateProvinces[candidateCount] = capitalProvId;
                    candidateScores[candidateCount] = 120.0f + (capProv.VictoryPoints * 5.0f);
                    candidateCount++;
                    if (candidateCount >= MaxCandidateObjectives)
                    {
                        break;
                    }
                }
            }
        }

        // 2. Second priority: High-value supply hubs, ports, and Victory Point centers
        if (candidateCount < MaxCandidateObjectives)
        {
            for (ushort p = 0; p < provinces.Length && candidateCount < MaxCandidateObjectives; p++)
            {
                ref readonly ProvinceData prov = ref provinces[p];
                if (prov.Terrain == TerrainType.Ocean || !world.IsAtWarWith(countryTag, prov.ControllerTag))
                {
                    continue;
                }

                float strategicValue = (prov.SupplyHubLevel * 25.0f)
                                     + (prov.PortLevel * 15.0f)
                                     + (prov.VictoryPoints * 6.0f)
                                     + (prov.Terrain == TerrainType.Urban ? 20.0f : 0.0f);

                if (strategicValue >= 30.0f)
                {
                    // Check if already in candidates
                    bool alreadyPresent = false;
                    for (int c = 0; c < candidateCount; c++)
                    {
                        if (candidateProvinces[c] == p)
                        {
                            alreadyPresent = true;
                            break;
                        }
                    }

                    if (!alreadyPresent)
                    {
                        candidateProvinces[candidateCount] = p;
                        candidateScores[candidateCount] = strategicValue;
                        candidateCount++;
                    }
                }
            }
        }

        if (candidateCount == 0)
        {
            return DivisionComponent.NoProvince;
        }

        // Find candidate with highest strategic score
        ushort bestObjective = candidateProvinces[0];
        float bestScore = candidateScores[0];
        for (int c = 1; c < candidateCount; c++)
        {
            if (candidateScores[c] > bestScore)
            {
                bestScore = candidateScores[c];
                bestObjective = candidateProvinces[c];
            }
        }

        return bestObjective;
    }
}
