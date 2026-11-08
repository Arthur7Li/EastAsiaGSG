using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;

namespace EastAsiaGSG.Core.ECS;

/// <summary>
/// Lightweight, unmanaged copy of division spatial state indexed in ProvinceSpatialIndex.
/// </summary>
public struct SpatialDivisionItem
{
    public Entity Entity;
    public int DivisionId;
    public ushort TemplateId;
    public byte CountryTag;
    public byte CombatWidth;
    public ushort CurrentProvinceId;
    public DivisionState State;
    public byte PgmAuthorized;
    public float Organization;
    public float MaxOrganization;
    public float Strength;
    public float SoftAttack;
    public float HardAttack;
    public float Defense;
    public float Breakthrough;
    public float Armor;
    public float Piercing;
    public float Hardness;
    public float Entrenchment;
    public ushort ObjectiveProvinceId;
    public OperationalOrderStance OperationalStance;
}

/// <summary>
/// High-performance zero-allocation CSR (Compressed Sparse Row) spatial index for field division entities.
/// Rebuilt in a single linear O(D) pass per tick, allowing O(1) retrieval of all divisions stationed
/// in any province without scanning Arch ECS chunks.
/// </summary>
public sealed class ProvinceSpatialIndex
{
    public const int MaxSpatialItems = 2048;

    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();

    private readonly int[] _offsets = new int[SimulationWorld.MaxProvinces + 1];
    private readonly ushort[] _counts = new ushort[SimulationWorld.MaxProvinces];
    private readonly SpatialDivisionItem[] _items = new SpatialDivisionItem[MaxSpatialItems];
    private readonly int[] _cursors = new int[SimulationWorld.MaxProvinces];

    private int _totalItemCount;

    /// <summary>
    /// Total number of active, non-shattered field divisions currently indexed.
    /// </summary>
    public int TotalItemCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _totalItemCount;
    }

    /// <summary>
    /// Rebuilds the spatial index across all active division entities with zero heap allocations.
    /// </summary>
    public void Rebuild(SimulationWorld world)
    {
        Array.Clear(_counts);

        // Pass 1: Count combat-capable non-shattered divisions per province
        Query query1 = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query1)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                ref readonly DivisionComponent div = ref divs[i];
                ushort provId = div.CurrentProvinceId;
                if (provId < SimulationWorld.MaxProvinces && div.State != DivisionState.Shattered)
                {
                    _counts[provId]++;
                }
            }
        }

        // Prefix sum: compute province slice offsets and initialize cursors in < 1 microsecond
        int running = 0;
        for (int p = 0; p < SimulationWorld.MaxProvinces; p++)
        {
            _offsets[p] = running;
            _cursors[p] = running;
            running += _counts[p];
        }
        _offsets[SimulationWorld.MaxProvinces] = running;
        _totalItemCount = running > MaxSpatialItems ? MaxSpatialItems : running;

        // Pass 2: Populate flat items array using cursors
        Query query2 = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query2)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int count = chunk.Count;

            for (int i = 0; i < count; i++)
            {
                ref readonly DivisionComponent div = ref divs[i];
                ushort provId = div.CurrentProvinceId;
                if (provId < SimulationWorld.MaxProvinces && div.State != DivisionState.Shattered)
                {
                    int cursor = _cursors[provId]++;
                    if (cursor < MaxSpatialItems)
                    {
                        _items[cursor] = new SpatialDivisionItem
                        {
                            Entity = entities[i],
                            DivisionId = div.DivisionId,
                            TemplateId = div.TemplateId,
                            CountryTag = div.CountryTag,
                            CombatWidth = div.CombatWidth,
                            CurrentProvinceId = provId,
                            State = div.State,
                            PgmAuthorized = div.PgmAuthorized,
                            Organization = div.Organization,
                            MaxOrganization = div.MaxOrganization,
                            Strength = div.Strength,
                            SoftAttack = div.SoftAttack,
                            HardAttack = div.HardAttack,
                            Defense = div.Defense,
                            Breakthrough = div.Breakthrough,
                            Armor = div.Armor,
                            Piercing = div.Piercing,
                            Hardness = div.Hardness,
                            Entrenchment = div.Entrenchment,
                            ObjectiveProvinceId = div.ObjectiveProvinceId,
                            OperationalStance = div.OperationalStance
                        };
                    }
                }
            }
        }
    }

    /// <summary>
    /// Returns a zero-allocation read-only span of all divisions currently stationed in the specified province in O(1) time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<SpatialDivisionItem> GetDivisionsInProvince(ushort provinceId)
    {
        if (provinceId >= SimulationWorld.MaxProvinces)
        {
            return ReadOnlySpan<SpatialDivisionItem>.Empty;
        }

        int count = _counts[provinceId];
        if (count == 0)
        {
            return ReadOnlySpan<SpatialDivisionItem>.Empty;
        }

        int offset = _offsets[provinceId];
        return new ReadOnlySpan<SpatialDivisionItem>(_items, offset, count);
    }

    /// <summary>
    /// Returns the number of active divisions stationed in the specified province in O(1) time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetDivisionCountInProvince(ushort provinceId)
    {
        return provinceId < SimulationWorld.MaxProvinces ? _counts[provinceId] : 0;
    }

    /// <summary>
    /// Returns a read-only span of all currently indexed divisions across the entire theatre.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<SpatialDivisionItem> GetAllActiveDivisions()
    {
        return new ReadOnlySpan<SpatialDivisionItem>(_items, 0, _totalItemCount);
    }
}
