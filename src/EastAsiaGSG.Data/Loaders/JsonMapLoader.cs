using System.Text.Json;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Data.Schemas;

namespace EastAsiaGSG.Data.Loaders;

/// <summary>
/// Deserializes and validates map state and province definitions from JSON,
/// populating SimulationWorld arrays and packing the CSR ProvinceGraph.
/// </summary>
public static class JsonMapLoader
{
    /// <summary>
    /// Loads map states and provinces from JSON files on disk.
    /// </summary>
    public static void LoadMap(SimulationWorld world, string statesJsonPath, string provincesJsonPath)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(statesJsonPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(provincesJsonPath);

        if (!File.Exists(statesJsonPath))
        {
            throw new FileNotFoundException($"States JSON file not found at '{statesJsonPath}'", statesJsonPath);
        }

        if (!File.Exists(provincesJsonPath))
        {
            throw new FileNotFoundException($"Provinces JSON file not found at '{provincesJsonPath}'", provincesJsonPath);
        }

        using FileStream statesStream = File.OpenRead(statesJsonPath);
        using FileStream provincesStream = File.OpenRead(provincesJsonPath);
        LoadMap(world, statesStream, provincesStream);
    }

    /// <summary>
    /// Loads map states and provinces from IO Streams.
    /// </summary>
    public static void LoadMap(SimulationWorld world, Stream statesStream, Stream provincesStream)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(statesStream);
        ArgumentNullException.ThrowIfNull(provincesStream);

        List<StateDto>? states = JsonSerializer.Deserialize(statesStream, MapJsonContext.Default.ListStateDto);
        List<ProvinceDto>? provinces = JsonSerializer.Deserialize(provincesStream, MapJsonContext.Default.ListProvinceDto);

        LoadAndValidate(world, states, provinces);
    }

    /// <summary>
    /// Loads map states and provinces from in-memory UTF-8 JSON byte spans.
    /// </summary>
    public static void LoadMap(SimulationWorld world, ReadOnlySpan<byte> statesUtf8Json, ReadOnlySpan<byte> provincesUtf8Json)
    {
        ArgumentNullException.ThrowIfNull(world);

        List<StateDto>? states = JsonSerializer.Deserialize(statesUtf8Json, MapJsonContext.Default.ListStateDto);
        List<ProvinceDto>? provinces = JsonSerializer.Deserialize(provincesUtf8Json, MapJsonContext.Default.ListProvinceDto);

        LoadAndValidate(world, states, provinces);
    }

    private static void LoadAndValidate(
        SimulationWorld world,
        List<StateDto>? states,
        List<ProvinceDto>? provinces)
    {
        if (states == null || states.Count == 0)
        {
            throw new MapValidationException("State definition list is empty or could not be deserialized.");
        }

        if (provinces == null || provinces.Count == 0)
        {
            throw new MapValidationException("Province definition list is empty or could not be deserialized.");
        }

        var stateMap = new Dictionary<ushort, StateDto>(states.Count);
        var stateProvincesFromState = new Dictionary<ushort, HashSet<ushort>>(states.Count);

        // 1. Validate and index States
        foreach (StateDto state in states)
        {
            if (state.StateId >= SimulationWorld.MaxStates)
            {
                throw new MapValidationException(
                    $"State '{state.Name}' ID {state.StateId} exceeds MaxStates limit ({SimulationWorld.MaxStates}).");
            }

            if (!stateMap.TryAdd(state.StateId, state))
            {
                throw new MapValidationException($"Duplicate StateId {state.StateId} detected in states configuration.");
            }

            if (!CountryTag.TryParse(state.OwnerTag, out _))
            {
                throw new MapValidationException(
                    $"State {state.StateId} ('{state.Name}') has invalid OwnerTag '{state.OwnerTag}'.");
            }

            if (!string.IsNullOrWhiteSpace(state.ControllerTag) && !CountryTag.TryParse(state.ControllerTag, out _))
            {
                throw new MapValidationException(
                    $"State {state.StateId} ('{state.Name}') has invalid ControllerTag '{state.ControllerTag}'.");
            }

            var provSet = new HashSet<ushort>(state.Provinces.Count);
            foreach (ushort provId in state.Provinces)
            {
                if (!provSet.Add(provId))
                {
                    throw new MapValidationException(
                        $"State {state.StateId} lists duplicate ProvinceId {provId}.");
                }
            }
            stateProvincesFromState[state.StateId] = provSet;
        }

        // 2. Validate and index Provinces
        var provinceMap = new Dictionary<ushort, ProvinceDto>(provinces.Count);
        var stateProvincesFromProvinces = new Dictionary<ushort, HashSet<ushort>>(states.Count);

        foreach (ProvinceDto prov in provinces)
        {
            if (prov.ProvinceId >= SimulationWorld.MaxProvinces)
            {
                throw new MapValidationException(
                    $"Province '{prov.Name}' ID {prov.ProvinceId} exceeds MaxProvinces limit ({SimulationWorld.MaxProvinces}).");
            }

            if (!provinceMap.TryAdd(prov.ProvinceId, prov))
            {
                throw new MapValidationException(
                    $"Duplicate ProvinceId {prov.ProvinceId} detected in provinces configuration.");
            }

            if (!stateMap.TryGetValue(prov.StateId, out StateDto? parentState))
            {
                throw new MapValidationException(
                    $"Province {prov.ProvinceId} ('{prov.Name}') references non-existent StateId {prov.StateId}.");
            }

            if (!string.IsNullOrWhiteSpace(prov.OwnerTag) && !CountryTag.TryParse(prov.OwnerTag, out _))
            {
                throw new MapValidationException(
                    $"Province {prov.ProvinceId} has invalid OwnerTag '{prov.OwnerTag}'.");
            }

            if (!string.IsNullOrWhiteSpace(prov.ControllerTag) && !CountryTag.TryParse(prov.ControllerTag, out _))
            {
                throw new MapValidationException(
                    $"Province {prov.ProvinceId} has invalid ControllerTag '{prov.ControllerTag}'.");
            }

            if (!stateProvincesFromProvinces.TryGetValue(prov.StateId, out var provSet))
            {
                provSet = [];
                stateProvincesFromProvinces[prov.StateId] = provSet;
            }
            provSet.Add(prov.ProvinceId);
        }

        // 3. Bijective cross-validation: State.Provinces vs Province.StateId
        foreach (var (stateId, stateProvSet) in stateProvincesFromState)
        {
            if (!stateProvincesFromProvinces.TryGetValue(stateId, out var actualProvSet))
            {
                throw new MapValidationException(
                    $"State {stateId} specifies provinces, but no provinces reference this StateId.");
            }

            foreach (ushort provId in stateProvSet)
            {
                if (!provinceMap.ContainsKey(provId))
                {
                    throw new MapValidationException(
                        $"State {stateId} lists ProvinceId {provId}, which does not exist in provinces list.");
                }
            }

            if (!stateProvSet.SetEquals(actualProvSet))
            {
                throw new MapValidationException(
                    $"State {stateId} province list does not match provinces referencing this state.");
            }
        }

        // 4. Validate Adjacency Edges & Bidirectional Symmetry
        var edgeMap = new Dictionary<(ushort From, ushort To), AdjacencyEdgeFlags>(provinces.Count * 6);

        foreach (ProvinceDto prov in provinces)
        {
            ushort fromId = prov.ProvinceId;

            foreach (ProvinceAdjacencyDto adj in prov.Adjacencies)
            {
                ushort toId = adj.TargetProvinceId;

                if (fromId == toId)
                {
                    throw new MapValidationException(
                        $"Province {fromId} ('{prov.Name}') has a self-loop adjacency edge to itself.");
                }

                if (!provinceMap.ContainsKey(toId))
                {
                    throw new MapValidationException(
                        $"Province {fromId} connects to undefined ProvinceId {toId}.");
                }

                AdjacencyEdgeFlags flags = ParseEdgeFlags(adj.Flags);

                if (!edgeMap.TryAdd((fromId, toId), flags))
                {
                    throw new MapValidationException(
                        $"Province {fromId} has duplicate adjacency edge to Province {toId}.");
                }
            }
        }

        // Check for every (From, To) edge, reverse (To, From) exists with matching flags
        foreach (var ((fromId, toId), forwardFlags) in edgeMap)
        {
            if (!edgeMap.TryGetValue((toId, fromId), out AdjacencyEdgeFlags reverseFlags))
            {
                throw new MapValidationException(
                    $"Asymmetric adjacency: Province {fromId} connects to {toId} with flags [{forwardFlags}], but reverse edge {toId} -> {fromId} does not exist.");
            }

            if (forwardFlags != reverseFlags)
            {
                throw new MapValidationException(
                    $"Mismatched edge flags: Province {fromId} -> {toId} has flags [{forwardFlags}], but reverse edge {toId} -> {fromId} has flags [{reverseFlags}].");
            }
        }

        // 5. Populate SimulationWorld.States
        foreach (StateDto stateDto in states)
        {
            ref StateData state = ref world.States[stateDto.StateId];
            CountryTag.TryParse(stateDto.OwnerTag, out byte ownerTag);
            byte controllerTag = ownerTag;
            if (!string.IsNullOrWhiteSpace(stateDto.ControllerTag) && CountryTag.TryParse(stateDto.ControllerTag, out byte cTag))
            {
                controllerTag = cTag;
            }

            state.StateId = stateDto.StateId;
            state.OwnerTag = ownerTag;
            state.ControllerTag = controllerTag;
            state.CoreTagsMask = 0;
            foreach (string coreTagStr in stateDto.CoreTags)
            {
                if (CountryTag.TryParse(coreTagStr, out byte coreTagId))
                {
                    state.AddCore(coreTagId);
                }
            }

            state.CivilianFactories = stateDto.CivilianFactories;
            state.MilitaryFactories = stateDto.MilitaryFactories;
            state.NavalDockyards = stateDto.NavalDockyards;
            state.AirbaseLevel = stateDto.AirbaseLevel;
            state.RadarSamLevel = stateDto.RadarSamLevel;
            state.InfrastructureLevel = stateDto.InfrastructureLevel;
            state.Population = stateDto.Population;
            state.ConstructionProgress = 0f;
            state.AssignedCivFactories = 0;

            if (stateDto.Resources != null)
            {
                state.SetResource(ResourceType.Oil, stateDto.Resources.Oil);
                state.SetResource(ResourceType.Steel, stateDto.Resources.Steel);
                state.SetResource(ResourceType.Aluminum, stateDto.Resources.Aluminum);
                state.SetResource(ResourceType.Semiconductors, stateDto.Resources.Semiconductors);
            }
            else
            {
                state.SetResource(ResourceType.Oil, 0);
                state.SetResource(ResourceType.Steel, 0);
                state.SetResource(ResourceType.Aluminum, 0);
                state.SetResource(ResourceType.Semiconductors, 0);
            }
        }

        // 6. Populate SimulationWorld.Provinces
        foreach (ProvinceDto provDto in provinces)
        {
            ref ProvinceData prov = ref world.Provinces[provDto.ProvinceId];
            ref readonly StateData parentState = ref world.States[provDto.StateId];

            byte ownerTag = parentState.OwnerTag;
            if (!string.IsNullOrWhiteSpace(provDto.OwnerTag) && CountryTag.TryParse(provDto.OwnerTag, out byte pOwner))
            {
                ownerTag = pOwner;
            }

            byte controllerTag = parentState.ControllerTag;
            if (!string.IsNullOrWhiteSpace(provDto.ControllerTag) && CountryTag.TryParse(provDto.ControllerTag, out byte pCtrl))
            {
                controllerTag = pCtrl;
            }

            prov.ProvinceId = provDto.ProvinceId;
            prov.StateId = provDto.StateId;
            prov.OwnerTag = ownerTag;
            prov.ControllerTag = controllerTag;
            prov.Terrain = ParseTerrain(provDto.Terrain);
            prov.StrategicRegionId = provDto.StrategicRegionId;
            prov.SupplyHubLevel = provDto.SupplyHubLevel;
            prov.PortLevel = provDto.PortLevel;
            prov.InfrastructureLevel = provDto.InfrastructureLevel;
            prov.CenterX = provDto.CenterX;
            prov.CenterY = provDto.CenterY;
            prov.SupplyRatio = 1.0f;
        }

        // 7. Pack CSR ProvinceGraph
        var directedEdges = new DirectedEdge[edgeMap.Count];
        int edgeIndex = 0;
        foreach (var ((fromId, toId), flags) in edgeMap)
        {
            directedEdges[edgeIndex++] = new DirectedEdge(fromId, toId, flags);
        }

        world.Graph.BuildCsr(world.Provinces.AsSpan(), directedEdges.AsSpan());
    }

    public static AdjacencyEdgeFlags ParseEdgeFlags(string? flagsStr)
    {
        if (string.IsNullOrWhiteSpace(flagsStr))
        {
            return AdjacencyEdgeFlags.Standard;
        }

        AdjacencyEdgeFlags result = AdjacencyEdgeFlags.None;
        ReadOnlySpan<char> span = flagsStr.AsSpan();

        int start = 0;
        while (start < span.Length)
        {
            while (start < span.Length && (span[start] == ' ' || span[start] == ',' || span[start] == '|' || span[start] == ';'))
            {
                start++;
            }
            if (start >= span.Length)
            {
                break;
            }

            int end = start;
            while (end < span.Length && span[end] != ' ' && span[end] != ',' && span[end] != '|' && span[end] != ';')
            {
                end++;
            }

            ReadOnlySpan<char> token = span.Slice(start, end - start);
            if (token.Equals("Standard".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                result |= AdjacencyEdgeFlags.Standard;
            }
            else if (token.Equals("River".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                result |= AdjacencyEdgeFlags.River;
            }
            else if (token.Equals("Railway".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                result |= AdjacencyEdgeFlags.Railway;
            }
            else if (token.Equals("Strait".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                result |= AdjacencyEdgeFlags.Strait;
            }
            else if (token.Equals("None".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                // Explicitly none
            }
            else
            {
                throw new MapValidationException($"Unknown adjacency edge flag: '{token.ToString()}'");
            }

            start = end;
        }

        if (result == AdjacencyEdgeFlags.None)
        {
            result = AdjacencyEdgeFlags.Standard;
        }
        else if ((result & AdjacencyEdgeFlags.Strait) == 0 && (result & AdjacencyEdgeFlags.Standard) == 0)
        {
            // Standard traversable land edge modifier (e.g. River or Railway implies standard land edge unless Strait)
            result |= AdjacencyEdgeFlags.Standard;
        }

        return result;
    }

    public static TerrainType ParseTerrain(string? terrainStr)
    {
        if (string.IsNullOrWhiteSpace(terrainStr))
        {
            return TerrainType.Plains;
        }

        if (Enum.TryParse<TerrainType>(terrainStr, ignoreCase: true, out var terrain))
        {
            return terrain;
        }

        throw new MapValidationException($"Unknown terrain type: '{terrainStr}'");
    }
}
