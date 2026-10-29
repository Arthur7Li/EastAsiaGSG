using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Tier 2 Daily (00:00) system that executes the Two-Tier Supply Graph pipeline:
/// 1. Sums division supply demands per province.
/// 2. Resolves Rail and Port backbone flow from national capitals and entry nodes to forward hubs.
/// 3. Enforces the Soviet Trans-Siberian Artery bottleneck logic.
/// 4. Runs multi-source bounded Dijkstra overland propagation from operational hubs across friendly territory.
/// 5. Updates ProvinceData.SupplyRatio (0.05f to 1.00f) across all provinces with zero heap allocations.
/// </summary>
public static class SupplyGraphSystem
{
    public const float MinSupplyRatio = 0.05f;
    public const float MaxSupplyRatio = 1.00f;

    public const float BaseCapitalThroughput = 120.0f;
    public const float BaseHubCapacityMultiplier = 15.0f;
    public const float BasePortCapacityMultiplier = 10.0f;
    public const float LocalFactorySupplyFactor = 0.50f;
    public const float MinOverlandSupplyThreshold = 2.0f;

    public const float BaseEdgeFriction = 12.0f;
    public const float RiverFrictionPenalty = 5.0f;

    public const float DemandInfantry = 1.0f;
    public const float DemandMechanized = 2.0f;
    public const float DemandArmored = 3.0f;

    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();

    private sealed class SupplyContext
    {
        public readonly float[] DemandedSupply = new float[SimulationWorld.MaxProvinces];
        public readonly float[] DeliveredSupply = new float[SimulationWorld.MaxProvinces];
        public readonly float[] HubCapacity = new float[SimulationWorld.MaxProvinces];
        public readonly ushort[] BfsQueue = new ushort[SimulationWorld.MaxProvinces];
        public readonly uint[] RailVisited = new uint[SimulationWorld.MaxProvinces];
        public readonly ushort[] ActiveExportPorts = new ushort[SimulationWorld.MaxProvinces];

        // Zero-allocation Max-Binary-Heap for Overland Dijkstra
        public readonly ushort[] HeapNodes = new ushort[SimulationWorld.MaxProvinces];
        public readonly float[] HeapKeys = new float[SimulationWorld.MaxProvinces];
        public int HeapSize;

        public uint RailToken = 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HeapPush(ushort node, float key)
        {
            int i = HeapSize++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (HeapKeys[parent] >= key)
                {
                    break;
                }
                HeapNodes[i] = HeapNodes[parent];
                HeapKeys[i] = HeapKeys[parent];
                i = parent;
            }
            HeapNodes[i] = node;
            HeapKeys[i] = key;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryHeapPop(out ushort node, out float key)
        {
            if (HeapSize == 0)
            {
                node = 0;
                key = 0f;
                return false;
            }

            node = HeapNodes[0];
            key = HeapKeys[0];
            HeapSize--;

            if (HeapSize > 0)
            {
                ushort lastNode = HeapNodes[HeapSize];
                float lastKey = HeapKeys[HeapSize];
                int i = 0;

                while (true)
                {
                    int left = (i << 1) + 1;
                    if (left >= HeapSize)
                    {
                        break;
                    }

                    int right = left + 1;
                    int bestChild = (right < HeapSize && HeapKeys[right] > HeapKeys[left]) ? right : left;

                    if (HeapKeys[bestChild] <= lastKey)
                    {
                        break;
                    }

                    HeapNodes[i] = HeapNodes[bestChild];
                    HeapKeys[i] = HeapKeys[bestChild];
                    i = bestChild;
                }

                HeapNodes[i] = lastNode;
                HeapKeys[i] = lastKey;
            }

            return true;
        }
    }

    [ThreadStatic]
    private static SupplyContext? s_threadLocal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SupplyContext GetContext() => s_threadLocal ??= new SupplyContext();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetTerrainFrictionModifier(TerrainType terrain)
    {
        return terrain switch
        {
            TerrainType.Plains => 1.0f,
            TerrainType.Urban => 1.2f,
            TerrainType.Hills => 1.3f,
            TerrainType.Forest => 1.5f,
            TerrainType.Jungle => 1.7f,
            TerrainType.Mountain => 2.0f,
            TerrainType.Marsh => 2.2f,
            TerrainType.Ocean => 1.0f,
            _ => 1.0f
        };
    }

    /// <summary>
    /// Executes the full daily supply graph computation across all provinces, hubs, and dynamic divisions.
    /// </summary>
    public static void Execute(SimulationWorld world)
    {
        SupplyContext ctx = GetContext();

        Array.Clear(ctx.DemandedSupply, 0, SimulationWorld.MaxProvinces);
        Array.Clear(ctx.DeliveredSupply, 0, SimulationWorld.MaxProvinces);
        Array.Clear(ctx.HubCapacity, 0, SimulationWorld.MaxProvinces);

        // 1. Pass 1: Aggregate division supply demands per province
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
                    continue;
                }

                ushort provId = div.CurrentProvinceId;
                if (provId >= SimulationWorld.MaxProvinces)
                {
                    continue;
                }

                float demand = div.Hardness >= 0.65f
                    ? DemandArmored
                    : (div.Hardness >= 0.35f ? DemandMechanized : DemandInfantry);

                float strFactor = System.Math.Clamp(div.Strength, 0.20f, 1.0f);
                ctx.DemandedSupply[provId] += demand * strFactor;
            }
        }

        // 2. Pass 2: Rail & Port backbone from national capitals/sources
        for (byte countryTag = 0; countryTag < CountryTag.ActiveCount; countryTag++)
        {
            ushort capitalProvId = world.GetCapitalProvince(countryTag);
            ushort sourceProvId = ushort.MaxValue;

            if (capitalProvId < SimulationWorld.MaxProvinces
                && DivisionMovementSystem.IsFriendlyOrAllied(world, countryTag, world.Provinces[capitalProvId].ControllerTag))
            {
                sourceProvId = capitalProvId;
            }
            else
            {
                // Fallback to highest capacity controlled hub/city as temporary supply center
                int bestScore = -1;
                for (ushort p = 0; p < SimulationWorld.MaxProvinces; p++)
                {
                    ref readonly ProvinceData prov = ref world.Provinces[p];
                    if (prov.ControllerTag == countryTag)
                    {
                        int score = (prov.SupplyHubLevel * 10) + prov.InfrastructureLevel;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            sourceProvId = p;
                        }
                    }
                }
            }

            if (sourceProvId >= SimulationWorld.MaxProvinces)
            {
                continue;
            }

            // Trans-Siberian Artery Bottleneck check for SOV:
            // State 0 must have infrastructure > 0 and be friendly controlled for off-map supply to enter.
            if (countryTag == CountryTag.SOV)
            {
                ref readonly ProvinceData sovEntryProv = ref world.Provinces[sourceProvId];
                ref readonly StateData sovEntryState = ref world.States[sovEntryProv.StateId];
                if (sovEntryState.InfrastructureLevel == 0
                    || !DivisionMovementSystem.IsFriendlyOrAllied(world, countryTag, sovEntryState.ControllerTag))
                {
                    continue;
                }
            }

            ctx.HubCapacity[sourceProvId] = System.Math.Max(ctx.HubCapacity[sourceProvId], BaseCapitalThroughput);

            ctx.RailToken++;
            if (ctx.RailToken == 0)
            {
                Array.Clear(ctx.RailVisited, 0, SimulationWorld.MaxProvinces);
                ctx.RailToken = 1;
            }

            int queueHead = 0;
            int queueTail = 0;
            int exportPortCount = 0;

            ctx.BfsQueue[queueTail++] = sourceProvId;
            ctx.RailVisited[sourceProvId] = ctx.RailToken;

            if (world.Provinces[sourceProvId].PortLevel > 0)
            {
                ctx.ActiveExportPorts[exportPortCount++] = sourceProvId;
            }

            // Rail BFS propagation
            while (queueHead < queueTail)
            {
                ushort currId = ctx.BfsQueue[queueHead++];
                ref readonly ProvinceData currProv = ref world.Provinces[currId];
                ReadOnlySpan<ushort> neighbors = world.Graph.GetNeighbors(in currProv);
                ReadOnlySpan<byte> edgeFlags = world.Graph.GetEdgeFlags(in currProv);

                for (int k = 0; k < neighbors.Length; k++)
                {
                    if ((edgeFlags[k] & (byte)AdjacencyEdgeFlags.Railway) == 0)
                    {
                        continue;
                    }

                    ushort nextId = neighbors[k];
                    ref readonly ProvinceData nextProv = ref world.Provinces[nextId];

                    if (!DivisionMovementSystem.IsFriendlyOrAllied(world, countryTag, nextProv.ControllerTag))
                    {
                        continue;
                    }

                    ref readonly StateData nextState = ref world.States[nextProv.StateId];
                    if (nextState.InfrastructureLevel == 0)
                    {
                        continue;
                    }

                    if (ctx.RailVisited[nextId] == ctx.RailToken)
                    {
                        continue;
                    }

                    ctx.RailVisited[nextId] = ctx.RailToken;
                    ctx.BfsQueue[queueTail++] = nextId;

                    if (nextProv.SupplyHubLevel > 0)
                    {
                        float hubCap = nextProv.SupplyHubLevel * BaseHubCapacityMultiplier * (1.0f + 0.05f * nextState.InfrastructureLevel);
                        ctx.HubCapacity[nextId] = System.Math.Max(ctx.HubCapacity[nextId], hubCap);
                    }

                    if (nextProv.PortLevel > 0 && exportPortCount < SimulationWorld.MaxProvinces)
                    {
                        ctx.ActiveExportPorts[exportPortCount++] = nextId;
                    }
                }
            }

            // 3. Sea Convoy route connection to overseas/island friendly ports
            if (exportPortCount > 0)
            {
                byte maxExportPortLevel = 0;
                for (int ep = 0; ep < exportPortCount; ep++)
                {
                    byte level = world.Provinces[ctx.ActiveExportPorts[ep]].PortLevel;
                    if (level > maxExportPortLevel)
                    {
                        maxExportPortLevel = level;
                    }
                }

                for (ushort p = 0; p < SimulationWorld.MaxProvinces; p++)
                {
                    ref readonly ProvinceData prov = ref world.Provinces[p];
                    if (prov.PortLevel > 0
                        && DivisionMovementSystem.IsFriendlyOrAllied(world, countryTag, prov.ControllerTag)
                        && ctx.RailVisited[p] != ctx.RailToken)
                    {
                        // Overseas import port
                        float convoyCap = System.Math.Min(maxExportPortLevel, prov.PortLevel) * BasePortCapacityMultiplier;
                        ctx.HubCapacity[p] = System.Math.Max(ctx.HubCapacity[p], convoyCap);

                        // Feed the island/regional rail network from the import port
                        ctx.RailVisited[p] = ctx.RailToken;
                        ctx.BfsQueue[queueTail++] = p;
                    }
                }

                // Drain queue for secondary rail branches seeded by import ports
                while (queueHead < queueTail)
                {
                    ushort currId = ctx.BfsQueue[queueHead++];
                    ref readonly ProvinceData currProv = ref world.Provinces[currId];
                    ReadOnlySpan<ushort> neighbors = world.Graph.GetNeighbors(in currProv);
                    ReadOnlySpan<byte> edgeFlags = world.Graph.GetEdgeFlags(in currProv);

                    for (int k = 0; k < neighbors.Length; k++)
                    {
                        if ((edgeFlags[k] & (byte)AdjacencyEdgeFlags.Railway) == 0)
                        {
                            continue;
                        }

                        ushort nextId = neighbors[k];
                        ref readonly ProvinceData nextProv = ref world.Provinces[nextId];

                        if (!DivisionMovementSystem.IsFriendlyOrAllied(world, countryTag, nextProv.ControllerTag))
                        {
                            continue;
                        }

                        ref readonly StateData nextState = ref world.States[nextProv.StateId];
                        if (nextState.InfrastructureLevel == 0)
                        {
                            continue;
                        }

                        if (ctx.RailVisited[nextId] == ctx.RailToken)
                        {
                            continue;
                        }

                        ctx.RailVisited[nextId] = ctx.RailToken;
                        ctx.BfsQueue[queueTail++] = nextId;

                        if (nextProv.SupplyHubLevel > 0)
                        {
                            float hubCap = nextProv.SupplyHubLevel * BaseHubCapacityMultiplier * (1.0f + 0.05f * nextState.InfrastructureLevel);
                            ctx.HubCapacity[nextId] = System.Math.Max(ctx.HubCapacity[nextId], hubCap);
                        }
                    }
                }
            }
        }

        // 4. Pass 3: Local state factory trickle for isolated pockets
        for (ushort s = 0; s < SimulationWorld.MaxStates; s++)
        {
            ref readonly StateData state = ref world.States[s];
            if (state.ControllerTag == CountryTag.None)
            {
                continue;
            }

            float localSupply = (state.CivilianFactories + state.MilitaryFactories) * LocalFactorySupplyFactor;
            if (localSupply <= 0.0f)
            {
                continue;
            }

            for (ushort p = 0; p < SimulationWorld.MaxProvinces; p++)
            {
                ref readonly ProvinceData prov = ref world.Provinces[p];
                if (prov.StateId == s
                    && prov.SupplyHubLevel > 0
                    && DivisionMovementSystem.IsFriendlyOrAllied(world, state.ControllerTag, prov.ControllerTag))
                {
                    float maxLocalCap = prov.SupplyHubLevel * 10.0f;
                    float finalLocal = localSupply < maxLocalCap ? localSupply : maxLocalCap;
                    ctx.HubCapacity[p] = System.Math.Max(ctx.HubCapacity[p], finalLocal);
                }
            }
        }

        // 5. Pass 4: Multi-Source Overland Dijkstra from all operational hubs
        ctx.HeapSize = 0;
        for (ushort p = 0; p < SimulationWorld.MaxProvinces; p++)
        {
            float cap = ctx.HubCapacity[p];
            if (cap > 0.0f)
            {
                ctx.DeliveredSupply[p] = cap;
                ctx.HeapPush(p, cap);
            }
        }

        while (ctx.TryHeapPop(out ushort currId, out float currSupply))
        {
            if (currSupply < ctx.DeliveredSupply[currId])
            {
                continue;
            }

            ref readonly ProvinceData currProv = ref world.Provinces[currId];
            ReadOnlySpan<ushort> neighbors = world.Graph.GetNeighbors(in currProv);
            ReadOnlySpan<byte> edgeFlags = world.Graph.GetEdgeFlags(in currProv);

            for (int k = 0; k < neighbors.Length; k++)
            {
                ushort nextId = neighbors[k];
                ref readonly ProvinceData nextProv = ref world.Provinces[nextId];

                if (!DivisionMovementSystem.IsFriendlyOrAllied(world, currProv.ControllerTag, nextProv.ControllerTag))
                {
                    continue;
                }

                float terrainMod = GetTerrainFrictionModifier(nextProv.Terrain);
                float riverPen = ((edgeFlags[k] & (byte)AdjacencyEdgeFlags.River) != 0 && (edgeFlags[k] & (byte)AdjacencyEdgeFlags.Railway) == 0)
                    ? RiverFrictionPenalty
                    : 0.0f;

                float infra = nextProv.InfrastructureLevel;
                float friction = (BaseEdgeFriction * terrainMod + riverPen) / (1.0f + 0.15f * infra);
                float nextSupply = currSupply - friction;

                if (nextSupply > MinOverlandSupplyThreshold && nextSupply > ctx.DeliveredSupply[nextId])
                {
                    ctx.DeliveredSupply[nextId] = nextSupply;
                    ctx.HeapPush(nextId, nextSupply);
                }
            }
        }

        // 6. Pass 5: Resolve ProvinceData.SupplyRatio for all provinces
        for (ushort i = 0; i < SimulationWorld.MaxProvinces; i++)
        {
            ref ProvinceData prov = ref world.Provinces[i];
            if (prov.ControllerTag == CountryTag.None)
            {
                prov.SupplyRatio = MaxSupplyRatio;
                continue;
            }

            float demanded = ctx.DemandedSupply[i];
            float delivered = ctx.DeliveredSupply[i];

            if (demanded <= 0.0001f)
            {
                prov.SupplyRatio = delivered > 0.50f ? MaxSupplyRatio : MinSupplyRatio;
            }
            else
            {
                float ratio = delivered / demanded;
                prov.SupplyRatio = System.Math.Clamp(ratio, MinSupplyRatio, MaxSupplyRatio);
            }
        }
    }
}
