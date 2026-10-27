using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Tier 1 (Every 6 Hours) system that processes division movement along CSR province adjacency edges,
/// enforces the Strait &amp; Amphibious Control Gate, and initiates land combat when entering defended hostile provinces.
/// </summary>
public static class DivisionMovementSystem
{
    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();
    private static readonly QueryDescription CombatQuery = new QueryDescription().WithAll<ActiveCombatComponent>();

    /// <summary>
    /// Determines whether two country tags belong to the same nation or the same non-neutral alliance faction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFriendlyOrAllied(SimulationWorld world, byte tagA, byte tagB)
    {
        if (tagA == tagB)
        {
            return true;
        }

        if (tagA >= CountryTag.ActiveCount || tagB >= CountryTag.ActiveCount)
        {
            return false;
        }

        FactionId factionA = world.Countries[tagA].Faction;
        FactionId factionB = world.Countries[tagB].Faction;
        return factionA != FactionId.Neutral && factionA == factionB;
    }

    /// <summary>
    /// Computes an A* path from the division's current province to destinationProvinceId and assigns its waypoints.
    /// </summary>
    public static unsafe bool IssueMoveOrder(
        SimulationWorld world,
        ref DivisionComponent div,
        ushort destinationProvinceId)
    {
        if (!div.IsCombatCapable || div.State == DivisionState.Retreating)
        {
            return false;
        }

        if (destinationProvinceId >= SimulationWorld.MaxProvinces || div.CurrentProvinceId == destinationProvinceId)
        {
            div.ClearWaypoints();
            if (div.State == DivisionState.Moving || div.State == DivisionState.Attacking)
            {
                div.State = DivisionState.Idle;
                div.ActiveCombatId = DivisionComponent.NoCombat;
            }
            return false;
        }

        Span<ushort> pathBuffer = stackalloc ushort[128];
        PathQuery query = new(
            FromProvinceId: div.CurrentProvinceId,
            ToProvinceId: destinationProvinceId,
            MovingCountryTag: div.CountryTag,
            Flags: PathfindingFlags.AllowHostileTerritory | PathfindingFlags.AllowStraits);

        PathResult result = world.FindPath(in query, pathBuffer);
        if (!result.Success || result.PathLength <= 0)
        {
            return false;
        }

        int count = result.PathLength > DivisionComponent.MaxWaypoints
            ? DivisionComponent.MaxWaypoints
            : result.PathLength;

        for (int i = 0; i < count; i++)
        {
            div.Waypoints[i] = pathBuffer[i];
        }

        div.WaypointCount = (byte)count;
        div.CurrentWaypointIndex = 0;
        div.TargetProvinceId = div.Waypoints[0];
        div.DestinationProvinceId = destinationProvinceId;
        div.MovementProgress = 0.0f;
        div.CurrentEdgeCost = ComputeHopCost(world, div.CurrentProvinceId, div.TargetProvinceId, div.CountryTag);
        div.ActiveCombatId = DivisionComponent.NoCombat;
        div.State = DivisionState.Moving;
        div.Entrenchment = 0.0f;
        return true;
    }

    /// <summary>
    /// Advances all moving and retreating divisions by one 6-hour tick.
    /// </summary>
    public static unsafe void Execute(SimulationWorld world)
    {
        CleanupFinishedCombats(world);

        Query query = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<DivisionComponent> divisions = chunk.GetSpan<DivisionComponent>();
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                ref DivisionComponent div = ref divisions[i];

                if (div.State == DivisionState.Shattered || div.Strength <= 0.0f)
                {
                    div.State = DivisionState.Shattered;
                    continue;
                }

                if (div.State == DivisionState.Idle || div.State == DivisionState.Defending)
                {
                    // Passively build entrenchment up to 0.20f while stationary
                    if (div.Entrenchment < 0.20f)
                    {
                        div.Entrenchment = System.MathF.Min(0.20f, div.Entrenchment + 0.01f);
                    }
                    continue;
                }

                if (div.State == DivisionState.Attacking)
                {
                    // Division is actively engaged in combat; movement resumes once combat concludes
                    continue;
                }

                if (div.State == DivisionState.Retreating)
                {
                    AdvanceRetreat(world, ref div);
                    continue;
                }

                if (div.State == DivisionState.Moving)
                {
                    AdvanceMovement(world, ref div);
                }
            }
        }
    }

    private static unsafe void AdvanceMovement(SimulationWorld world, ref DivisionComponent div)
    {
        ushort fromId = div.CurrentProvinceId;
        ushort toId = div.TargetProvinceId;

        if (toId >= SimulationWorld.MaxProvinces || fromId == toId)
        {
            div.ClearWaypoints();
            div.State = DivisionState.Idle;
            return;
        }

        ref readonly ProvinceData fromProv = ref world.Provinces[fromId];
        ref ProvinceData toProv = ref world.Provinces[toId];

        // Verify CSR adjacency exists
        if (!world.Graph.TryGetEdgeFlags(in fromProv, toId, out AdjacencyEdgeFlags edgeFlags))
        {
            div.ClearWaypoints();
            div.State = DivisionState.Idle;
            return;
        }

        bool targetIsFriendly = toProv.ControllerTag == CountryTag.None
            || IsFriendlyOrAllied(world, div.CountryTag, toProv.ControllerTag);

        // Enforce Strait & Amphibious Control Gate (> 60% Naval Control & > 50% Air Superiority) on hostile strait crossings
        if (!targetIsFriendly && (edgeFlags & AdjacencyEdgeFlags.Strait) != 0)
        {
            FactionId attackerFaction = div.CountryTag < SimulationWorld.MaxCountries
                ? world.Countries[div.CountryTag].Faction
                : FactionId.Neutral;

            byte regionId = toProv.StrategicRegionId;
            if (regionId < SimulationWorld.MaxStrategicRegions
                && !world.Regions[regionId].CanCrossStraitOrInvade(attackerFaction))
            {
                div.ClearWaypoints();
                div.State = DivisionState.Idle;
                return;
            }
        }

        // Check if hostile defending divisions occupy TargetProvinceId
        if (!targetIsFriendly && HasHostileDefenders(world, toId, div.CountryTag, out byte defenderTag))
        {
            if (div.Organization <= 0.0f)
            {
                div.ClearWaypoints();
                div.State = DivisionState.Idle;
                return;
            }

            EngageOrSpawnCombat(world, ref div, toId, defenderTag);
            return;
        }

        // Advance movement progress along the edge
        float edgeCost = div.CurrentEdgeCost > 0.001f
            ? div.CurrentEdgeCost
            : ComputeHopCost(world, fromId, toId, div.CountryTag);
        div.CurrentEdgeCost = edgeCost;

        float supplyFactor = 0.50f + 0.50f * System.Math.Clamp(fromProv.SupplyRatio, 0.0f, 1.0f);
        float progressStep = (div.Speed * supplyFactor) / edgeCost;
        div.MovementProgress += progressStep;

        if (div.MovementProgress >= 1.0f)
        {
            // Complete hop into TargetProvinceId
            div.CurrentProvinceId = toId;
            div.MovementProgress = 0.0f;

            // Capture undefended hostile province
            if (!targetIsFriendly)
            {
                toProv.ControllerTag = div.CountryTag;
            }

            int nextIndex = div.CurrentWaypointIndex + 1;
            if (nextIndex < div.WaypointCount)
            {
                div.CurrentWaypointIndex = (byte)nextIndex;
                div.TargetProvinceId = div.Waypoints[nextIndex];
                div.CurrentEdgeCost = ComputeHopCost(world, div.CurrentProvinceId, div.TargetProvinceId, div.CountryTag);
            }
            else
            {
                if (div.DestinationProvinceId != div.CurrentProvinceId
                    && div.DestinationProvinceId != DivisionComponent.NoProvince)
                {
                    if (!IssueMoveOrder(world, ref div, div.DestinationProvinceId))
                    {
                        div.ClearWaypoints();
                        div.State = DivisionState.Idle;
                    }
                }
                else
                {
                    div.ClearWaypoints();
                    div.State = DivisionState.Idle;
                }
            }
        }
    }

    private static void AdvanceRetreat(SimulationWorld world, ref DivisionComponent div)
    {
        ushort retreatTo = div.RetreatProvinceId;
        if (retreatTo >= SimulationWorld.MaxProvinces)
        {
            div.State = DivisionState.Shattered;
            div.Strength = 0.0f;
            return;
        }

        float edgeCost = div.CurrentEdgeCost > 0.001f
            ? div.CurrentEdgeCost
            : ComputeHopCost(world, div.CurrentProvinceId, retreatTo, div.CountryTag);
        div.CurrentEdgeCost = edgeCost;

        div.MovementProgress += (div.Speed * 0.85f) / edgeCost;
        if (div.MovementProgress >= 1.0f)
        {
            div.CurrentProvinceId = retreatTo;
            div.RetreatProvinceId = DivisionComponent.NoProvince;
            div.ClearWaypoints();
            div.State = DivisionState.Idle;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ComputeHopCost(
        SimulationWorld world,
        ushort fromProvinceId,
        ushort toProvinceId,
        byte movingTag)
    {
        if (fromProvinceId >= SimulationWorld.MaxProvinces || toProvinceId >= SimulationWorld.MaxProvinces)
        {
            return 25.0f;
        }

        ref readonly ProvinceData fromProv = ref world.Provinces[fromProvinceId];
        ref readonly ProvinceData toProv = ref world.Provinces[toProvinceId];

        if (!world.Graph.TryGetEdgeFlags(in fromProv, toProvinceId, out AdjacencyEdgeFlags flags))
        {
            flags = AdjacencyEdgeFlags.Standard;
        }

        PathQuery query = new(
            fromProvinceId,
            toProvinceId,
            movingTag,
            PathfindingFlags.AllowHostileTerritory | PathfindingFlags.AllowStraits);

        float cost = ProvincePathfinder.CalculateStepCost(in fromProv, in toProv, flags, in query, world.Countries);
        if (float.IsInfinity(cost) || float.IsNaN(cost) || cost <= 0.001f)
        {
            return 25.0f;
        }
        return cost;
    }

    private static bool HasHostileDefenders(
        SimulationWorld world,
        ushort targetProvinceId,
        byte attackerTag,
        out byte defenderTag)
    {
        defenderTag = world.Provinces[targetProvinceId].ControllerTag;

        Query query = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<DivisionComponent> divisions = chunk.GetSpan<DivisionComponent>();
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                ref DivisionComponent candidate = ref divisions[i];
                if (candidate.CurrentProvinceId == targetProvinceId
                    && candidate.IsCombatCapable
                    && candidate.State != DivisionState.Retreating
                    && !IsFriendlyOrAllied(world, attackerTag, candidate.CountryTag))
                {
                    defenderTag = candidate.CountryTag;
                    if (candidate.State == DivisionState.Idle || candidate.State == DivisionState.Moving)
                    {
                        candidate.State = DivisionState.Defending;
                    }
                    return true;
                }
            }
        }

        return false;
    }

    private static void EngageOrSpawnCombat(
        SimulationWorld world,
        ref DivisionComponent attacker,
        ushort targetProvinceId,
        byte defenderTag)
    {
        attacker.State = DivisionState.Attacking;
        attacker.Entrenchment = 0.0f;

        // Check if an ongoing ActiveCombatComponent already exists at targetProvinceId
        Query combatQuery = world.EcsWorld.Query(in CombatQuery);
        foreach (ref Chunk chunk in combatQuery)
        {
            Span<ActiveCombatComponent> combats = chunk.GetSpan<ActiveCombatComponent>();
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                ref ActiveCombatComponent existing = ref combats[i];
                if (existing.Outcome == CombatOutcome.Ongoing && existing.TargetProvinceId == targetProvinceId)
                {
                    existing.RegisterAttackOrigin(attacker.CurrentProvinceId);
                    attacker.ActiveCombatId = existing.CombatId;
                    return;
                }
            }
        }

        // Create a new ActiveCombatComponent entity
        int combatId = world.AllocateNextCombatId();
        ActiveCombatComponent newCombat = ActiveCombatComponent.Create(
            combatId: combatId,
            targetProvinceId: targetProvinceId,
            primaryOriginProvinceId: attacker.CurrentProvinceId,
            attackerTag: attacker.CountryTag,
            defenderTag: defenderTag);

        attacker.ActiveCombatId = combatId;
        world.EcsWorld.Create(newCombat);
    }

    private static void CleanupFinishedCombats(SimulationWorld world)
    {
        Span<Entity> toDestroy = stackalloc Entity[64];
        int destroyCount = 0;

        Query combatQuery = world.EcsWorld.Query(in CombatQuery);
        foreach (ref Chunk chunk in combatQuery)
        {
            Span<ActiveCombatComponent> combats = chunk.GetSpan<ActiveCombatComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (combats[i].Outcome != CombatOutcome.Ongoing && destroyCount < toDestroy.Length)
                {
                    toDestroy[destroyCount++] = entities[i];
                }
            }
        }

        for (int i = 0; i < destroyCount; i++)
        {
            world.EcsWorld.Destroy(toDestroy[i]);
        }
    }
}
