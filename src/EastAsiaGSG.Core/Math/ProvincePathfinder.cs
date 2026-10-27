using System.Runtime.CompilerServices;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Math;

/// <summary>
/// Bitmask options altering pathfinder behavior, constraints, and heuristics.
/// </summary>
[Flags]
public enum PathfindingFlags : byte
{
    None = 0,
    AllowHostileTerritory = 1 << 0,  // Permit moving into enemy-controlled provinces
    AllowStraits = 1 << 1,           // Permit crossing strait edges
    IgnoreTerrainCost = 1 << 2,      // Compute topology/hop distance without terrain multipliers
    PreferRailways = 1 << 3,         // Heavily discount edges with AdjacencyEdgeFlags.Railway
    IncludeStartProvince = 1 << 4    // Include starting province in destination path
}

/// <summary>
/// Input query parameters for an A* pathfinding search.
/// </summary>
public readonly record struct PathQuery(
    ushort FromProvinceId,
    ushort ToProvinceId,
    byte MovingCountryTag = CountryTag.None,
    PathfindingFlags Flags = PathfindingFlags.AllowHostileTerritory,
    float MaxCost = float.PositiveInfinity);

/// <summary>
/// Result status and telemetry for a completed pathfinding query.
/// </summary>
public readonly record struct PathResult(
    bool Success,
    int PathLength,
    float TotalCost,
    ushort DestinationProvinceId)
{
    public static readonly PathResult Failed = new(false, 0, float.PositiveInfinity, ushort.MaxValue);
}

/// <summary>
/// High-performance, zero-allocation A* pathfinder operating over the CSR ProvinceGraph.
/// Employs generational search tokens for O(1) resets and an unmanaged binary min-heap.
/// </summary>
public sealed class ProvincePathfinder
{
    public const int MaxNodes = SimulationWorld.MaxProvinces; // 1500

    [ThreadStatic]
    private static ProvincePathfinder? s_threadLocal;

    /// <summary>
    /// Gets or creates a thread-local pathfinder instance with pre-allocated buffers,
    /// safe for concurrent execution across client UI and simulation worker threads.
    /// </summary>
    public static ProvincePathfinder GetThreadLocal()
    {
        return s_threadLocal ??= new ProvincePathfinder();
    }

    /// <summary>
    /// Thread-safe static pathfinding query utilizing thread-local buffers.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PathResult FindPathThreadSafe(
        SimulationWorld world,
        in PathQuery query,
        Span<ushort> destinationPath)
    {
        return GetThreadLocal().FindPath(world, in query, destinationPath);
    }

    private readonly float[] _gScores = new float[MaxNodes];
    private readonly float[] _fScores = new float[MaxNodes];
    private readonly ushort[] _cameFrom = new ushort[MaxNodes];
    private readonly uint[] _visitedTokens = new uint[MaxNodes];

    // Priority queue storage (Binary Min-Heap)
    private readonly ushort[] _heap = new ushort[MaxNodes];
    private readonly ushort[] _heapPositions = new ushort[MaxNodes];
    private readonly uint[] _heapTokens = new uint[MaxNodes];
    private int _heapCount;

    // Generational search token for O(1) state reset without clearing arrays
    private uint _searchToken;

    /// <summary>
    /// Finds the optimal lowest-cost path between query.FromProvinceId and query.ToProvinceId.
    /// Writes the resulting province ID sequence into destinationPath with 0 heap allocations.
    /// </summary>
    public PathResult FindPath(
        SimulationWorld world,
        in PathQuery query,
        Span<ushort> destinationPath)
    {
        ArgumentNullException.ThrowIfNull(world);

        ushort start = query.FromProvinceId;
        ushort goal = query.ToProvinceId;

        if (start >= MaxNodes || goal >= MaxNodes)
        {
            return PathResult.Failed;
        }

        ProvinceData[] provinces = world.Provinces;
        CountryState[] countries = world.Countries;
        ushort[] adjTargets = world.Graph.AdjacencyTargets;
        byte[] adjFlags = world.Graph.AdjacencyEdgeFlags;
        float[] gScores = _gScores;
        float[] fScores = _fScores;
        ushort[] cameFrom = _cameFrom;
        uint[] visitedTokens = _visitedTokens;
        uint[] heapTokens = _heapTokens;

        // Check if goal is ocean and straits are not allowed
        ref readonly ProvinceData goalProv = ref provinces[goal];
        if (goalProv.Terrain == TerrainType.Ocean && (query.Flags & PathfindingFlags.AllowStraits) == 0)
        {
            return PathResult.Failed;
        }

        // Trivial case: Start == Goal
        if (start == goal)
        {
            if ((query.Flags & PathfindingFlags.IncludeStartProvince) != 0)
            {
                if (destinationPath.IsEmpty)
                {
                    return PathResult.Failed;
                }
                destinationPath[0] = start;
                return new PathResult(true, 1, 0f, goal);
            }
            return new PathResult(true, 0, 0f, goal);
        }

        // Advance search token for O(1) state reset
        uint token = ++_searchToken;
        if (token == 0)
        {
            Array.Clear(visitedTokens, 0, visitedTokens.Length);
            Array.Clear(heapTokens, 0, heapTokens.Length);
            token = _searchToken = 1;
        }

        // Reset heap
        _heapCount = 0;

        // Initialize start node
        visitedTokens[start] = token;
        gScores[start] = 0f;
        float startH = ComputeHeuristic(in provinces[start], in goalProv, query.Flags);
        fScores[start] = startH;
        cameFrom[start] = start;

        HeapPush(start, startH);

        bool reachedGoal = false;

        while (_heapCount > 0)
        {
            ushort current = HeapPopMin();

            if (current == goal)
            {
                reachedGoal = true;
                break;
            }

            float currentG = gScores[current];
            if (currentG >= query.MaxCost || fScores[current] > query.MaxCost)
            {
                continue;
            }

            ref readonly ProvinceData currentProv = ref provinces[current];
            int adjStart = currentProv.AdjacencyOffset;
            int adjEnd = adjStart + currentProv.AdjacencyCount;

            for (int i = adjStart; i < adjEnd; i++)
            {
                ushort neighbor = adjTargets[i];
                if (neighbor >= MaxNodes)
                {
                    continue;
                }

                ref readonly ProvinceData neighborProv = ref provinces[neighbor];
                AdjacencyEdgeFlags flags = (AdjacencyEdgeFlags)adjFlags[i];

                float stepCost = CalculateStepCost(
                    in currentProv,
                    in neighborProv,
                    flags,
                    in query,
                    countries);

                if (float.IsPositiveInfinity(stepCost) || float.IsNaN(stepCost))
                {
                    continue;
                }

                float tentativeG = currentG + stepCost;
                if (tentativeG > query.MaxCost)
                {
                    continue;
                }

                bool visited = visitedTokens[neighbor] == token;

                if (!visited || tentativeG < gScores[neighbor])
                {
                    visitedTokens[neighbor] = token;
                    cameFrom[neighbor] = current;
                    gScores[neighbor] = tentativeG;

                    float h = ComputeHeuristic(in neighborProv, in goalProv, query.Flags);
                    float f = tentativeG + h;
                    fScores[neighbor] = f;

                    if (heapTokens[neighbor] != token)
                    {
                        HeapPush(neighbor, f);
                    }
                    else
                    {
                        HeapDecreaseKey(neighbor, f);
                    }
                }
            }
        }

        if (!reachedGoal)
        {
            return PathResult.Failed;
        }

        // Reconstruct path
        return ReconstructPath(start, goal, query.Flags, destinationPath);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private PathResult ReconstructPath(
        ushort start,
        ushort goal,
        PathfindingFlags flags,
        Span<ushort> destinationPath)
    {
        // Measure path length first by backtracking with defense-in-depth loop limit
        int length = 0;
        ushort curr = goal;
        bool includeStart = (flags & PathfindingFlags.IncludeStartProvince) != 0;

        while (curr != start)
        {
            length++;
            if (length > MaxNodes)
            {
                return PathResult.Failed;
            }
            curr = _cameFrom[curr];
        }

        if (includeStart)
        {
            length++;
        }

        if (destinationPath.Length < length)
        {
            // Destination buffer is insufficient
            return new PathResult(false, length, _gScores[goal], goal);
        }

        // Write backwards into destinationPath then reverse
        int writeIndex = length - 1;
        curr = goal;

        while (curr != start)
        {
            destinationPath[writeIndex--] = curr;
            curr = _cameFrom[curr];
        }

        if (includeStart)
        {
            destinationPath[writeIndex] = start;
        }

        return new PathResult(true, length, _gScores[goal], goal);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CalculateStepCost(
        in ProvinceData fromProv,
        in ProvinceData toProv,
        AdjacencyEdgeFlags flags,
        in PathQuery query,
        ReadOnlySpan<CountryState> countries = default)
    {
        // 1. Strait gating
        if ((flags & AdjacencyEdgeFlags.Strait) != 0)
        {
            if ((query.Flags & PathfindingFlags.AllowStraits) == 0)
            {
                return float.PositiveInfinity;
            }
        }

        // 2. Ocean gating (cannot traverse ocean unless strait crossing is enabled)
        if (toProv.Terrain == TerrainType.Ocean && (flags & AdjacencyEdgeFlags.Strait) == 0)
        {
            return float.PositiveInfinity;
        }

        // 3. Geopolitical territorial access & alliance check
        byte movingTag = query.MovingCountryTag;
        byte controller = toProv.ControllerTag;

        bool isAllied = movingTag < CountryTag.ActiveCount &&
                        controller < CountryTag.ActiveCount &&
                        (movingTag == controller ||
                         (!countries.IsEmpty &&
                          movingTag < countries.Length &&
                          controller < countries.Length &&
                          countries[movingTag].Faction == countries[controller].Faction &&
                          countries[movingTag].Faction != FactionId.Neutral));

        if (movingTag < CountryTag.ActiveCount && controller < CountryTag.ActiveCount)
        {
            if (!isAllied && (query.Flags & PathfindingFlags.AllowHostileTerritory) == 0)
            {
                return float.PositiveInfinity;
            }
        }

        // If ignoring terrain/distance cost, return unit hop cost
        if ((query.Flags & PathfindingFlags.IgnoreTerrainCost) != 0)
        {
            return 1.0f;
        }

        // 4. Physical distance calculation
        float dx = toProv.CenterX - fromProv.CenterX;
        float dy = toProv.CenterY - fromProv.CenterY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= 0.001f)
        {
            distance = 25.0f; // Nominal baseline if coordinates uninitialized/coincident
        }

        // 5. Terrain multiplier (applied to entering province)
        float terrainMult = toProv.Terrain switch
        {
            TerrainType.Plains => 1.0f,
            TerrainType.Forest => 1.25f,
            TerrainType.Hills => 1.50f,
            TerrainType.Mountain => 2.25f,
            TerrainType.Urban => 1.10f,
            TerrainType.Jungle => 1.75f,
            TerrainType.Marsh => 2.00f,
            TerrainType.Ocean => 3.00f, // For strait crossing
            _ => 1.0f
        };

        float cost = distance * terrainMult;

        // 6. River penalty
        if ((flags & AdjacencyEdgeFlags.River) != 0)
        {
            cost *= 1.35f;
        }

        // 7. Railway bonus
        if ((flags & AdjacencyEdgeFlags.Railway) != 0)
        {
            if ((query.Flags & PathfindingFlags.PreferRailways) != 0)
            {
                cost *= 0.40f; // Strategic redeployment fast transit
            }
            else
            {
                cost *= 0.75f; // Standard rail transit advantage
            }
        }

        // 8. Strait crossing base penalty
        if ((flags & AdjacencyEdgeFlags.Strait) != 0)
        {
            cost += 35.0f;
        }

        // 9. Infrastructure modifier (Level 0..10 reduces travel duration)
        float infraMod = 1.0f / (0.70f + 0.05f * toProv.InfrastructureLevel);
        cost *= infraMod;

        // 10. Hostile territory movement friction (only applied to non-allied territory)
        if (movingTag < CountryTag.ActiveCount && controller < CountryTag.ActiveCount && !isAllied)
        {
            cost *= 1.40f; // Slower movement when advancing into enemy lines
        }

        return cost;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeHeuristic(
        in ProvinceData fromProv,
        in ProvinceData toProv,
        PathfindingFlags flags)
    {
        if ((flags & PathfindingFlags.IgnoreTerrainCost) != 0)
        {
            return 0f; // Degrades to Dijkstra for hop counts
        }

        float dx = toProv.CenterX - fromProv.CenterX;
        float dy = toProv.CenterY - fromProv.CenterY;
        float euclideanDist = MathF.Sqrt(dx * dx + dy * dy);

        // Under-estimate factor: 0.25f when PreferRailways is active, 0.45f standard.
        // Guarantees admissibility (<= true lowest possible traversal cost) under all conditions.
        float factor = (flags & PathfindingFlags.PreferRailways) != 0 ? 0.25f : 0.45f;
        return euclideanDist * factor;
    }

    // =========================================================================
    // Unmanaged Binary Min-Heap Priority Queue (Zero Allocation)
    // =========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void HeapPush(ushort provinceId, float fScore)
    {
        int i = _heapCount++;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            ushort parentNode = _heap[parent];
            if (fScore >= _fScores[parentNode])
            {
                break;
            }

            _heap[i] = parentNode;
            _heapPositions[parentNode] = (ushort)i;
            i = parent;
        }

        _heap[i] = provinceId;
        _heapPositions[provinceId] = (ushort)i;
        _heapTokens[provinceId] = _searchToken;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort HeapPopMin()
    {
        ushort minNode = _heap[0];
        _heapTokens[minNode] = 0; // Mark no longer in heap
        _heapCount--;

        if (_heapCount > 0)
        {
            ushort lastNode = _heap[_heapCount];
            float lastScore = _fScores[lastNode];
            int i = 0;
            int half = _heapCount >> 1;

            while (i < half)
            {
                int left = (i << 1) + 1;
                int right = left + 1;
                int bestChild = left;
                float bestScore = _fScores[_heap[left]];

                if (right < _heapCount && _fScores[_heap[right]] < bestScore)
                {
                    bestChild = right;
                    bestScore = _fScores[_heap[right]];
                }

                if (lastScore <= bestScore)
                {
                    break;
                }

                ushort childNode = _heap[bestChild];
                _heap[i] = childNode;
                _heapPositions[childNode] = (ushort)i;
                i = bestChild;
            }

            _heap[i] = lastNode;
            _heapPositions[lastNode] = (ushort)i;
        }

        return minNode;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void HeapDecreaseKey(ushort provinceId, float newFScore)
    {
        int i = _heapPositions[provinceId];

        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            ushort parentNode = _heap[parent];
            if (newFScore >= _fScores[parentNode])
            {
                break;
            }

            _heap[i] = parentNode;
            _heapPositions[parentNode] = (ushort)i;
            i = parent;
        }

        _heap[i] = provinceId;
        _heapPositions[provinceId] = (ushort)i;
    }
}
