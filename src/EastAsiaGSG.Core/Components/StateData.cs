using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Components;

/// <summary>
/// Explicit 32-byte blittable representation of a strategic state (Index == StateId, 0..139).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public unsafe struct StateData
{
    public const int ResourceCount = 4;

    [FieldOffset(0)] public ushort StateId;
    [FieldOffset(2)] public byte OwnerTag;
    [FieldOffset(3)] public byte ControllerTag;
    [FieldOffset(4)] public ushort CoreTagsMask;
    [FieldOffset(6)] public byte CivilianFactories;
    [FieldOffset(7)] public byte MilitaryFactories;
    [FieldOffset(8)] public byte NavalDockyards;
    [FieldOffset(9)] public byte AirbaseLevel;
    [FieldOffset(10)] public byte RadarSamLevel;
    [FieldOffset(11)] public byte InfrastructureLevel;
    [FieldOffset(12)] public int Population;
    [FieldOffset(16)] public fixed ushort Resources[ResourceCount];
    [FieldOffset(24)] public float ConstructionProgress;
    [FieldOffset(28)] public ushort AssignedCivFactories;
    [FieldOffset(30)] public ushort ReservedPadding;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ushort GetResource(ResourceType type)
    {
        int idx = (int)type;
        if ((uint)idx >= ResourceCount)
        {
            return 0;
        }
        return Resources[idx];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResource(ResourceType type, ushort value)
    {
        int idx = (int)type;
        if ((uint)idx < ResourceCount)
        {
            Resources[idx] = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool HasCore(byte tagId)
    {
        return tagId < 16 && (CoreTagsMask & (1 << tagId)) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddCore(byte tagId)
    {
        if (tagId < 16)
        {
            CoreTagsMask |= (ushort)(1 << tagId);
        }
    }
}
