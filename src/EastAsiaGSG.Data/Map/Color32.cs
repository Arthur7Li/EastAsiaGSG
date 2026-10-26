using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Data.Map;

/// <summary>
/// Explicit 4-byte unmanaged RGBA8 color struct used in the 1D Map Lookup Texture buffer.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 4)]
public readonly struct Color32 : IEquatable<Color32>
{
    [FieldOffset(0)] public readonly byte R;
    [FieldOffset(1)] public readonly byte G;
    [FieldOffset(2)] public readonly byte B;
    [FieldOffset(3)] public readonly byte A;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color32(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color32 WithAlpha(byte alpha) => new(R, G, B, alpha);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 Lerp(Color32 a, Color32 b, float t)
    {
        float clamped = System.Math.Clamp(t, 0f, 1f);
        byte r = (byte)System.MathF.Round(a.R + (b.R - a.R) * clamped);
        byte g = (byte)System.MathF.Round(a.G + (b.G - a.G) * clamped);
        byte bl = (byte)System.MathF.Round(a.B + (b.B - a.B) * clamped);
        byte al = (byte)System.MathF.Round(a.A + (b.A - a.A) * clamped);
        return new Color32(r, g, bl, al);
    }

    public bool Equals(Color32 other) => R == other.R && G == other.G && B == other.B && A == other.A;
    public override bool Equals(object? obj) => obj is Color32 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    public static bool operator ==(Color32 left, Color32 right) => left.Equals(right);
    public static bool operator !=(Color32 left, Color32 right) => !left.Equals(right);
    public override string ToString() => $"RGBA({R}, {G}, {B}, {A})";
}
