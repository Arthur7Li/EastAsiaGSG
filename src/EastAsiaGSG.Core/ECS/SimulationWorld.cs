using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.Math;
using EastAsiaGSG.Core.Systems;

namespace EastAsiaGSG.Core.ECS;

/// <summary>
/// Headless Data-Oriented simulation kernel holding flat contiguous state arrays,
/// the CSR province adjacency graph, the deterministic command queue, and the Arch ECS World.
/// </summary>
public sealed class SimulationWorld : IDisposable
{
    public const int MaxProvinces = 1500;
    public const int MaxStates = 140;
    public const int MaxCountries = 16;
    public const int MaxStrategicRegions = 18;
    public const int MaxDivisionTemplates = 64;
    public const int MaxFocusesPerCountry = 128;
    public const int MaxTriggeredNewsEvents = 64;
    public const int DefaultStartingManpower = 250_000;

    public const int StartYear = 1985;
    public const int StartMonth = 1;
    public const int StartDay = 1;
    public const int StartHour = 0;

    private static ReadOnlySpan<byte> DaysPerMonthStandard => [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();
    private static readonly QueryDescription CombatQuery = new QueryDescription().WithAll<ActiveCombatComponent>();
    private static readonly QueryDescription ProductionLineQuery = new QueryDescription().WithAll<ProductionLineComponent>();
    private static readonly QueryDescription TaskForceQuery = new QueryDescription().WithAll<NavalTaskForceComponent>();
    private static readonly FocusNodeData EmptyFocusNode = default;

    // Flat Contiguous State Arrays (Index == ID)
    public readonly ProvinceData[] Provinces = new ProvinceData[MaxProvinces];
    public readonly StateData[] States = new StateData[MaxStates];
    public readonly CountryState[] Countries = new CountryState[MaxCountries];
    public readonly StrategicRegionData[] Regions = new StrategicRegionData[MaxStrategicRegions];
    public readonly DivisionTemplateRecord[] Templates = new DivisionTemplateRecord[MaxDivisionTemplates];
    public readonly FocusNodeData[] FocusTrees = new FocusNodeData[MaxCountries * MaxFocusesPerCountry];
    public readonly ushort[] CountryFocusCounts = new ushort[MaxCountries];
    public readonly ushort[] CountryCapitals = new ushort[MaxCountries];
    public readonly float[] CountryHomelandEscalation = new float[MaxCountries];
    public readonly ushort[] CountryWarMasks = new ushort[MaxCountries];
    public readonly ushort[] CountryWargoalMasks = new ushort[MaxCountries];
    public ushort CountryCapitulatedMask;
    public readonly TacticalDoctrine[] CountryDoctrines = new TacticalDoctrine[MaxCountries];
    public readonly float[] SeaZoneInterdictionPenalty = new float[MaxStrategicRegions];
    public readonly byte[] CountryUnlockedTechGen = new byte[MaxCountries * CountryState.ArchetypeCount];
    public readonly byte[] CountryTechBonuses = new byte[MaxCountries * CountryState.ArchetypeCount];
    public readonly int[] TriggeredNewsEventHashes = new int[MaxTriggeredNewsEvents];
    public readonly byte[] TriggeredNewsEventCountries = new byte[MaxTriggeredNewsEvents];

    // Global CSR Adjacency Graph for Provinces
    public readonly ProvinceGraph Graph = new();
    public ushort[] AdjacencyTargets => Graph.AdjacencyTargets;
    public byte[] AdjacencyEdgeFlags => Graph.AdjacencyEdgeFlags;

    // High-Performance Zero-Allocation A* Pathfinder
    public readonly ProvincePathfinder Pathfinder = new();

    // Allocation-Free Deterministic Command Queue
    public readonly CommandRingBuffer CommandQueue = new(4096);

    // Arch ECS World for Dynamic Entities (Divisions, ActiveCombats, ProductionLines)
    public readonly World EcsWorld = World.Create();

    // High-Performance Zero-Allocation CSR Spatial Index for Division Entities (O(1) province lookups)
    public readonly ProvinceSpatialIndex SpatialIndex = new();

    // Timestamp & Stratified Tick Telemetry (Starts 1985-01-01 00:00)
    public int TotalSixHourTicks { get; private set; }
    public int DailyTicksExecuted { get; private set; }
    public int MonthlyTicksExecuted { get; private set; }
    public int CommandsProcessedCount { get; private set; }
    public int TriggeredNewsEventCount { get; private set; }

    /// <summary>
    /// 16-bit mask of country tags (0..14) controlled by the Two-Tier Utility AI (StrategicMinisterAISystem &amp; TacticalFrontlineAISystem).
    /// Defaults to 0 so isolated unit tests remain unaffected unless AI is explicitly enabled.
    /// </summary>
    public ushort AiControlledCountryMask { get; set; }

    public int CurrentYear { get; private set; } = StartYear;
    public int CurrentMonth { get; private set; } = StartMonth;
    public int CurrentDay { get; private set; } = StartDay;
    public int CurrentHour { get; private set; } = StartHour;

    // Monotonic Dirty-Revision Counters for Reactive Presentation Cache Invalidation
    public uint OwnershipRevision { get; private set; } = 1;
    public uint SupplyRevision { get; private set; } = 1;
    public uint EconomyRevision { get; private set; } = 1;
    public uint AirNavalRevision { get; private set; } = 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InvalidateOwnership() => OwnershipRevision++;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InvalidateSupply() => SupplyRevision++;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InvalidateEconomy() => EconomyRevision++;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InvalidateAirNaval() => AirNavalRevision++;

    private int _nextCombatId = 1;
    private int _nextDivisionId = 10_000;
    private int _nextProductionLineId = 1_000;
    private bool _disposed;

    public SimulationWorld()
    {
        InitializeDefaultArrays();
    }

    private void InitializeDefaultArrays()
    {
        CountryCapitulatedMask = 0;

        for (ushort i = 0; i < MaxProvinces; i++)
        {
            Provinces[i].ProvinceId = i;
            Provinces[i].OwnerTag = CountryTag.None;
            Provinces[i].ControllerTag = CountryTag.None;
            Provinces[i].SupplyRatio = 1.0f;
        }

        for (ushort i = 0; i < MaxStates; i++)
        {
            States[i].StateId = i;
            States[i].OwnerTag = CountryTag.None;
            States[i].ControllerTag = CountryTag.None;
        }

        for (byte i = 0; i < MaxCountries; i++)
        {
            Countries[i].TagId = i;
            Countries[i].Faction = FactionId.Neutral;
            Countries[i].Defcon = 5;
            Countries[i].HomelandEscalation = i == CountryTag.USA ? (byte)15 : (byte)0;
            CountryHomelandEscalation[i] = i == CountryTag.USA ? 15.0f : 0.0f;
            Countries[i].Stability = 0.70f;
            Countries[i].WarSupport = 0.50f;
            Countries[i].ConsumerGoodsRatio = 0.25f;
            Countries[i].Manpower = DefaultStartingManpower;
            Countries[i].ActiveFocusId = -1;
            Countries[i].FocusDaysRemaining = 0;
        }

        Countries[CountryTag.USA].Faction = FactionId.BlueforPacom;
        Countries[CountryTag.ROK].Faction = FactionId.BlueforPacom;
        Countries[CountryTag.JPN].Faction = FactionId.BlueforPacom;
        Countries[CountryTag.TWN].Faction = FactionId.BlueforPacom;
        Countries[CountryTag.PHL].Faction = FactionId.BlueforPacom;
        Countries[CountryTag.SOV].Faction = FactionId.RedforSocialist;
        Countries[CountryTag.PRK].Faction = FactionId.RedforSocialist;
        Countries[CountryTag.VNM].Faction = FactionId.RedforSocialist;
        Countries[CountryTag.LAO].Faction = FactionId.RedforSocialist;
        Countries[CountryTag.PRC].Faction = FactionId.PrcSphere;

        for (ushort i = 0; i < MaxDivisionTemplates; i++)
        {
            Templates[i].TemplateId = i;
            Templates[i].CombatWidth = 20;
            Templates[i].IsConfigured = 0;
            Templates[i].CountryTag = CountryTag.None;
        }

        for (byte i = 0; i < MaxStrategicRegions; i++)
        {
            Regions[i].RegionId = i;
            Regions[i].WeatherEfficiencyModifier = 1.0f;
        }

        for (int i = 0; i < CountryUnlockedTechGen.Length; i++)
        {
            CountryUnlockedTechGen[i] = (byte)TechGeneration.Gen1_1980Baseline;
        }

        for (byte i = 0; i < MaxCountries; i++)
        {
            CountryDoctrines[i] = TacticalDoctrine.Standard;
        }
        CountryDoctrines[CountryTag.USA] = TacticalDoctrine.AirLandBattle;
        CountryDoctrines[CountryTag.SOV] = TacticalDoctrine.DeepOperations;
        CountryDoctrines[CountryTag.PRC] = TacticalDoctrine.MassAssault;
        CountryDoctrines[CountryTag.PRK] = TacticalDoctrine.MassAssault;
        CountryDoctrines[CountryTag.ROK] = TacticalDoctrine.ElasticDefense;
        CountryDoctrines[CountryTag.TWN] = TacticalDoctrine.ElasticDefense;
        CountryDoctrines[CountryTag.JPN] = TacticalDoctrine.ElasticDefense;

        for (byte i = 0; i < MaxCountries; i++)
        {
            CountryCapitals[i] = ushort.MaxValue;
        }
        CountryCapitals[CountryTag.ROK] = 0;   // Seoul
        CountryCapitals[CountryTag.PRK] = 60;  // Pyongyang
        CountryCapitals[CountryTag.SOV] = 0;   // Trans-Siberian Irkutsk/Chita Artery Entry Node
        CountryCapitals[CountryTag.USA] = 3;   // PACOM Naval Entry Port
    }

    /// <summary>
    /// Returns the current simulation date encoded as an 8-digit integer YYYYMMDD (e.g. 19850101).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCurrentDateInt()
    {
        return (CurrentYear * 10000) + (CurrentMonth * 100) + CurrentDay;
    }

    /// <summary>
    /// Sets the simulation calendar date and hour directly (used by scenario setup and historical date tests).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetDate(int year, int month, int day, int hour = 0)
    {
        CurrentYear = year;
        CurrentMonth = System.Math.Clamp(month, 1, 12);
        int maxDays = GetDaysInMonth(CurrentYear, CurrentMonth);
        CurrentDay = System.Math.Clamp(day, 1, maxDays);
        CurrentHour = (hour / 6) * 6 % 24;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort GetCapitalProvince(byte countryTag)
    {
        if (countryTag < MaxCountries)
        {
            ushort capital = CountryCapitals[countryTag];
            if (capital < MaxProvinces)
            {
                return capital;
            }
        }
        return ushort.MaxValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int AllocateNextCombatId()
    {
        return _nextCombatId++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AllocateNextDivisionId()
    {
        return _nextDivisionId++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AllocateNextProductionLineId()
    {
        return _nextProductionLineId++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasCapitulated(byte countryTag)
    {
        return countryTag < MaxCountries && (CountryCapitulatedMask & (1 << countryTag)) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetCapitulated(byte countryTag, bool capitulated)
    {
        if (countryTag < MaxCountries)
        {
            if (capitulated)
            {
                CountryCapitulatedMask |= (ushort)(1 << countryTag);
            }
            else
            {
                CountryCapitulatedMask &= (ushort)~(1 << countryTag);
            }
        }
    }

    /// <summary>
    /// Registers or updates a FocusNodeData entry in the flat FocusTrees table.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RegisterFocusNode(in FocusNodeData node)
    {
        if (node.CountryTag < MaxCountries && (uint)node.FocusIndex < MaxFocusesPerCountry)
        {
            int slot = (node.CountryTag * MaxFocusesPerCountry) + node.FocusIndex;
            FocusTrees[slot] = node;
            FocusTrees[slot].IsConfigured = 1;

            ushort nextCount = (ushort)(node.FocusIndex + 1);
            if (nextCount > CountryFocusCounts[node.CountryTag])
            {
                CountryFocusCounts[node.CountryTag] = nextCount;
            }
        }
    }

    /// <summary>
    /// Returns a read-only reference to a country's FocusNodeData by index (0..127).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly FocusNodeData GetFocusNode(byte countryTag, int focusIndex)
    {
        if (countryTag < MaxCountries && (uint)focusIndex < MaxFocusesPerCountry)
        {
            return ref FocusTrees[(countryTag * MaxFocusesPerCountry) + focusIndex];
        }
        return ref EmptyFocusNode;
    }

    /// <summary>
    /// Searches a country's configured focus nodes for a matching FocusIdHash with zero heap allocations.
    /// </summary>
    public bool TryFindFocusIndexByHash(byte countryTag, int focusIdHash, out short focusIndex)
    {
        if (countryTag < MaxCountries)
        {
            int baseOffset = countryTag * MaxFocusesPerCountry;
            int limit = CountryFocusCounts[countryTag];
            for (short i = 0; i < limit; i++)
            {
                ref readonly FocusNodeData node = ref FocusTrees[baseOffset + i];
                if (node.IsConfigured != 0 && node.FocusIdHash == focusIdHash)
                {
                    focusIndex = i;
                    return true;
                }
            }
        }

        focusIndex = -1;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DeclareWar(byte tagA, byte tagB)
    {
        if (tagA < MaxCountries && tagB < MaxCountries && tagA != tagB)
        {
            CountryWarMasks[tagA] |= (ushort)(1 << tagB);
            CountryWarMasks[tagB] |= (ushort)(1 << tagA);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MakePeace(byte tagA, byte tagB)
    {
        if (tagA < MaxCountries && tagB < MaxCountries)
        {
            CountryWarMasks[tagA] &= (ushort)~(1 << tagB);
            CountryWarMasks[tagB] &= (ushort)~(1 << tagA);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAtWarWith(byte tagA, byte tagB)
    {
        if (tagA < MaxCountries && tagB < MaxCountries)
        {
            return (CountryWarMasks[tagA] & (1 << tagB)) != 0;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the country has an active diplomatic war bitmask or is engaged in an ongoing combat.
    /// </summary>
    public bool IsAtWar(byte countryTag)
    {
        if (countryTag >= MaxCountries)
        {
            return false;
        }

        if (CountryWarMasks[countryTag] != 0)
        {
            return true;
        }

        Query query = EcsWorld.Query(in CombatQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<ActiveCombatComponent> combats = chunk.GetSpan<ActiveCombatComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                ref readonly ActiveCombatComponent combat = ref combats[i];
                if (combat.Outcome == CombatOutcome.Ongoing &&
                    (combat.AttackerTag == countryTag || combat.DefenderTag == countryTag))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GrantWargoal(byte actorTag, byte targetTag)
    {
        if (actorTag < MaxCountries && targetTag < MaxCountries && actorTag != targetTag)
        {
            CountryWargoalMasks[actorTag] |= (ushort)(1 << targetTag);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasWargoal(byte actorTag, byte targetTag)
    {
        if (actorTag < MaxCountries && targetTag < MaxCountries)
        {
            return (CountryWargoalMasks[actorTag] & (1 << targetTag)) != 0;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TechGeneration GetUnlockedTechGeneration(byte countryTag, EquipmentType archetype)
    {
        if (countryTag < MaxCountries && (uint)archetype < CountryState.ArchetypeCount)
        {
            return (TechGeneration)CountryUnlockedTechGen[(countryTag << 3) | (int)archetype];
        }
        return TechGeneration.Gen1_1980Baseline;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUnlockedTechGeneration(byte countryTag, EquipmentType archetype, TechGeneration generation)
    {
        if (countryTag < MaxCountries && (uint)archetype < CountryState.ArchetypeCount)
        {
            byte clamped = (byte)System.Math.Clamp((int)generation, 0, CountryState.TechGenerationCount - 1);
            CountryUnlockedTechGen[(countryTag << 3) | (int)archetype] = clamped;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetTechBonusCount(byte countryTag, EquipmentType archetype)
    {
        if (countryTag < MaxCountries && (uint)archetype < CountryState.ArchetypeCount)
        {
            return CountryTechBonuses[(countryTag << 3) | (int)archetype];
        }
        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncrementTechBonusCount(byte countryTag, EquipmentType archetype, byte count = 1)
    {
        if (countryTag < MaxCountries && (uint)archetype < CountryState.ArchetypeCount)
        {
            int idx = (countryTag << 3) | (int)archetype;
            int updated = CountryTechBonuses[idx] + count;
            CountryTechBonuses[idx] = (byte)(updated > 255 ? 255 : updated);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordNewsEvent(int eventIdHash, byte countryTag)
    {
        int slot = TriggeredNewsEventCount % MaxTriggeredNewsEvents;
        TriggeredNewsEventHashes[slot] = eventIdHash;
        TriggeredNewsEventCountries[slot] = countryTag;
        TriggeredNewsEventCount++;
    }

    public bool HasTriggeredNewsEvent(int eventIdHash)
    {
        int count = TriggeredNewsEventCount < MaxTriggeredNewsEvents
            ? TriggeredNewsEventCount
            : MaxTriggeredNewsEvents;
        for (int i = 0; i < count; i++)
        {
            if (TriggeredNewsEventHashes[i] == eventIdHash)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Registers or updates a DivisionTemplateRecord in the flat Templates table.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RegisterDivisionTemplate(in DivisionTemplateRecord template)
    {
        if (template.TemplateId < MaxDivisionTemplates)
        {
            Templates[template.TemplateId] = template;
            Templates[template.TemplateId].IsConfigured = 1;
        }
    }

    /// <summary>
    /// Spawns a DivisionComponent entity in EcsWorld.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity SpawnDivision(in DivisionComponent division)
    {
        return EcsWorld.Create(division);
    }

    /// <summary>
    /// Finds a division entity by prioritizing DivisionId first, falling back to ECS Entity.Id with zero heap allocations.
    /// </summary>
    public bool TryFindDivisionById(int divisionOrEntityId, out Entity foundEntity)
    {
        // Phase 1: Search by domain DivisionId across all chunks
        Query query1 = EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query1)
        {
            Span<DivisionComponent> divisions = chunk.GetSpan<DivisionComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (divisions[i].DivisionId == divisionOrEntityId)
                {
                    foundEntity = entities[i];
                    return true;
                }
            }
        }

        // Phase 2: Fallback search by internal ECS Entity.Id across all chunks
        Query query2 = EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query2)
        {
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (entities[i].Id == divisionOrEntityId)
                {
                    foundEntity = entities[i];
                    return true;
                }
            }
        }

        foundEntity = default;
        return false;
    }

    /// <summary>
    /// Finds an active combat entity at the specified contested province.
    /// </summary>
    public bool TryFindActiveCombatAtProvince(ushort targetProvinceId, out Entity combatEntity)
    {
        Query query = EcsWorld.Query(in CombatQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<ActiveCombatComponent> combats = chunk.GetSpan<ActiveCombatComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (combats[i].TargetProvinceId == targetProvinceId)
                {
                    combatEntity = entities[i];
                    return true;
                }
            }
        }

        combatEntity = default;
        return false;
    }

    /// <summary>
    /// Spawns a discrete NavalTaskForceComponent entity in EcsWorld.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity SpawnNavalTaskForce(in NavalTaskForceComponent taskForce)
    {
        return EcsWorld.Create(taskForce);
    }

    /// <summary>
    /// Spawns a discrete AirWingComponent entity in EcsWorld.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity SpawnAirWing(in AirWingComponent wing)
    {
        return EcsWorld.Create(wing);
    }

    /// <summary>
    /// Spawns a ProductionLineComponent entity in EcsWorld.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity SpawnProductionLine(in ProductionLineComponent line)
    {
        return EcsWorld.Create(line);
    }

    /// <summary>
    /// Finds a production line entity by prioritizing LineId first, falling back to ECS Entity.Id with zero heap allocations.
    /// </summary>
    public bool TryFindProductionLineById(int lineOrEntityId, out Entity foundEntity)
    {
        // Phase 1: Search by domain LineId across all chunks
        Query query1 = EcsWorld.Query(in ProductionLineQuery);
        foreach (ref Chunk chunk in query1)
        {
            Span<ProductionLineComponent> lines = chunk.GetSpan<ProductionLineComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (lines[i].LineId == lineOrEntityId)
                {
                    foundEntity = entities[i];
                    return true;
                }
            }
        }

        // Phase 2: Fallback search by internal ECS Entity.Id across all chunks
        Query query2 = EcsWorld.Query(in ProductionLineQuery);
        foreach (ref Chunk chunk in query2)
        {
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (entities[i].Id == lineOrEntityId)
                {
                    foundEntity = entities[i];
                    return true;
                }
            }
        }

        foundEntity = default;
        return false;
    }

    /// <summary>
    /// Finds a naval task force entity by prioritizing TaskForceId first, falling back to ECS Entity.Id with zero heap allocations.
    /// </summary>
    public bool TryFindNavalTaskForceById(int taskForceOrEntityId, out Entity foundEntity)
    {
        Query query1 = EcsWorld.Query(in TaskForceQuery);
        foreach (ref Chunk chunk in query1)
        {
            Span<NavalTaskForceComponent> tfs = chunk.GetSpan<NavalTaskForceComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (tfs[i].TaskForceId == taskForceOrEntityId)
                {
                    foundEntity = entities[i];
                    return true;
                }
            }
        }

        Query query2 = EcsWorld.Query(in TaskForceQuery);
        foreach (ref Chunk chunk in query2)
        {
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (entities[i].Id == taskForceOrEntityId)
                {
                    foundEntity = entities[i];
                    return true;
                }
            }
        }

        foundEntity = default;
        return false;
    }

    /// <summary>
    /// Recalculates and updates States[stateId].ControllerTag based on the majority controller of provinces in stateId.
    /// Urban and SupplyHub provinces carry higher strategic weight. Zero heap allocations.
    /// </summary>
    public void RecalculateStateController(ushort stateId)
    {
        if (stateId >= MaxStates)
        {
            return;
        }

        Span<int> tagWeights = stackalloc int[MaxCountries];
        int bestWeight = 0;
        byte bestController = States[stateId].ControllerTag;

        for (ushort p = 0; p < MaxProvinces; p++)
        {
            ref readonly ProvinceData prov = ref Provinces[p];
            if (prov.StateId != stateId || prov.OwnerTag == CountryTag.None || prov.Terrain == TerrainType.Ocean)
            {
                continue;
            }

            byte ctrl = prov.ControllerTag;
            if (ctrl < MaxCountries)
            {
                int weight = 1 + (prov.SupplyHubLevel * 2) + (prov.Terrain == TerrainType.Urban ? 3 : 0);
                tagWeights[ctrl] += weight;
                if (tagWeights[ctrl] > bestWeight)
                {
                    bestWeight = tagWeights[ctrl];
                    bestController = ctrl;
                }
            }
        }

        if (bestWeight > 0)
        {
            States[stateId].ControllerTag = bestController;
        }
    }

    /// <summary>
    /// Recruits a new division from national manpower and equipment stockpile, deploying it at requestedProvinceId or nation's capital.
    /// </summary>
    public bool TryRecruitDivisionFromStockpile(byte countryTag, ushort templateId, ushort requestedProvinceId, out Entity spawnedEntity)
    {
        spawnedEntity = default;
        if (countryTag >= MaxCountries)
        {
            return false;
        }

        ref CountryState country = ref Countries[countryTag];
        if (templateId >= MaxDivisionTemplates || Templates[templateId].IsConfigured == 0)
        {
            templateId = DivisionTemplateRecord.DefaultInfantryTemplateId;
            if (Templates[templateId].IsConfigured == 0)
            {
                Templates[templateId] = DivisionTemplateRecord.CreateInfantryDivision(templateId);
            }
        }

        ref readonly DivisionTemplateRecord tpl = ref Templates[templateId];
        int reqManpower = tpl.TotalManpower > 0 ? tpl.TotalManpower : 10_000;
        if (country.Manpower < reqManpower / 2)
        {
            return false;
        }

        int manpowerToDeduct = country.Manpower >= reqManpower ? reqManpower : country.Manpower;
        country.Manpower -= manpowerToDeduct;

        for (int a = 0; a < DivisionTemplateRecord.LandArchetypeCount; a++)
        {
            EquipmentType arch = (EquipmentType)a;
            ushort required = tpl.GetRequired(arch);
            if (required > 0)
            {
                ReinforcementSystem.ConsumeArchetypeFromStockpile(ref country, arch, required);
            }
        }

        ushort spawnProv = requestedProvinceId;
        if (spawnProv >= MaxProvinces || Provinces[spawnProv].ControllerTag != countryTag)
        {
            spawnProv = GetCapitalProvince(countryTag);
        }
        if (spawnProv >= MaxProvinces)
        {
            for (ushort p = 0; p < MaxProvinces; p++)
            {
                if (Provinces[p].ControllerTag == countryTag && Provinces[p].Terrain != TerrainType.Ocean)
                {
                    spawnProv = p;
                    break;
                }
            }
        }
        if (spawnProv >= MaxProvinces)
        {
            spawnProv = 0;
        }

        TechGeneration techGen = GetUnlockedTechGeneration(countryTag, EquipmentType.InfantryKit);
        tpl.ComputeCombatStats(techGen,
            out float softAttack,
            out float hardAttack,
            out float defense,
            out float breakthrough,
            out float armor,
            out float piercing,
            out float hardness,
            out float speed);

        ushort mbtReq = tpl.GetRequired(EquipmentType.MBT);
        ushort ifvReq = tpl.GetRequired(EquipmentType.IFV);
        ushort heliReq = tpl.GetRequired(EquipmentType.AttackHeli);

        int divId = AllocateNextDivisionId();
        DivisionComponent div = DivisionComponent.Create(
            divisionId: divId,
            countryTag: countryTag,
            startingProvinceId: spawnProv,
            templateId: templateId,
            combatWidth: tpl.CombatWidth,
            softAttack: softAttack,
            hardAttack: hardAttack,
            defense: defense,
            breakthrough: breakthrough,
            armor: armor,
            piercing: piercing,
            hardness: hardness,
            maxOrganization: 60.0f,
            startingStrength: 1.0f,
            startingExperience: 0.30f,
            speed: speed,
            pgmAuthorized: mbtReq > 0 || ifvReq > 0 || heliReq > 0);

        spawnedEntity = SpawnDivision(in div);
        return true;
    }

    /// <summary>
    /// Enqueues a deterministic command to be executed at the start of the next 6-hour tick.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EnqueueCommand(in SimulationCommand command)
    {
        return CommandQueue.TryEnqueue(in command);
    }

    /// <summary>
    /// Computes the optimal path between two provinces using the built-in zero-allocation pathfinder.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PathResult FindPath(in PathQuery query, Span<ushort> destinationPath)
    {
        return Pathfinder.FindPath(this, in query, destinationPath);
    }

    /// <summary>
    /// Computes the optimal path between two provinces using thread-local zero-allocation pathfinder buffers.
    /// Safe for concurrent calls from rendering UI and background simulation threads.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PathResult FindPathThreadSafe(in PathQuery query, Span<ushort> destinationPath)
    {
        return ProvincePathfinder.FindPathThreadSafe(this, in query, destinationPath);
    }

    /// <summary>
    /// Executes one 6-hour simulation step, routing to Tier 1 (Every 6h), Tier 2 (Daily at 00:00),
    /// and Tier 3 (Monthly on the 1st at 00:00) before advancing the simulation clock by 6 hours.
    /// </summary>
    public void StepSixHours()
    {
        // Tier 1: Every 6 Hours (00:00, 06:00, 12:00, 18:00)
        ExecuteSixHourTier();

        // Tier 2: Daily Tick (Executed at 00:00)
        if (CurrentHour == 0)
        {
            ExecuteDailyTier();

            // Tier 3: Monthly Tick (Executed on the 1st of each month at 00:00)
            if (CurrentDay == 1)
            {
                ExecuteMonthlyTier();
            }
        }

        AdvanceClockSixHours();
        TotalSixHourTicks++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteSixHourTier()
    {
        // 1. CommandProcessorSystem: Drain player and AI queued commands
        DrainCommandQueue();

        // 2. Spatial Index: Rebuild zero-allocation CSR division index across provinces in O(D) time
        SpatialIndex.Rebuild(this);

        // 3. DivisionMovementSystem: Advance movement along CSR edges & trigger combats
        DivisionMovementSystem.Execute(this);

        // 4. CombatResolutionSystem: Resolve 6-hour HOI4 land combat math
        CombatResolutionSystem.Execute(this);

        // 5. AirAndNavalRegionSystem (Phase 4.2): Update air superiority, naval control & A2/AD
        AirAndNavalRegionSystem.Execute(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DrainCommandQueue()
    {
        while (CommandQueue.TryDequeue(out SimulationCommand cmd))
        {
            CommandsProcessedCount++;
            if (cmd.CountryTag >= MaxCountries)
            {
                continue;
            }

            switch (cmd.Type)
            {
                case CommandType.SelectFocus:
                {
                    FocusTreeSystem.TryStartFocus(this, cmd.CountryTag, (short)cmd.TargetId, (short)cmd.IntParam0);
                    break;
                }
                case CommandType.ShiftDefcon:
                {
                    ref CountryState country = ref Countries[cmd.CountryTag];
                    byte targetDefcon = (byte)cmd.TargetId;
                    if (targetDefcon is >= 1 and <= 5)
                    {
                        country.Defcon = targetDefcon;
                    }
                    break;
                }
                case CommandType.AssignFactory:
                {
                    if (cmd.TargetId < MaxStates)
                    {
                        ref StateData state = ref States[cmd.TargetId];
                        int clamped = cmd.IntParam0 < 0 ? 0 : (cmd.IntParam0 > 15 ? 15 : cmd.IntParam0);
                        state.AssignedCivFactories = (ushort)clamped;
                        InvalidateEconomy();
                    }
                    break;
                }
                case CommandType.MoveDivision:
                {
                    if (TryFindDivisionById(cmd.EntityId, out Entity divEntity))
                    {
                        ref DivisionComponent div = ref EcsWorld.Get<DivisionComponent>(divEntity);
                        if (div.CountryTag == cmd.CountryTag)
                        {
                            DivisionMovementSystem.IssueMoveOrder(this, ref div, cmd.TargetId);
                        }
                    }
                    break;
                }
                case CommandType.TogglePgm:
                {
                    if (TryFindDivisionById(cmd.EntityId, out Entity divEntity))
                    {
                        ref DivisionComponent div = ref EcsWorld.Get<DivisionComponent>(divEntity);
                        if (div.CountryTag == cmd.CountryTag)
                        {
                            div.PgmAuthorized = cmd.IntParam0 != 0 ? (byte)1 : (byte)0;
                        }
                    }
                    break;
                }
                case CommandType.QueueConstruction:
                {
                    if (cmd.TargetId < MaxStates)
                    {
                        ref StateData state = ref States[cmd.TargetId];
                        if (state.ControllerTag == cmd.CountryTag || state.ControllerTag == CountryTag.None)
                        {
                            BuildingType buildingType = (BuildingType)(cmd.IntParam0 & 0xFF);
                            byte levels = (byte)((cmd.IntParam0 >> 8) & 0xFF);
                            ushort requestedCiv = (ushort)System.Math.Clamp(cmd.IntParam1, 0, ConstructionSystem.MaxCivFactoriesPerState);
                            ConstructionSystem.QueueConstruction(ref state, buildingType, levels, requestedCiv);
                            InvalidateEconomy();
                        }
                    }
                    break;
                }
                case CommandType.AssignProductionFactories:
                {
                    if (TryFindProductionLineById(cmd.EntityId, out Entity lineEntity))
                    {
                        ref ProductionLineComponent line = ref EcsWorld.Get<ProductionLineComponent>(lineEntity);
                        if (line.CountryTag == cmd.CountryTag)
                        {
                            int clamped = System.Math.Clamp(cmd.IntParam0, 0, ProductionLineComponent.MaxFactoriesPerLine);
                            line.AssignedMilitaryFactories = (ushort)clamped;
                            InvalidateEconomy();
                        }
                    }
                    break;
                }
                case CommandType.DeclareWar:
                {
                    byte targetTag = (byte)cmd.TargetId;
                    if (targetTag < MaxCountries && targetTag != cmd.CountryTag)
                    {
                        DeclareWar(cmd.CountryTag, targetTag);
                    }
                    break;
                }
                case CommandType.RecruitDivision:
                {
                    ushort tplId = cmd.TargetId > 0 ? cmd.TargetId : DivisionTemplateRecord.DefaultInfantryTemplateId;
                    ushort spawnProv = cmd.IntParam0 is >= 0 and < MaxProvinces
                        ? (ushort)cmd.IntParam0
                        : GetCapitalProvince(cmd.CountryTag);
                    if (TryRecruitDivisionFromStockpile(cmd.CountryTag, tplId, spawnProv, out _))
                    {
                        InvalidateEconomy();
                    }
                    break;
                }
                case CommandType.SwitchProductionEquipment:
                {
                    if (TryFindProductionLineById(cmd.EntityId, out Entity lineEntity))
                    {
                        ref ProductionLineComponent line = ref EcsWorld.Get<ProductionLineComponent>(lineEntity);
                        if (line.CountryTag == cmd.CountryTag)
                        {
                            EquipmentType newType = (EquipmentType)cmd.IntParam0;
                            TechGeneration newGen = (TechGeneration)cmd.IntParam1;
                            ProductionSystem.SwitchLineEquipment(ref line, newType, newGen);
                            InvalidateEconomy();
                        }
                    }
                    break;
                }
                case CommandType.CreateProductionLine:
                {
                    EquipmentType type = (EquipmentType)cmd.IntParam0;
                    TechGeneration gen = (TechGeneration)(cmd.IntParam1 & 0xFF);
                    ushort assignedFactories = (ushort)((cmd.IntParam1 >> 8) & 0xFFFF);
                    int lineId = AllocateNextProductionLineId();
                    ProductionLineComponent line = ProductionLineComponent.Create(
                        lineId: lineId,
                        countryTag: cmd.CountryTag,
                        archetype: type,
                        generation: gen,
                        assignedMilitaryFactories: assignedFactories,
                        initialEfficiency: ProductionLineComponent.MinEfficiency);
                    SpawnProductionLine(line);
                    InvalidateEconomy();
                    break;
                }
                case CommandType.AssignNavalMission:
                {
                    if (TryFindNavalTaskForceById(cmd.EntityId, out Entity tfEntity))
                    {
                        ref NavalTaskForceComponent tf = ref EcsWorld.Get<NavalTaskForceComponent>(tfEntity);
                        if (tf.CountryTag == cmd.CountryTag)
                        {
                            if (cmd.TargetId < MaxStrategicRegions)
                            {
                                tf.AssignedRegionId = (byte)cmd.TargetId;
                            }
                            tf.Mission = (NavalMission)cmd.IntParam0;
                            InvalidateAirNaval();
                        }
                    }
                    break;
                }
                case CommandType.None:
                default:
                    break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetAiControlled(byte countryTag, bool enabled)
    {
        if (countryTag < CountryTag.ActiveCount)
        {
            if (enabled)
            {
                AiControlledCountryMask |= (ushort)(1 << countryTag);
            }
            else
            {
                AiControlledCountryMask &= (ushort)~(1 << countryTag);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAiControlled(byte countryTag)
    {
        return countryTag < CountryTag.ActiveCount && (AiControlledCountryMask & (1 << countryTag)) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnableAiForAllCountries(byte excludedPlayerTag = CountryTag.None)
    {
        ushort mask = (ushort)((1 << CountryTag.ActiveCount) - 1);
        if (excludedPlayerTag < CountryTag.ActiveCount)
        {
            mask &= (ushort)~(1 << excludedPlayerTag);
        }
        AiControlledCountryMask = mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteDailyTier()
    {
        DailyTicksExecuted++;

        // 1. SupplyGraphSystem (Phase 4.1):
        SupplyGraphSystem.Execute(this);

        // 2. SuperpowerEscalationSystem (Phase 4.3):
        SuperpowerEscalationSystem.Execute(this);

        // 3. ConstructionSystem & ProductionSystem (Phase 3.1):
        ConstructionSystem.Execute(this);
        ProductionSystem.Execute(this);

        // 3. ReinforcementSystem (Phase 3.2 - field division equipment/manpower reinforcement & daily Org recovery):
        ReinforcementSystem.Execute(this);

        // 4. FocusTreeSystem (Phase 5.1 - daily Political Power, Focus countdown & FocusEffectExecutor):
        FocusTreeSystem.Execute(this);

        // 5. CapitulationSystem (Phase 10.1 - Victory Points, stability surrender gate & territorial transfer):
        CapitulationSystem.Execute(this);

        // 6. OperationalPlanSystem (Phase 8.2 - Schwerpunkt spearheads & operational objectives):
        OperationalPlanSystem.Execute(this);

        // 6. TacticalFrontlineAISystem (Phase 6.1 / Phase 8.3 - multi-province pincer attacks & entrenchment):
        TacticalFrontlineAISystem.Execute(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteMonthlyTier()
    {
        MonthlyTicksExecuted++;

        // 1. MacroEconomySystem (Phase 3 / Phase 6 - monthly Cold War economic modifiers):
        MacroEconomySystem.Execute(this);

        // 2. StrategicMinisterAISystem (Phase 6.1 - monthly focus, construction & production utility scoring):
        StrategicMinisterAISystem.Execute(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceClockSixHours()
    {
        int nextHour = CurrentHour + 6;
        if (nextHour < 24)
        {
            CurrentHour = nextHour;
            return;
        }

        CurrentHour = 0;
        int daysInCurrentMonth = GetDaysInMonth(CurrentYear, CurrentMonth);
        int nextDay = CurrentDay + 1;
        if (nextDay <= daysInCurrentMonth)
        {
            CurrentDay = nextDay;
            return;
        }

        CurrentDay = 1;
        int nextMonth = CurrentMonth + 1;
        if (nextMonth <= 12)
        {
            CurrentMonth = nextMonth;
            return;
        }

        CurrentMonth = 1;
        CurrentYear++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDaysInMonth(int year, int month)
    {
        if (month == 2 && IsLeapYear(year))
        {
            return 29;
        }
        return DaysPerMonthStandard[month - 1];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLeapYear(int year)
    {
        return (year & 3) == 0 && (year % 100 != 0 || year % 400 == 0);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            World.Destroy(EcsWorld);
            _disposed = true;
        }
    }
}
