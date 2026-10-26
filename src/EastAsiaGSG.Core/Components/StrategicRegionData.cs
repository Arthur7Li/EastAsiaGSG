using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Components;

/// <summary>
/// Explicit 40-byte blittable representation of one of the 18 Strategic Air/Sea Regions.
/// Tracks per-faction air superiority and naval control ratios.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 40)]
public unsafe struct StrategicRegionData
{
    public const int FactionSlotCount = 4;
    public const float StraitNavalControlThreshold = 0.60f;
    public const float StraitAirSuperiorityThreshold = 0.50f;

    [FieldOffset(0)] public byte RegionId;
    [FieldOffset(1)] public byte IsSeaZone;
    [FieldOffset(2)] public ushort CoastalRadarSamPenaltyBps;
    [FieldOffset(4)] public float WeatherEfficiencyModifier;
    [FieldOffset(8)] public fixed float AirSuperiorityRatio[FactionSlotCount];
    [FieldOffset(24)] public fixed float NavalControlRatio[FactionSlotCount];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float GetAirSuperiority(FactionId faction)
    {
        int idx = (int)faction & 0x3;
        return AirSuperiorityRatio[idx];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetAirSuperiority(FactionId faction, float ratio)
    {
        int idx = (int)faction & 0x3;
        AirSuperiorityRatio[idx] = ratio;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float GetNavalControl(FactionId faction)
    {
        int idx = (int)faction & 0x3;
        return NavalControlRatio[idx];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetNavalControl(FactionId faction, float ratio)
    {
        int idx = (int)faction & 0x3;
        NavalControlRatio[idx] = ratio;
    }

    /// <summary>
    /// Evaluates the theatre Strait &amp; Amphibious Control Gate (&gt; 60% Naval Control and &gt; 50% Air Superiority).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool CanCrossStraitOrInvade(FactionId faction)
    {
        return GetNavalControl(faction) > StraitNavalControlThreshold
            && GetAirSuperiority(faction) > StraitAirSuperiorityThreshold;
    }
}
