using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Tier 2 (Daily at 00:00) Tactical Frontline Utility AI.
/// Computes border threat/desirability gradients across CSR province adjacency edges and issues
/// deterministic <see cref="CommandType.MoveDivision"/> and <see cref="CommandType.TogglePgm"/> commands
/// for AI-controlled countries to:
/// 1. Garrison undefended or high-threat border provinces during peacetime and wartime.
/// 2. Launch coordinated offensives into vulnerable hostile provinces when at war.
/// Zero heap allocations in hot loop.
/// </summary>
public static class TacticalFrontlineAISystem
{
    private const int MaxFrontlineProvincesPerCountry = 64;
    private const int MaxDivisionsPerCountry = 64;
    private const float ReadyOrgRatioForGarrison = 0.55f;
    private const float ReadyOrgRatioForAttack = 0.70f;
    private const float ReadyStrengthForAttack = 0.60f;
    private const float RequiredAttackForceRatio = 1.10f;

    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();

    /// <summary>
    /// Executes daily tactical frontline AI evaluation for all countries enabled in <see cref="SimulationWorld.AiControlledCountryMask"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Execute(SimulationWorld world)
    {
        ushort aiMask = world.AiControlledCountryMask;
        if (aiMask == 0)
        {
            return;
        }

        // Fast-path: check if any AI country has field divisions
        Query divQuery = world.EcsWorld.Query(in DivisionQuery);
        bool anyAiDivision = false;
        foreach (ref Chunk chunk in divQuery)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                byte tag = divs[i].CountryTag;
                if (tag < CountryTag.ActiveCount && (aiMask & (1 << tag)) != 0)
                {
                    anyAiDivision = true;
                    break;
                }
            }
            if (anyAiDivision)
            {
                break;
            }
        }

        if (!anyAiDivision)
        {
            return;
        }

        for (byte tag = 0; tag < CountryTag.ActiveCount; tag++)
        {
            if ((aiMask & (1 << tag)) == 0)
            {
                continue;
            }

            EvaluateCountryFrontline(world, tag);
        }
    }

    /// <summary>
    /// Evaluates frontline garrisoning and offensive opportunities for a single AI country tag.
    /// Can be invoked directly by tests even if <see cref="SimulationWorld.AiControlledCountryMask"/> is not set.
    /// </summary>
    public static void EvaluateCountryFrontline(SimulationWorld world, byte countryTag)
    {
        if (countryTag >= CountryTag.ActiveCount)
        {
            return;
        }

        Span<int> idleDivIds = stackalloc int[MaxDivisionsPerCountry];
        Span<ushort> idleDivProvs = stackalloc ushort[MaxDivisionsPerCountry];
        Span<float> idleDivAttackScores = stackalloc float[MaxDivisionsPerCountry];
        Span<byte> idleDivReadyForAttack = stackalloc byte[MaxDivisionsPerCountry];
        Span<byte> idleDivPgm = stackalloc byte[MaxDivisionsPerCountry];
        int idleDivCount = 0;

        bool atWar = world.IsAtWar(countryTag);

        // 1. Gather all combat-capable Idle divisions belonging to countryTag
        Query query = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                ref readonly DivisionComponent div = ref divs[i];
                if (div.CountryTag != countryTag ||
                    !div.IsCombatCapable ||
                    div.State != DivisionState.Idle ||
                    div.CurrentProvinceId >= SimulationWorld.MaxProvinces)
                {
                    continue;
                }

                float orgRatio = div.MaxOrganization > 0.0f ? div.Organization / div.MaxOrganization : 0.0f;
                if (orgRatio < ReadyOrgRatioForGarrison)
                {
                    continue;
                }

                if (idleDivCount < MaxDivisionsPerCountry)
                {
                    idleDivIds[idleDivCount] = div.DivisionId;
                    idleDivProvs[idleDivCount] = div.CurrentProvinceId;
                    idleDivAttackScores[idleDivCount] = (div.SoftAttack + div.HardAttack + div.Breakthrough) * div.Strength;
                    idleDivReadyForAttack[idleDivCount] =
                        (orgRatio >= ReadyOrgRatioForAttack && div.Strength >= ReadyStrengthForAttack) ? (byte)1 : (byte)0;
                    idleDivPgm[idleDivCount] = div.PgmAuthorized;
                    idleDivCount++;
                }
            }
        }

        if (idleDivCount == 0)
        {
            return;
        }

        // 2. Discover Frontline Provinces controlled by countryTag
        Span<ushort> frontlineProvs = stackalloc ushort[MaxFrontlineProvincesPerCountry];
        Span<float> frontlineThreatScores = stackalloc float[MaxFrontlineProvincesPerCountry];
        Span<byte> frontlineFriendlyCount = stackalloc byte[MaxFrontlineProvincesPerCountry];
        int frontlineCount = 0;

        ReadOnlySpan<ProvinceData> provinces = world.Provinces.AsSpan();
        for (ushort p = 0; p < provinces.Length && frontlineCount < MaxFrontlineProvincesPerCountry; p++)
        {
            ref readonly ProvinceData prov = ref provinces[p];
            if (prov.ControllerTag != countryTag || prov.Terrain == TerrainType.Ocean || prov.AdjacencyCount == 0)
            {
                continue;
            }

            ReadOnlySpan<ushort> neighbors = world.Graph.GetNeighbors(in prov);
            bool isBorder = false;
            float borderThreat = 0.0f;

            for (int n = 0; n < neighbors.Length; n++)
            {
                ushort nId = neighbors[n];
                if (nId >= SimulationWorld.MaxProvinces)
                {
                    continue;
                }

                ref readonly ProvinceData nProv = ref provinces[nId];
                if (nProv.Terrain == TerrainType.Ocean || nProv.ControllerTag == CountryTag.None)
                {
                    continue;
                }

                byte nCtrl = nProv.ControllerTag;
                if (nCtrl == countryTag || DivisionMovementSystem.IsFriendlyOrAllied(world, countryTag, nCtrl))
                {
                    continue;
                }

                bool hostileEdge = atWar ? world.IsAtWarWith(countryTag, nCtrl) : true;
                if (hostileEdge)
                {
                    isBorder = true;
                    borderThreat += world.IsAtWarWith(countryTag, nCtrl) ? 50.0f : 20.0f;
                }
            }

            if (isBorder)
            {
                float strategicVal = (prov.SupplyHubLevel * 15.0f)
                                   + (prov.PortLevel * 10.0f)
                                   + (prov.Terrain == TerrainType.Urban ? 25.0f : 0.0f);

                frontlineProvs[frontlineCount] = p;
                frontlineThreatScores[frontlineCount] = borderThreat + strategicVal;
                frontlineFriendlyCount[frontlineCount] = CountFriendlyDivisionsAssignedToProvince(world, countryTag, p);
                frontlineCount++;
            }
        }

        int pgmStockpile = world.Countries[countryTag].GetTotalArchetypeStockpile(EquipmentType.PGM);
        bool authorizePgm = atWar && pgmStockpile >= 20;

        // 3. Wartime Offensive Evaluation: Launch attacks from frontline provinces into weak hostile neighbors
        Span<byte> divisionOrderIssued = stackalloc byte[MaxDivisionsPerCountry];
        if (atWar)
        {
            for (int d = 0; d < idleDivCount; d++)
            {
                if (idleDivReadyForAttack[d] == 0)
                {
                    continue;
                }

                ushort fromProvId = idleDivProvs[d];
                ref readonly ProvinceData fromProv = ref provinces[fromProvId];

                // Ensure minimum supply before launching offensive
                if (fromProv.SupplyRatio < 0.35f)
                {
                    continue;
                }

                ushort bestTargetProv = FindBestHostileTargetNeighbor(
                    world,
                    countryTag,
                    in fromProv,
                    idleDivAttackScores[d]);

                if (bestTargetProv < SimulationWorld.MaxProvinces)
                {
                    byte desiredPgmByte = authorizePgm ? (byte)1 : (byte)0;
                    if (idleDivPgm[d] != desiredPgmByte)
                    {
                        world.EnqueueCommand(SimulationCommand.CreateTogglePgm(
                            countryTag,
                            idleDivIds[d],
                            authorizePgm));
                    }

                    world.EnqueueCommand(SimulationCommand.CreateMoveDivision(
                        countryTag,
                        idleDivIds[d],
                        bestTargetProv));
                    divisionOrderIssued[d] = 1;
                }
            }
        }

        // 4. Frontline Garrison Assignment: Move unengaged rear or over-stacked divisions to under-defended border sectors
        if (frontlineCount == 0)
        {
            return;
        }

        for (int d = 0; d < idleDivCount; d++)
        {
            if (divisionOrderIssued[d] != 0)
            {
                continue;
            }

            ushort currentProv = idleDivProvs[d];
            int currentFrontlineSlot = -1;
            for (int f = 0; f < frontlineCount; f++)
            {
                if (frontlineProvs[f] == currentProv)
                {
                    currentFrontlineSlot = f;
                    break;
                }
            }

            // If division is already the sole or pair defender of a frontline province, keep it entrenched unless another sector is empty
            bool isRearDivision = currentFrontlineSlot < 0;
            bool isOverStackedFrontline = currentFrontlineSlot >= 0 && frontlineFriendlyCount[currentFrontlineSlot] >= 2;

            if (!isRearDivision && !isOverStackedFrontline)
            {
                continue;
            }

            // Find the highest-priority frontline province that needs defenders
            int bestFrontSlot = -1;
            float bestPriority = float.MinValue;

            for (int f = 0; f < frontlineCount; f++)
            {
                if (f == currentFrontlineSlot)
                {
                    continue;
                }

                // If moving from an already-garrisoned frontline province, only redeploy to an empty (0-division) frontline sector
                if (!isRearDivision && frontlineFriendlyCount[f] > 0)
                {
                    continue;
                }

                float priority = frontlineThreatScores[f] - (frontlineFriendlyCount[f] * 45.0f);
                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    bestFrontSlot = f;
                }
            }

            if (bestFrontSlot >= 0)
            {
                ushort destProv = frontlineProvs[bestFrontSlot];
                world.EnqueueCommand(SimulationCommand.CreateMoveDivision(
                    countryTag,
                    idleDivIds[d],
                    destProv));

                if (frontlineFriendlyCount[bestFrontSlot] < byte.MaxValue)
                {
                    frontlineFriendlyCount[bestFrontSlot]++;
                }
                if (currentFrontlineSlot >= 0 && frontlineFriendlyCount[currentFrontlineSlot] > 0)
                {
                    frontlineFriendlyCount[currentFrontlineSlot]--;
                }
                divisionOrderIssued[d] = 1;
            }
        }
    }

    private static byte CountFriendlyDivisionsAssignedToProvince(
        SimulationWorld world,
        byte countryTag,
        ushort provinceId)
    {
        int total = 0;
        Query query = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                ref readonly DivisionComponent div = ref divs[i];
                if (div.CountryTag != countryTag || !div.IsCombatCapable)
                {
                    continue;
                }

                if (div.CurrentProvinceId == provinceId ||
                    (div.State == DivisionState.Moving && div.DestinationProvinceId == provinceId))
                {
                    total++;
                }
            }
        }
        return (byte)(total > byte.MaxValue ? byte.MaxValue : total);
    }

    private static ushort FindBestHostileTargetNeighbor(
        SimulationWorld world,
        byte attackerTag,
        in ProvinceData fromProv,
        float singleDivAttackScore)
    {
        ReadOnlySpan<ushort> neighbors = world.Graph.GetNeighbors(in fromProv);
        ReadOnlySpan<byte> edgeFlags = world.Graph.GetEdgeFlags(in fromProv);
        ReadOnlySpan<ProvinceData> provinces = world.Provinces.AsSpan();

        // Sum total ready friendly attack power in fromProv
        float totalFriendlyAttack = SumFriendlyAttackPowerInProvince(world, attackerTag, fromProv.ProvinceId);
        if (totalFriendlyAttack <= 0.0f)
        {
            totalFriendlyAttack = singleDivAttackScore;
        }

        ushort bestTarget = ushort.MaxValue;
        float bestDesirability = float.MinValue;

        for (int i = 0; i < neighbors.Length; i++)
        {
            ushort targetProvId = neighbors[i];
            if (targetProvId >= SimulationWorld.MaxProvinces)
            {
                continue;
            }

            ref readonly ProvinceData targetProv = ref provinces[targetProvId];
            if (targetProv.Terrain == TerrainType.Ocean || !world.IsAtWarWith(attackerTag, targetProv.ControllerTag))
            {
                continue;
            }

            AdjacencyEdgeFlags flags = (AdjacencyEdgeFlags)edgeFlags[i];
            if ((flags & AdjacencyEdgeFlags.Strait) != 0)
            {
                byte regionId = targetProv.StrategicRegionId;
                FactionId attackerFaction = world.Countries[attackerTag].Faction;
                if (regionId < SimulationWorld.MaxStrategicRegions &&
                    !world.Regions[regionId].CanCrossStraitOrInvade(attackerFaction))
                {
                    continue;
                }
            }

            float enemyDefense = SumEnemyDefensePowerInProvince(world, attackerTag, targetProvId, targetProv.Terrain, flags);
            float forceRatio = enemyDefense <= 1.0f ? 10.0f : totalFriendlyAttack / enemyDefense;

            if (forceRatio < RequiredAttackForceRatio)
            {
                continue;
            }

            float desirability = (forceRatio * 35.0f)
                               + (targetProv.SupplyHubLevel * 20.0f)
                               + (targetProv.PortLevel * 15.0f)
                               + (targetProv.Terrain == TerrainType.Urban ? 30.0f : 0.0f);

            if (desirability > bestDesirability)
            {
                bestDesirability = desirability;
                bestTarget = targetProvId;
            }
        }

        return bestTarget;
    }

    private static float SumFriendlyAttackPowerInProvince(SimulationWorld world, byte countryTag, ushort provinceId)
    {
        float sum = 0.0f;
        Query query = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                ref readonly DivisionComponent div = ref divs[i];
                if (div.CountryTag == countryTag &&
                    div.IsCombatCapable &&
                    div.CurrentProvinceId == provinceId)
                {
                    sum += (div.SoftAttack + div.HardAttack + div.Breakthrough) * div.Strength;
                }
            }
        }
        return sum;
    }

    private static float SumEnemyDefensePowerInProvince(
        SimulationWorld world,
        byte attackerTag,
        ushort targetProvinceId,
        TerrainType terrain,
        AdjacencyEdgeFlags edgeFlags)
    {
        float sum = 0.0f;
        Query query = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                ref readonly DivisionComponent div = ref divs[i];
                if (div.CurrentProvinceId == targetProvinceId &&
                    div.IsCombatCapable &&
                    !DivisionMovementSystem.IsFriendlyOrAllied(world, attackerTag, div.CountryTag))
                {
                    sum += (div.SoftAttack + div.HardAttack + div.Defense) * div.Strength * (1.0f + div.Entrenchment);
                }
            }
        }

        float terrainFactor = terrain switch
        {
            TerrainType.Mountain => 1.35f,
            TerrainType.Hills => 1.15f,
            TerrainType.Urban => 1.25f,
            TerrainType.Forest or TerrainType.Jungle => 1.10f,
            _ => 1.0f
        };

        if ((edgeFlags & AdjacencyEdgeFlags.River) != 0)
        {
            terrainFactor *= 1.15f;
        }

        return sum * terrainFactor;
    }
}
