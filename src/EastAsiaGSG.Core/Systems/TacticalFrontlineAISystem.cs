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
/// 2. Launch coordinated multi-province pincer offensives (+20 combat width bonus) into vulnerable hostile provinces.
/// 3. Vector <see cref="OperationalOrderStance.Schwerpunkt"/> spearheads toward strategic objectives.
/// 4. Respect entrenchment hysteresis to preserve heavily dug-in defensive redoubts.
/// Utilizes <see cref="ProvinceSpatialIndex"/> for O(1) spatial queries with zero heap allocations in hot loop.
/// </summary>
public static class TacticalFrontlineAISystem
{
    private const int MaxFrontlineProvincesPerCountry = 64;
    private const int MaxDivisionsPerCountry = 64;
    private const int MaxOffensiveTargets = 32;
    private const float ReadyOrgRatioForGarrison = 0.55f;
    private const float ReadyOrgRatioForAttack = 0.70f;
    private const float ReadyStrengthForAttack = 0.60f;
    private const float RequiredAttackForceRatio = 1.10f;
    private const float PincerAttackForceRatio = 0.95f;
    private const float EntrenchedHoldForceRatio = 2.50f;
    private const float MinEntrenchmentToHold = 0.25f;

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

        // Fast-path: check if any AI country has active divisions in spatial index
        ReadOnlySpan<SpatialDivisionItem> allDivs = world.SpatialIndex.GetAllActiveDivisions();
        bool anyAiDivision = false;
        for (int i = 0; i < allDivs.Length; i++)
        {
            byte tag = allDivs[i].CountryTag;
            if (tag < CountryTag.ActiveCount && (aiMask & (1 << tag)) != 0)
            {
                anyAiDivision = true;
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
    /// Evaluates frontline garrisoning, multi-province pincer offensives, and operational movement for a single AI country tag.
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
        Span<OperationalOrderStance> idleDivStances = stackalloc OperationalOrderStance[MaxDivisionsPerCountry];
        Span<ushort> idleDivObjectives = stackalloc ushort[MaxDivisionsPerCountry];
        Span<float> idleDivEntrenchment = stackalloc float[MaxDivisionsPerCountry];
        int idleDivCount = 0;

        bool atWar = world.IsAtWar(countryTag);

        // 1. Gather all combat-capable Idle divisions belonging to countryTag using SpatialIndex
        ReadOnlySpan<SpatialDivisionItem> allDivs = world.SpatialIndex.GetAllActiveDivisions();
        for (int i = 0; i < allDivs.Length; i++)
        {
            ref readonly SpatialDivisionItem div = ref allDivs[i];
            if (div.CountryTag != countryTag ||
                div.State != DivisionState.Idle ||
                div.Organization <= 0.0f ||
                div.Strength <= 0.0f ||
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
                idleDivStances[idleDivCount] = div.OperationalStance;
                idleDivObjectives[idleDivCount] = div.ObjectiveProvinceId;
                idleDivEntrenchment[idleDivCount] = div.Entrenchment;
                idleDivCount++;
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

        // 3. Wartime Offensive Evaluation: Multi-Province Coordinated Pincers & Schwerpunkt Spearheads
        Span<byte> divisionOrderIssued = stackalloc byte[MaxDivisionsPerCountry];

        if (atWar && frontlineCount > 0)
        {
            // Discover all candidate hostile target provinces adjacent to our frontline
            Span<ushort> targetProvinces = stackalloc ushort[MaxOffensiveTargets];
            Span<float> targetDesirability = stackalloc float[MaxOffensiveTargets];
            Span<byte> targetPincerFlanks = stackalloc byte[MaxOffensiveTargets];
            Span<float> targetCombinedAttack = stackalloc float[MaxOffensiveTargets];
            Span<float> targetDefensePower = stackalloc float[MaxOffensiveTargets];
            int targetCount = 0;

            for (int f = 0; f < frontlineCount; f++)
            {
                ushort fromProvId = frontlineProvs[f];
                ref readonly ProvinceData fromProv = ref provinces[fromProvId];

                if (fromProv.SupplyRatio < 0.35f)
                {
                    continue;
                }

                // Check how much ready attack power is stationed in fromProv
                float fromAttackPower = 0.0f;
                for (int d = 0; d < idleDivCount; d++)
                {
                    if (idleDivProvs[d] == fromProvId && idleDivReadyForAttack[d] != 0)
                    {
                        fromAttackPower += idleDivAttackScores[d];
                    }
                }

                if (fromAttackPower <= 0.0f)
                {
                    continue;
                }

                ReadOnlySpan<ushort> neighbors = world.Graph.GetNeighbors(in fromProv);
                ReadOnlySpan<byte> edgeFlags = world.Graph.GetEdgeFlags(in fromProv);

                for (int n = 0; n < neighbors.Length; n++)
                {
                    ushort targetProvId = neighbors[n];
                    if (targetProvId >= SimulationWorld.MaxProvinces)
                    {
                        continue;
                    }

                    ref readonly ProvinceData targetProv = ref provinces[targetProvId];
                    if (targetProv.Terrain == TerrainType.Ocean || !world.IsAtWarWith(countryTag, targetProv.ControllerTag))
                    {
                        continue;
                    }

                    AdjacencyEdgeFlags flags = (AdjacencyEdgeFlags)edgeFlags[n];
                    if ((flags & AdjacencyEdgeFlags.Strait) != 0)
                    {
                        byte regionId = targetProv.StrategicRegionId;
                        FactionId attackerFaction = world.Countries[countryTag].Faction;
                        if (regionId < SimulationWorld.MaxStrategicRegions &&
                            !world.Regions[regionId].CanCrossStraitOrInvade(attackerFaction))
                        {
                            continue;
                        }
                    }

                    // Find or insert into candidate targets list
                    int targetSlot = -1;
                    for (int t = 0; t < targetCount; t++)
                    {
                        if (targetProvinces[t] == targetProvId)
                        {
                            targetSlot = t;
                            break;
                        }
                    }

                    if (targetSlot == -1)
                    {
                        if (targetCount < MaxOffensiveTargets)
                        {
                            targetSlot = targetCount++;
                            targetProvinces[targetSlot] = targetProvId;
                            targetPincerFlanks[targetSlot] = 1;
                            targetCombinedAttack[targetSlot] = fromAttackPower;
                            targetDefensePower[targetSlot] = SumEnemyDefensePowerInProvince(world, countryTag, targetProvId, targetProv.Terrain, flags);
                        }
                    }
                    else
                    {
                        targetPincerFlanks[targetSlot]++;
                        targetCombinedAttack[targetSlot] += fromAttackPower;
                    }
                }
            }

            // Score candidate offensive targets
            for (int t = 0; t < targetCount; t++)
            {
                ushort targetProvId = targetProvinces[t];
                ref readonly ProvinceData targetProv = ref provinces[targetProvId];
                float totalAtt = targetCombinedAttack[t];
                float def = targetDefensePower[t];
                float forceRatio = def <= 1.0f ? 10.0f : totalAtt / def;

                byte flanks = targetPincerFlanks[t];
                bool isPincer = flanks >= 2;
                float requiredRatio = isPincer ? PincerAttackForceRatio : RequiredAttackForceRatio;

                if (forceRatio < requiredRatio)
                {
                    targetDesirability[t] = float.MinValue;
                    continue;
                }

                float desirability = (forceRatio * 35.0f)
                                   + (isPincer ? 35.0f : 0.0f)
                                   + (targetProv.SupplyHubLevel * 20.0f)
                                   + (targetProv.PortLevel * 15.0f)
                                   + (targetProv.VictoryPoints * 5.0f)
                                   + (targetProv.Terrain == TerrainType.Urban ? 30.0f : 0.0f);

                targetDesirability[t] = desirability;
            }

            // Launch offensives against valid targets
            for (int t = 0; t < targetCount; t++)
            {
                if (targetDesirability[t] <= float.MinValue)
                {
                    continue;
                }

                ushort targetProvId = targetProvinces[t];
                float def = targetDefensePower[t];

                // Coordinate all ready divisions in all adjacent friendly provinces
                for (int d = 0; d < idleDivCount; d++)
                {
                    if (divisionOrderIssued[d] != 0 || idleDivReadyForAttack[d] == 0)
                    {
                        continue;
                    }

                    ushort fromProvId = idleDivProvs[d];
                    ref readonly ProvinceData fromProv = ref provinces[fromProvId];

                    if (!world.Graph.TryGetEdgeFlags(in fromProv, targetProvId, out _))
                    {
                        continue;
                    }

                    // Entrenchment Hysteresis:
                    // If heavily entrenched and not a designated Schwerpunkt spearhead, do not abandon redoubt
                    // unless force ratio is overwhelmingly high (>= 2.5) or province has surplus defenders (> 2)
                    if (idleDivEntrenchment[d] >= MinEntrenchmentToHold &&
                        idleDivStances[d] != OperationalOrderStance.Schwerpunkt)
                    {
                        int defendersHere = world.SpatialIndex.GetDivisionCountInProvince(fromProvId);
                        float singleForceRatio = def <= 1.0f ? 10.0f : targetCombinedAttack[t] / def;
                        if (singleForceRatio < EntrenchedHoldForceRatio && defendersHere <= 2)
                        {
                            continue; // Hold fortified defensive line
                        }
                    }

                    // Enqueue attack move command
                    world.EnqueueCommand(SimulationCommand.CreateMoveDivision(
                        countryTag,
                        idleDivIds[d],
                        targetProvId));
                    divisionOrderIssued[d] = 1;

                    // Authorize PGM for offensive strikes or Schwerpunkt spearheads
                    bool authPgm = authorizePgm || (idleDivStances[d] == OperationalOrderStance.Schwerpunkt && pgmStockpile > 0);
                    byte desiredPgmByte = authPgm ? (byte)1 : (byte)0;
                    if (idleDivPgm[d] != desiredPgmByte)
                    {
                        world.EnqueueCommand(SimulationCommand.CreateTogglePgm(
                            countryTag,
                            idleDivIds[d],
                            authPgm));
                    }
                }
            }

            // Vector any remaining Schwerpunkt spearheads along axis towards deep objective
            Span<ushort> pathBuffer = stackalloc ushort[16];
            for (int d = 0; d < idleDivCount; d++)
            {
                if (divisionOrderIssued[d] != 0 ||
                    idleDivStances[d] != OperationalOrderStance.Schwerpunkt ||
                    idleDivObjectives[d] >= SimulationWorld.MaxProvinces ||
                    idleDivObjectives[d] == idleDivProvs[d])
                {
                    continue;
                }

                ushort objProv = idleDivObjectives[d];
                PathQuery pq = new(
                    idleDivProvs[d],
                    objProv,
                    countryTag,
                    PathfindingFlags.AllowHostileTerritory | PathfindingFlags.AllowStraits);

                PathResult pr = world.FindPath(in pq, pathBuffer);
                if (pr.Success && pr.PathLength > 0)
                {
                    ushort nextHop = pathBuffer[0];
                    world.EnqueueCommand(SimulationCommand.CreateMoveDivision(
                        countryTag,
                        idleDivIds[d],
                        nextHop));
                    divisionOrderIssued[d] = 1;

                    if (pgmStockpile > 0 && idleDivPgm[d] == 0)
                    {
                        world.EnqueueCommand(SimulationCommand.CreateTogglePgm(
                            countryTag,
                            idleDivIds[d],
                            true));
                    }
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

            // Entrenchment protection: do not redeploy well-entrenched garrison unless current sector is heavily over-defended
            if (idleDivEntrenchment[d] >= MinEntrenchmentToHold &&
                currentFrontlineSlot != -1 &&
                frontlineFriendlyCount[currentFrontlineSlot] <= 2)
            {
                continue;
            }

            // If already on a frontline province with <= 2 friendly divisions, stay to maintain garrison
            if (currentFrontlineSlot != -1 && frontlineFriendlyCount[currentFrontlineSlot] <= 2)
            {
                continue;
            }

            // Find the most vulnerable / under-defended frontline province
            int bestFrontlineSlot = -1;
            float maxNeed = 0.0f;

            for (int f = 0; f < frontlineCount; f++)
            {
                byte assigned = frontlineFriendlyCount[f];
                if (assigned >= 3)
                {
                    continue;
                }

                float need = frontlineThreatScores[f] / (assigned + 1.0f);
                if (need > maxNeed)
                {
                    maxNeed = need;
                    bestFrontlineSlot = f;
                }
            }

            if (bestFrontlineSlot != -1)
            {
                ushort destProv = frontlineProvs[bestFrontlineSlot];
                if (destProv != currentProv)
                {
                    world.EnqueueCommand(SimulationCommand.CreateMoveDivision(
                        countryTag,
                        idleDivIds[d],
                        destProv));
                    divisionOrderIssued[d] = 1;

                    if (currentFrontlineSlot != -1 && frontlineFriendlyCount[currentFrontlineSlot] > 0)
                    {
                        frontlineFriendlyCount[currentFrontlineSlot]--;
                    }
                    frontlineFriendlyCount[bestFrontlineSlot]++;
                }
            }
        }
    }

    private static byte CountFriendlyDivisionsAssignedToProvince(SimulationWorld world, byte countryTag, ushort provinceId)
    {
        int total = 0;
        ReadOnlySpan<SpatialDivisionItem> divs = world.SpatialIndex.GetDivisionsInProvince(provinceId);
        for (int i = 0; i < divs.Length; i++)
        {
            if (divs[i].CountryTag == countryTag && divs[i].State != DivisionState.Shattered)
            {
                total++;
            }
        }

        // Also account for in-transit divisions moving toward provinceId
        ReadOnlySpan<SpatialDivisionItem> allDivs = world.SpatialIndex.GetAllActiveDivisions();
        for (int i = 0; i < allDivs.Length; i++)
        {
            ref readonly SpatialDivisionItem div = ref allDivs[i];
            if (div.CountryTag == countryTag &&
                div.State == DivisionState.Moving &&
                div.CurrentProvinceId != provinceId)
            {
                // Verify destination via ECS component
                ref readonly DivisionComponent comp = ref world.EcsWorld.Get<DivisionComponent>(div.Entity);
                if (comp.DestinationProvinceId == provinceId)
                {
                    total++;
                }
            }
        }

        return (byte)(total > byte.MaxValue ? byte.MaxValue : total);
    }

    private static float SumEnemyDefensePowerInProvince(
        SimulationWorld world,
        byte attackerTag,
        ushort targetProvinceId,
        TerrainType terrain,
        AdjacencyEdgeFlags edgeFlags)
    {
        float sum = 0.0f;
        ReadOnlySpan<SpatialDivisionItem> divs = world.SpatialIndex.GetDivisionsInProvince(targetProvinceId);
        for (int i = 0; i < divs.Length; i++)
        {
            ref readonly SpatialDivisionItem div = ref divs[i];
            if (div.Organization > 0.0f &&
                div.Strength > 0.0f &&
                div.State != DivisionState.Retreating &&
                div.State != DivisionState.Shattered &&
                !DivisionMovementSystem.IsFriendlyOrAllied(world, attackerTag, div.CountryTag))
            {
                sum += (div.SoftAttack + div.HardAttack + div.Defense) * div.Strength * (1.0f + div.Entrenchment);
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
