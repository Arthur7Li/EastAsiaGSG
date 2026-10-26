using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Components;

/// <summary>
/// Explicit 24-byte blittable representation of a map province (Index == ProvinceId, 0..1499).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct ProvinceData
{
    [FieldOffset(0)] public ushort ProvinceId;
    [FieldOffset(2)] public ushort StateId;
    [FieldOffset(4)] public byte OwnerTag;
    [FieldOffset(5)] public byte ControllerTag;
    [FieldOffset(6)] public TerrainType Terrain;
    [FieldOffset(7)] public byte StrategicRegionId;
    [FieldOffset(8)] public byte SupplyHubLevel;
    [FieldOffset(9)] public byte PortLevel;
    [FieldOffset(10)] public byte InfrastructureLevel;
    [FieldOffset(11)] public byte AdjacencyCount;
    [FieldOffset(12)] public int AdjacencyOffset;
    [FieldOffset(16)] public float SupplyRatio;
    [FieldOffset(20)] public short CenterX;
    [FieldOffset(22)] public short CenterY;
}
