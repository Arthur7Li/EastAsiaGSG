using System.Text;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Loaders;
using Xunit;

namespace EastAsiaGSG.SimTests;

public sealed class MapLoaderTests
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
    public void LoadMap_KoreanPeninsula_LoadsAllStatesAndProvincesSuccessfully()
    {
        string root = FindRepositoryRoot();
        string statesPath = Path.Combine(root, "data", "map", "states.json");
        string provincesPath = Path.Combine(root, "data", "map", "provinces.json");

        using var world = new SimulationWorld();
        JsonMapLoader.LoadMap(world, statesPath, provincesPath);

        // Verify Key ROK State: Gyeonggi-Seoul (State 0)
        ref readonly StateData seoulState = ref world.States[0];
        Assert.Equal(0, seoulState.StateId);
        Assert.Equal(CountryTag.ROK, seoulState.OwnerTag);
        Assert.Equal(CountryTag.ROK, seoulState.ControllerTag);
        Assert.True(seoulState.HasCore(CountryTag.ROK));
        Assert.True(seoulState.HasCore(CountryTag.PRK));
        Assert.Equal(12, seoulState.CivilianFactories);
        Assert.Equal(6, seoulState.MilitaryFactories);
        Assert.Equal(2, seoulState.NavalDockyards);
        Assert.Equal(7, seoulState.AirbaseLevel);
        Assert.Equal(4, seoulState.RadarSamLevel);
        Assert.Equal(15_200_000, seoulState.Population);
        Assert.Equal(10, seoulState.GetResource(ResourceType.Steel));
        Assert.Equal(20, seoulState.GetResource(ResourceType.Semiconductors));

        // Verify Key PRK State: Pyongyang (State 5)
        ref readonly StateData pyongyangState = ref world.States[5];
        Assert.Equal(5, pyongyangState.StateId);
        Assert.Equal(CountryTag.PRK, pyongyangState.OwnerTag);
        Assert.Equal(CountryTag.PRK, pyongyangState.ControllerTag);
        Assert.True(pyongyangState.HasCore(CountryTag.PRK));
        Assert.True(pyongyangState.HasCore(CountryTag.ROK));
        Assert.Equal(10, pyongyangState.MilitaryFactories);
        Assert.Equal(5, pyongyangState.CivilianFactories);
        Assert.Equal(16, pyongyangState.GetResource(ResourceType.Steel));

        // Verify Key Provinces
        // 0: Seoul Central
        ref readonly ProvinceData seoul = ref world.Provinces[0];
        Assert.Equal(0, seoul.ProvinceId);
        Assert.Equal(0, seoul.StateId);
        Assert.Equal(CountryTag.ROK, seoul.OwnerTag);
        Assert.Equal(TerrainType.Urban, seoul.Terrain);
        Assert.Equal(4, seoul.SupplyHubLevel);
        Assert.Equal(0, seoul.PortLevel);

        // 3: Incheon Harbor
        ref readonly ProvinceData incheon = ref world.Provinces[3];
        Assert.Equal(3, incheon.ProvinceId);
        Assert.Equal(6, incheon.PortLevel);

        // 56: Busan Central / Harbor
        ref readonly ProvinceData busan = ref world.Provinces[56];
        Assert.Equal(56, busan.ProvinceId);
        Assert.Equal(4, busan.StateId);
        Assert.Equal(CountryTag.ROK, busan.OwnerTag);
        Assert.Equal(TerrainType.Urban, busan.Terrain);
        Assert.Equal(8, busan.PortLevel);
        Assert.Equal(5, busan.SupplyHubLevel);

        // 60: Pyongyang Central
        ref readonly ProvinceData pyongyang = ref world.Provinces[60];
        Assert.Equal(60, pyongyang.ProvinceId);
        Assert.Equal(5, pyongyang.StateId);
        Assert.Equal(CountryTag.PRK, pyongyang.OwnerTag);
        Assert.Equal(TerrainType.Urban, pyongyang.Terrain);
        Assert.Equal(4, pyongyang.SupplyHubLevel);

        // 102: Wonsan Port
        ref readonly ProvinceData wonsan = ref world.Provinces[102];
        Assert.Equal(102, wonsan.ProvinceId);
        Assert.Equal(8, wonsan.StateId);
        Assert.Equal(CountryTag.PRK, wonsan.OwnerTag);
        Assert.Equal(6, wonsan.PortLevel);

        // 119: Chongjin Steel Titan
        ref readonly ProvinceData chongjin = ref world.Provinces[119];
        Assert.Equal(119, chongjin.ProvinceId);
        Assert.Equal(9, chongjin.StateId);
        Assert.Equal(CountryTag.PRK, chongjin.OwnerTag);
        Assert.Equal(TerrainType.Urban, chongjin.Terrain);
        Assert.Equal(7, chongjin.PortLevel);
    }

    [Fact]
    public void LoadMap_KoreanPeninsula_BuildsValidCsrAdjacencyGraph()
    {
        string root = FindRepositoryRoot();
        string statesPath = Path.Combine(root, "data", "map", "states.json");
        string provincesPath = Path.Combine(root, "data", "map", "provinces.json");

        using var world = new SimulationWorld();
        JsonMapLoader.LoadMap(world, statesPath, provincesPath);

        Assert.Equal(530, world.Graph.EdgeCount);

        // Seoul Central (0) neighbor verification
        ref readonly ProvinceData seoul = ref world.Provinces[0];
        ReadOnlySpan<ushort> seoulNeighbors = world.Graph.GetNeighbors(in seoul);
        Assert.Equal(6, seoulNeighbors.Length);
        Assert.Contains((ushort)1, seoulNeighbors.ToArray());
        Assert.Contains((ushort)2, seoulNeighbors.ToArray());
        Assert.Contains((ushort)4, seoulNeighbors.ToArray());
        Assert.Contains((ushort)5, seoulNeighbors.ToArray());
        Assert.Contains((ushort)8, seoulNeighbors.ToArray());
        Assert.Contains((ushort)9, seoulNeighbors.ToArray());

        // Han River crossing check: Seoul Central (0) <-> Gangnam (2)
        Assert.True(world.Graph.TryGetEdgeFlags(in seoul, 2, out AdjacencyEdgeFlags seoulGangnamFlags));
        Assert.True((seoulGangnamFlags & AdjacencyEdgeFlags.River) != 0);
        Assert.True((seoulGangnamFlags & AdjacencyEdgeFlags.Railway) != 0);

        // DMZ Border Edge check: Paju/Munsan (6) <-> Kaesong City (92)
        ref readonly ProvinceData paju = ref world.Provinces[6];
        ref readonly ProvinceData kaesong = ref world.Provinces[92];
        Assert.True(world.Graph.TryGetEdgeFlags(in paju, 92, out AdjacencyEdgeFlags dmzFlagsFwd));
        Assert.True(world.Graph.TryGetEdgeFlags(in kaesong, 6, out AdjacencyEdgeFlags dmzFlagsRev));
        Assert.Equal(dmzFlagsFwd, dmzFlagsRev);
        Assert.True((dmzFlagsFwd & AdjacencyEdgeFlags.River) != 0);
        Assert.True((dmzFlagsFwd & AdjacencyEdgeFlags.Railway) != 0);

        // Strait check: Mokpo Port (42) <-> Jindo Island (47)
        ref readonly ProvinceData mokpo = ref world.Provinces[42];
        Assert.True(world.Graph.TryGetEdgeFlags(in mokpo, 47, out AdjacencyEdgeFlags straitFlags));
        Assert.True((straitFlags & AdjacencyEdgeFlags.Strait) != 0);

        // Verify all 120 provinces have valid CSR bounds and degrees >= 2
        for (ushort i = 0; i < 120; i++)
        {
            ref readonly ProvinceData prov = ref world.Provinces[i];
            Assert.True(prov.AdjacencyCount >= 2, $"Province {i} degree {prov.AdjacencyCount} < 2");
            Assert.True(prov.AdjacencyOffset >= 0 && prov.AdjacencyOffset + prov.AdjacencyCount <= world.Graph.EdgeCount);

            ReadOnlySpan<ushort> neighbors = world.Graph.GetNeighbors(in prov);
            foreach (ushort neighborId in neighbors)
            {
                Assert.True(neighborId < 120, $"Province {i} has neighbor {neighborId} >= 120");
            }
        }
    }

    [Fact]
    public void LoadMap_KoreanPeninsula_CalculatesAccurateStateResourcesAndFactories()
    {
        string root = FindRepositoryRoot();
        string statesPath = Path.Combine(root, "data", "map", "states.json");
        string provincesPath = Path.Combine(root, "data", "map", "provinces.json");

        using var world = new SimulationWorld();
        JsonMapLoader.LoadMap(world, statesPath, provincesPath);

        // Compute ROK totals across states 0..4
        int rokCiv = 0;
        int rokMil = 0;
        int rokSteel = 0;
        int rokSemi = 0;
        int rokOil = 0;

        for (int i = 0; i <= 4; i++)
        {
            ref readonly StateData s = ref world.States[i];
            rokCiv += s.CivilianFactories;
            rokMil += s.MilitaryFactories;
            rokSteel += s.GetResource(ResourceType.Steel);
            rokSemi += s.GetResource(ResourceType.Semiconductors);
            rokOil += s.GetResource(ResourceType.Oil);
        }

        Assert.Equal(33, rokCiv);
        Assert.Equal(23, rokMil);
        Assert.Equal(56, rokSteel);
        Assert.Equal(44, rokSemi);
        Assert.Equal(12, rokOil);

        // Compute PRK totals across states 5..9
        int prkCiv = 0;
        int prkMil = 0;
        int prkSteel = 0;
        int prkSemi = 0;

        for (int i = 5; i <= 9; i++)
        {
            ref readonly StateData s = ref world.States[i];
            prkCiv += s.CivilianFactories;
            prkMil += s.MilitaryFactories;
            prkSteel += s.GetResource(ResourceType.Steel);
            prkSemi += s.GetResource(ResourceType.Semiconductors);
        }

        Assert.Equal(17, prkCiv);
        Assert.Equal(34, prkMil); // 1985 conventional parity: high PRK military factories
        Assert.Equal(66, prkSteel);
        Assert.Equal(1, prkSemi);
    }

    [Fact]
    public void LoadMap_ThrowsOnAsymmetricAdjacency()
    {
        using var world = new SimulationWorld();

        string statesJson = """
        [
          {
            "stateId": 0,
            "name": "State Zero",
            "ownerTag": "ROK",
            "coreTags": ["ROK"],
            "provinces": [0, 1]
          }
        ]
        """;

        // Province 0 connects to 1, but Province 1 has no reverse edge!
        string provincesJson = """
        [
          {
            "provinceId": 0,
            "name": "Prov Zero",
            "stateId": 0,
            "adjacencies": [
              { "targetProvinceId": 1, "flags": "Standard" }
            ]
          },
          {
            "provinceId": 1,
            "name": "Prov One",
            "stateId": 0,
            "adjacencies": []
          }
        ]
        """;

        byte[] statesBytes = Encoding.UTF8.GetBytes(statesJson);
        byte[] provBytes = Encoding.UTF8.GetBytes(provincesJson);

        var ex = Assert.Throws<MapValidationException>(() =>
            JsonMapLoader.LoadMap(world, statesBytes, provBytes));

        Assert.Contains("Asymmetric adjacency", ex.Message);
    }

    [Fact]
    public void LoadMap_ThrowsOnMismatchedEdgeFlags()
    {
        using var world = new SimulationWorld();

        string statesJson = """
        [
          {
            "stateId": 0,
            "name": "State Zero",
            "ownerTag": "ROK",
            "coreTags": ["ROK"],
            "provinces": [0, 1]
          }
        ]
        """;

        // Province 0 connects to 1 with Railway, but Province 1 -> 0 has only Standard!
        string provincesJson = """
        [
          {
            "provinceId": 0,
            "name": "Prov Zero",
            "stateId": 0,
            "adjacencies": [
              { "targetProvinceId": 1, "flags": "Standard, Railway" }
            ]
          },
          {
            "provinceId": 1,
            "name": "Prov One",
            "stateId": 0,
            "adjacencies": [
              { "targetProvinceId": 0, "flags": "Standard" }
            ]
          }
        ]
        """;

        byte[] statesBytes = Encoding.UTF8.GetBytes(statesJson);
        byte[] provBytes = Encoding.UTF8.GetBytes(provincesJson);

        var ex = Assert.Throws<MapValidationException>(() =>
            JsonMapLoader.LoadMap(world, statesBytes, provBytes));

        Assert.Contains("Mismatched edge flags", ex.Message);
    }

    [Fact]
    public void LoadMap_ThrowsOnSelfLoop()
    {
        using var world = new SimulationWorld();

        string statesJson = """
        [
          {
            "stateId": 0,
            "name": "State Zero",
            "ownerTag": "ROK",
            "coreTags": ["ROK"],
            "provinces": [0]
          }
        ]
        """;

        string provincesJson = """
        [
          {
            "provinceId": 0,
            "name": "Prov Zero",
            "stateId": 0,
            "adjacencies": [
              { "targetProvinceId": 0, "flags": "Standard" }
            ]
          }
        ]
        """;

        byte[] statesBytes = Encoding.UTF8.GetBytes(statesJson);
        byte[] provBytes = Encoding.UTF8.GetBytes(provincesJson);

        var ex = Assert.Throws<MapValidationException>(() =>
            JsonMapLoader.LoadMap(world, statesBytes, provBytes));

        Assert.Contains("self-loop", ex.Message);
    }

    [Fact]
    public void LoadMap_ThrowsOnEmptyLists()
    {
        using var world = new SimulationWorld();
        byte[] emptyJson = Encoding.UTF8.GetBytes("[]");
        byte[] dummyState = Encoding.UTF8.GetBytes("""[{"stateId":0,"name":"S","ownerTag":"ROK","provinces":[0]}]""");
        byte[] dummyProv = Encoding.UTF8.GetBytes("""[{"provinceId":0,"name":"P","stateId":0,"adjacencies":[]}]""");

        Assert.Throws<MapValidationException>(() => JsonMapLoader.LoadMap(world, emptyJson, dummyProv));
        Assert.Throws<MapValidationException>(() => JsonMapLoader.LoadMap(world, dummyState, emptyJson));
    }

    [Fact]
    public void LoadMap_ThrowsOnStateIdOutOfBounds()
    {
        using var world = new SimulationWorld();

        string statesJson = """
        [
          {
            "stateId": 140,
            "name": "Invalid State",
            "ownerTag": "ROK",
            "provinces": [0]
          }
        ]
        """;

        string provincesJson = """
        [
          {
            "provinceId": 0,
            "name": "Prov Zero",
            "stateId": 140,
            "adjacencies": []
          }
        ]
        """;

        byte[] statesBytes = Encoding.UTF8.GetBytes(statesJson);
        byte[] provBytes = Encoding.UTF8.GetBytes(provincesJson);

        var ex = Assert.Throws<MapValidationException>(() =>
            JsonMapLoader.LoadMap(world, statesBytes, provBytes));

        Assert.Contains("exceeds MaxStates limit", ex.Message);
    }

    [Fact]
    public void LoadMap_ThrowsOnUnknownCountryTag()
    {
        using var world = new SimulationWorld();

        string statesJson = """
        [
          {
            "stateId": 0,
            "name": "State Zero",
            "ownerTag": "XYZ_INVALID",
            "coreTags": [],
            "provinces": [0]
          }
        ]
        """;

        string provincesJson = """
        [
          {
            "provinceId": 0,
            "name": "Prov Zero",
            "stateId": 0,
            "adjacencies": []
          }
        ]
        """;

        byte[] statesBytes = Encoding.UTF8.GetBytes(statesJson);
        byte[] provBytes = Encoding.UTF8.GetBytes(provincesJson);

        var ex = Assert.Throws<MapValidationException>(() =>
            JsonMapLoader.LoadMap(world, statesBytes, provBytes));

        Assert.Contains("invalid OwnerTag", ex.Message);
    }
}
