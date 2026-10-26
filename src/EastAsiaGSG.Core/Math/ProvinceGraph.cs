using System.Runtime.CompilerServices;
using EastAsiaGSG.Core.Components;

namespace EastAsiaGSG.Core.Math;

/// <summary>
/// Represents a directed adjacency edge between two provinces prior to CSR packing.
/// </summary>
public readonly record struct DirectedEdge(
    ushort FromProvinceId,
    ushort ToProvinceId,
    AdjacencyEdgeFlags Flags = AdjacencyEdgeFlags.Standard);

/// <summary>
/// Contiguous Compressed Sparse Row (CSR) adjacency graph for provinces.
/// Provides O(1) zero-allocation ReadOnlySpan neighbor lookups.
/// </summary>
public sealed class ProvinceGraph
{
    public ushort[] AdjacencyTargets { get; private set; } = [];
    public byte[] AdjacencyEdgeFlags { get; private set; } = [];

    public int EdgeCount => AdjacencyTargets.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<ushort> GetNeighbors(in ProvinceData province)
    {
        return AdjacencyTargets.AsSpan(province.AdjacencyOffset, province.AdjacencyCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> GetEdgeFlags(in ProvinceData province)
    {
        return AdjacencyEdgeFlags.AsSpan(province.AdjacencyOffset, province.AdjacencyCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetEdgeFlags(
        in ProvinceData fromProvince,
        ushort targetProvinceId,
        out AdjacencyEdgeFlags flags)
    {
        ReadOnlySpan<ushort> neighbors = GetNeighbors(in fromProvince);
        ReadOnlySpan<byte> edgeFlags = GetEdgeFlags(in fromProvince);

        for (int i = 0; i < neighbors.Length; i++)
        {
            if (neighbors[i] == targetProvinceId)
            {
                flags = (AdjacencyEdgeFlags)edgeFlags[i];
                return true;
            }
        }

        flags = Components.AdjacencyEdgeFlags.None;
        return false;
    }

    /// <summary>
    /// Packs directed edges into contiguous CSR arrays and populates AdjacencyOffset and AdjacencyCount on provinces.
    /// Deterministic: sorts edges by (FromProvinceId, ToProvinceId).
    /// </summary>
    public void BuildCsr(Span<ProvinceData> provinces, ReadOnlySpan<DirectedEdge> edges)
    {
        for (int i = 0; i < provinces.Length; i++)
        {
            provinces[i].AdjacencyOffset = 0;
            provinces[i].AdjacencyCount = 0;
        }

        if (edges.IsEmpty)
        {
            AdjacencyTargets = [];
            AdjacencyEdgeFlags = [];
            return;
        }

        DirectedEdge[] sorted = edges.ToArray();
        Array.Sort(sorted, static (a, b) =>
        {
            int cmp = a.FromProvinceId.CompareTo(b.FromProvinceId);
            return cmp != 0 ? cmp : a.ToProvinceId.CompareTo(b.ToProvinceId);
        });

        int totalEdges = sorted.Length;
        AdjacencyTargets = new ushort[totalEdges];
        AdjacencyEdgeFlags = new byte[totalEdges];

        for (int i = 0; i < totalEdges; i++)
        {
            DirectedEdge edge = sorted[i];
            AdjacencyTargets[i] = edge.ToProvinceId;
            AdjacencyEdgeFlags[i] = (byte)edge.Flags;

            if (edge.FromProvinceId < provinces.Length)
            {
                ref ProvinceData prov = ref provinces[edge.FromProvinceId];
                if (prov.AdjacencyCount == 0)
                {
                    prov.AdjacencyOffset = i;
                }
                prov.AdjacencyCount++;
            }
        }
    }
}
