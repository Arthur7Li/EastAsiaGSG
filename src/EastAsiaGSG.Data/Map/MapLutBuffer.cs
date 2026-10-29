using System.Runtime.CompilerServices;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Data.Map;

/// <summary>
/// Maintains the CPU-side 1D Dynamic Lookup Texture buffer (Width = 2048, Height = 1, RGBA8).
/// RGB stores the target province display color for the active MapMode; Alpha stores ControllerTag (0..14 or 255)
/// so the fragment shader can detect both province borders and thick international/DMZ borders.
/// </summary>
public sealed class MapLutBuffer
{
    public const int LutWidth = 2048;
    public const int BytesPerTexel = 4; // RGBA8

    private readonly Color32[] _colors = new Color32[LutWidth];
    private readonly byte[] _rawRgbaBytes = new byte[LutWidth * BytesPerTexel];

    private static readonly Color32[] CountryPalette =
    [
        new Color32(198, 48, 42),   // 00 PRC - PLA Crimson Red
        new Color32(58, 110, 185),  // 01 USA - USPACOM Navy Blue
        new Color32(150, 32, 38),   // 02 SOV - Soviet Deep Red
        new Color32(232, 224, 213), // 03 JPN - Imperial Ivory
        new Color32(46, 117, 211),  // 04 ROK - Taeguk Cobalt Blue
        new Color32(178, 52, 68),   // 05 PRK - DPRK Carmine
        new Color32(64, 152, 168),  // 06 TWN - ROC Teal/Cyan
        new Color32(181, 137, 42),  // 07 VNM - Vietnam Ochre Gold
        new Color32(114, 98, 178),  // 08 PHL - Philippines Violet
        new Color32(168, 132, 92),  // 09 MNG - Steppe Tan
        new Color32(82, 150, 98),   // 10 THA - Siam Emerald
        new Color32(142, 94, 72),   // 11 LAO - Laos Terracotta
        new Color32(118, 146, 72),  // 12 KHM - Cambodia Olive
        new Color32(162, 108, 134), // 13 MMR - Myanmar Mauve
        new Color32(214, 162, 58)   // 14 MYS - Malaysia Amber
    ];

    private static readonly Color32 UnownedColor = new(96, 102, 108, CountryTag.None);

    private static readonly Color32[] TerrainPalette =
    [
        new Color32(164, 188, 118), // 0 Plains
        new Color32(76, 130, 72),   // 1 Forest
        new Color32(158, 142, 102), // 2 Hills
        new Color32(112, 102, 94),  // 3 Mountain
        new Color32(168, 162, 172), // 4 Urban
        new Color32(42, 104, 58),   // 5 Jungle
        new Color32(94, 128, 114),  // 6 Marsh
        new Color32(20, 40, 68)     // 7 Ocean
    ];

    public ReadOnlySpan<Color32> Colors => _colors;

    /// <summary>
    /// Raw RGBA8 byte buffer (8,192 bytes) ready for direct upload to a 2048x1 GPU texture.
    /// </summary>
    public byte[] RawRgbaBytes => _rawRgbaBytes;

    public Color32 this[int provinceId] => _colors[provinceId];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 GetPoliticalColor(byte countryTag)
    {
        if (countryTag < CountryPalette.Length)
        {
            return CountryPalette[countryTag].WithAlpha(countryTag);
        }
        return UnownedColor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 GetTerrainColor(TerrainType terrain, byte controllerTag)
    {
        int idx = (int)terrain;
        Color32 baseColor = (uint)idx < (uint)TerrainPalette.Length
            ? TerrainPalette[idx]
            : TerrainPalette[0];
        return baseColor.WithAlpha(controllerTag);
    }

    /// <summary>
    /// Recomputes all 1,500 province colors into the 2048-entry CPU LUT buffer for the requested MapMode.
    /// </summary>
    public void UpdateFromWorld(SimulationWorld world, MapMode mode)
    {
        ArgumentNullException.ThrowIfNull(world);

        ReadOnlySpan<ProvinceData> provinces = world.Provinces;
        ReadOnlySpan<StateData> states = world.States;
        ReadOnlySpan<StrategicRegionData> regions = world.Regions;

        for (int i = 0; i < SimulationWorld.MaxProvinces; i++)
        {
            ref readonly ProvinceData prov = ref provinces[i];
            byte controller = prov.ControllerTag;

            Color32 color = mode switch
            {
                MapMode.Political => ComputePoliticalColor(in prov),
                MapMode.Terrain => GetTerrainColor(prov.Terrain, controller),
                MapMode.Supply => ComputeSupplyColor(in prov, controller),
                MapMode.Industrial => ComputeIndustrialColor(in prov, states, controller),
                MapMode.AirSuperiority => ComputeAirSuperiorityColor(in prov, regions, controller),
                _ => ComputePoliticalColor(in prov)
            };

            SetTexel(i, color);
        }

        // Fill remaining LUT slots (1500..2047) with neutral ocean/unowned sentinel
        for (int i = SimulationWorld.MaxProvinces; i < LutWidth; i++)
        {
            SetTexel(i, UnownedColor);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetTexel(int index, Color32 color)
    {
        _colors[index] = color;
        int byteOffset = index * BytesPerTexel;
        _rawRgbaBytes[byteOffset] = color.R;
        _rawRgbaBytes[byteOffset + 1] = color.G;
        _rawRgbaBytes[byteOffset + 2] = color.B;
        _rawRgbaBytes[byteOffset + 3] = color.A;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color32 ComputePoliticalColor(in ProvinceData prov)
    {
        return GetPoliticalColor(prov.ControllerTag);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color32 ComputeSupplyColor(in ProvinceData prov, byte controllerTag)
    {
        if (controllerTag == CountryTag.None)
        {
            return UnownedColor;
        }

        float ratio = System.Math.Clamp(prov.SupplyRatio, 0f, 1f);
        Color32 lowSupply = new(204, 48, 48, controllerTag);
        Color32 midSupply = new(218, 178, 52, controllerTag);
        Color32 fullSupply = new(56, 184, 88, controllerTag);

        Color32 blended = ratio < 0.5f
            ? Color32.Lerp(lowSupply, midSupply, ratio * 2f)
            : Color32.Lerp(midSupply, fullSupply, (ratio - 0.5f) * 2f);

        return blended.WithAlpha(controllerTag);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color32 ComputeIndustrialColor(
        in ProvinceData prov,
        ReadOnlySpan<StateData> states,
        byte controllerTag)
    {
        if (controllerTag == CountryTag.None || prov.StateId >= states.Length)
        {
            return UnownedColor;
        }

        ref readonly StateData state = ref states[prov.StateId];
        int totalFactories = state.CivilianFactories + state.MilitaryFactories + state.NavalDockyards;
        float normalized = System.Math.Clamp(totalFactories / 25f, 0f, 1f);

        Color32 lowInd = new(54, 68, 62, controllerTag);
        Color32 highInd = new(48, 224, 132, controllerTag);
        return Color32.Lerp(lowInd, highInd, normalized).WithAlpha(controllerTag);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color32 ComputeAirSuperiorityColor(
        in ProvinceData prov,
        ReadOnlySpan<StrategicRegionData> regions,
        byte controllerTag)
    {
        if (prov.StrategicRegionId >= regions.Length)
        {
            return UnownedColor;
        }

        ref readonly StrategicRegionData region = ref regions[prov.StrategicRegionId];
        float blueAir = region.GetAirSuperiority(FactionId.BlueforPacom);
        float redAir = region.GetAirSuperiority(FactionId.RedforSocialist) + region.GetAirSuperiority(FactionId.PrcSphere);

        float balance = System.Math.Clamp(0.5f + (blueAir - redAir) * 0.5f, 0f, 1f);
        Color32 redDominant = new(208, 58, 58, controllerTag);
        Color32 contested = new(185, 185, 190, controllerTag);
        Color32 blueDominant = new(58, 138, 224, controllerTag);

        Color32 blended = balance < 0.5f
            ? Color32.Lerp(redDominant, contested, balance * 2f)
            : Color32.Lerp(contested, blueDominant, (balance - 0.5f) * 2f);

        return blended.WithAlpha(controllerTag);
    }
}
