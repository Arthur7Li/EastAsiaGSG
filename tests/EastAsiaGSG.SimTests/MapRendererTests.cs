using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Loaders;
using EastAsiaGSG.Data.Map;

namespace EastAsiaGSG.SimTests;

public sealed class MapRendererTests
{
    private static string FindRepositoryRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "EastAsiaGSG.sln")))
            {
                return dir;
            }
            string? parent = Directory.GetParent(dir)?.FullName;
            if (parent == null || parent == dir)
            {
                break;
            }
            dir = parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing EastAsiaGSG.sln");
    }

    [Fact]
    public void ProvinceMapBuffer_16BitRgEncodingDecoding_RoundtripsAllValidIdsAndOceanSentinel()
    {
        for (ushort id = 0; id < SimulationWorld.MaxProvinces; id++)
        {
            ProvinceMapBuffer.EncodeProvinceId(id, out byte r, out byte g);
            ushort decoded = ProvinceMapBuffer.DecodeProvinceId(r, g);
            Assert.Equal(id, decoded);
        }

        ProvinceMapBuffer.EncodeProvinceId(ProvinceMapBuffer.OceanProvinceId, out byte oceanR, out byte oceanG);
        Assert.Equal(255, oceanR);
        Assert.Equal(255, oceanG);
        Assert.Equal(ProvinceMapBuffer.OceanProvinceId, ProvinceMapBuffer.DecodeProvinceId(oceanR, oceanG));
    }

    [Fact]
    public void ProvincesPng_KoreanPeninsula120Provinces_LosslessRoundtripAndO1MousePicking()
    {
        string root = FindRepositoryRoot();
        string statesPath = Path.Combine(root, "data", "map", "states.json");
        string provincesPath = Path.Combine(root, "data", "map", "provinces.json");
        string provincesPngPath = Path.Combine(root, "data", "map", "provinces.png");

        using var world = new SimulationWorld();
        JsonMapLoader.LoadMap(world, statesPath, provincesPath);

        ProvinceMapBuffer generated = ProvinceMapBuffer.GenerateVoronoiFromWorld(
            world,
            width: 1024,
            height: 1024,
            activeProvinceCount: 120,
            maxCoastalRadius: 60f);

        generated.SaveToPngFile(provincesPngPath);
        Assert.True(File.Exists(provincesPngPath));

        ProvinceMapBuffer loaded = ProvinceMapBuffer.LoadFromPngFile(provincesPngPath);
        Assert.Equal(1024, loaded.Width);
        Assert.Equal(1024, loaded.Height);
        Assert.Equal(generated.RawRgbBytes, loaded.RawRgbBytes);

        // Verify O(1) pixel and normalized UV picking for all 120 Korean Peninsula provinces (0..119)
        for (ushort provId = 0; provId < 120; provId++)
        {
            ref readonly ProvinceData prov = ref world.Provinces[provId];
            ushort pickedByPixel = loaded.PickProvinceAtPixel(prov.CenterX, prov.CenterY);
            Assert.Equal(provId, pickedByPixel);

            float u = (prov.CenterX + 0.5f) / loaded.Width;
            float v = (prov.CenterY + 0.5f) / loaded.Height;
            ushort pickedByUv = loaded.PickProvinceAtUv(u, v);
            Assert.Equal(provId, pickedByUv);
        }

        // Verify ocean pixels and out-of-bounds UVs return OceanProvinceId (0xFFFF)
        Assert.Equal(ProvinceMapBuffer.OceanProvinceId, loaded.PickProvinceAtPixel(10, 10));
        Assert.Equal(ProvinceMapBuffer.OceanProvinceId, loaded.PickProvinceAtPixel(1000, 1000));
        Assert.Equal(ProvinceMapBuffer.OceanProvinceId, loaded.PickProvinceAtUv(0.01f, 0.01f));
        Assert.Equal(ProvinceMapBuffer.OceanProvinceId, loaded.PickProvinceAtUv(-0.1f, 0.5f));
        Assert.Equal(ProvinceMapBuffer.OceanProvinceId, loaded.PickProvinceAtUv(1.2f, 0.5f));
    }

    [Fact]
    public void MapLutBuffer_DynamicColoringAndControllerTagAlpha_UpdatesAcrossAllMapModes()
    {
        string root = FindRepositoryRoot();
        string statesPath = Path.Combine(root, "data", "map", "states.json");
        string provincesPath = Path.Combine(root, "data", "map", "provinces.json");

        using var world = new SimulationWorld();
        JsonMapLoader.LoadMap(world, statesPath, provincesPath);

        var lut = new MapLutBuffer();
        Assert.Equal(2048 * 4, lut.RawRgbaBytes.Length);

        // 1. Political Map Mode
        lut.UpdateFromWorld(world, MapMode.Political);

        Color32 seoulPol = lut[0];      // ROK
        Color32 pyongyangPol = lut[60]; // PRK
        Color32 unownedPol = lut[500];  // Unowned

        Assert.NotEqual(seoulPol, pyongyangPol);
        Assert.Equal(CountryTag.ROK, seoulPol.A);
        Assert.Equal(CountryTag.PRK, pyongyangPol.A);
        Assert.Equal(CountryTag.None, unownedPol.A);

        // Verify raw byte array layout matches Color32[i] (RGBA8)
        Assert.Equal(seoulPol.R, lut.RawRgbaBytes[0]);
        Assert.Equal(seoulPol.G, lut.RawRgbaBytes[1]);
        Assert.Equal(seoulPol.B, lut.RawRgbaBytes[2]);
        Assert.Equal(CountryTag.ROK, lut.RawRgbaBytes[3]);

        // 2. Ownership / Controller change immediately updates LUT color and border Alpha tag
        world.Provinces[0].ControllerTag = CountryTag.PRK;
        lut.UpdateFromWorld(world, MapMode.Political);
        Assert.Equal(pyongyangPol, lut[0]);
        Assert.Equal(CountryTag.PRK, lut[0].A);
        world.Provinces[0].ControllerTag = CountryTag.ROK;

        // 3. Terrain Map Mode (Seoul = Urban, Province 14 = Mountain)
        lut.UpdateFromWorld(world, MapMode.Terrain);
        Color32 seoulTerrain = lut[0];
        Color32 mountainTerrain = lut[14];
        Assert.NotEqual(seoulTerrain, mountainTerrain);
        Assert.Equal(CountryTag.ROK, seoulTerrain.A);

        // 4. Supply Map Mode (Full Supply vs. Depleted Supply)
        world.Provinces[0].SupplyRatio = 1.0f;
        world.Provinces[1].SupplyRatio = 0.1f;
        lut.UpdateFromWorld(world, MapMode.Supply);
        Assert.True(lut[0].G > lut[1].G, "Full supply province should have higher green channel than low supply province.");
        Assert.True(lut[1].R > lut[0].R, "Low supply province should have higher red channel than full supply province.");
        Assert.Equal(CountryTag.ROK, lut[0].A);

        // 5. Industrial Map Mode (Gyeonggi-Seoul State 0 has 20 total factories vs. Gangwon-South State 1 has 5)
        lut.UpdateFromWorld(world, MapMode.Industrial);
        Color32 seoulInd = lut[0];   // State 0 (20 factories)
        Color32 gangwonInd = lut[12]; // State 1 (5 factories)
        Assert.True(seoulInd.G > gangwonInd.G);
        Assert.Equal(CountryTag.ROK, seoulInd.A);

        // 6. Air Superiority Map Mode
        world.Regions[0].SetAirSuperiority(FactionId.BlueforPacom, 0.85f);
        world.Regions[0].SetAirSuperiority(FactionId.RedforSocialist, 0.15f);
        lut.UpdateFromWorld(world, MapMode.AirSuperiority);
        Color32 blueAirColor = lut[0];
        Assert.True(blueAirColor.B > blueAirColor.R);
        Assert.Equal(CountryTag.ROK, blueAirColor.A);
    }
}
