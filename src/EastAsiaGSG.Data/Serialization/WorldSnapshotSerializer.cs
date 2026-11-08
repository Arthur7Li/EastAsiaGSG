using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Data.Serialization;

/// <summary>
/// High-performance binary snapshot serializer for EastAsiaGSG.
/// Uses zero-reflection unmanaged block memory transfers (MemoryMarshal.AsBytes) and ZLib compression
/// to serialize and deserialize complete simulation states in &lt; 5 ms.
/// </summary>
public static class WorldSnapshotSerializer
{
    /// <summary>
    /// Magic 8-byte ASCII header ("EAGSAV01" little-endian: 0x3130564153474145).
    /// </summary>
    public const ulong ExpectedMagic = 0x3130564153474145UL;

    /// <summary>
    /// File format version. Incremented on breaking schema revisions.
    /// </summary>
    public const uint ExpectedVersion = 1;

    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();
    private static readonly QueryDescription CombatQuery = new QueryDescription().WithAll<ActiveCombatComponent>();
    private static readonly QueryDescription ProductionLineQuery = new QueryDescription().WithAll<ProductionLineComponent>();
    private static readonly QueryDescription TaskForceQuery = new QueryDescription().WithAll<NavalTaskForceComponent>();
    private static readonly QueryDescription AirWingQuery = new QueryDescription().WithAll<AirWingComponent>();

    /// <summary>
    /// Serializes the entire SimulationWorld into a compressed binary stream.
    /// </summary>
    public static void SaveToStream(SimulationWorld world, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(stream);

        // 1. Write Uncompressed 12-byte Header
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(header[..8], ExpectedMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..12], ExpectedVersion);
        stream.Write(header);

        // 2. Write Compressed Payload
        using ZLibStream zlib = new(stream, CompressionLevel.Fastest, leaveOpen: true);
        using BinaryWriter writer = new(zlib, System.Text.Encoding.UTF8, leaveOpen: true);

        // A. Calendar & Telemetry Scalars
        world.GetIdCounters(out int nextCombatId, out int nextDivisionId, out int nextProductionLineId);
        writer.Write(world.TotalSixHourTicks);
        writer.Write(world.DailyTicksExecuted);
        writer.Write(world.MonthlyTicksExecuted);
        writer.Write(world.CommandsProcessedCount);
        writer.Write(world.TriggeredNewsEventCount);
        writer.Write(world.CurrentYear);
        writer.Write(world.CurrentMonth);
        writer.Write(world.CurrentDay);
        writer.Write(world.CurrentHour);
        writer.Write(world.OwnershipRevision);
        writer.Write(world.SupplyRevision);
        writer.Write(world.EconomyRevision);
        writer.Write(world.AirNavalRevision);
        writer.Write(world.CountryCapitulatedMask);
        writer.Write(world.AiControlledCountryMask);
        writer.Write(nextCombatId);
        writer.Write(nextDivisionId);
        writer.Write(nextProductionLineId);

        // B. Flat Contiguous State Arrays
        writer.Write(MemoryMarshal.AsBytes(world.Provinces.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.States.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.Countries.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.Regions.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.Templates.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.FocusTrees.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.CountryFocusCounts.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.CountryCapitals.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.CountryHomelandEscalation.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.CountryWarMasks.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.CountryWargoalMasks.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.CountryDoctrines.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(world.SeaZoneInterdictionPenalty.AsSpan()));
        writer.Write(world.CountryUnlockedTechGen.AsSpan());
        writer.Write(world.CountryTechBonuses.AsSpan());
        writer.Write(MemoryMarshal.AsBytes(world.TriggeredNewsEventHashes.AsSpan()));
        writer.Write(world.TriggeredNewsEventCountries.AsSpan());

        // C. CSR Adjacency Graph
        writer.Write(world.AdjacencyTargets.Length);
        writer.Write(MemoryMarshal.AsBytes(world.AdjacencyTargets.AsSpan()));
        writer.Write(world.AdjacencyEdgeFlags.Length);
        writer.Write(world.AdjacencyEdgeFlags.AsSpan());

        // D. Arch ECS Dynamic Entities
        // Divisions
        int divCount = 0;
        Query divQuery = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in divQuery)
        {
            divCount += chunk.Count;
        }
        writer.Write(divCount);
        foreach (ref Chunk chunk in divQuery)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            writer.Write(MemoryMarshal.AsBytes(divs[..chunk.Count]));
        }

        // Active Combats
        int combatCount = 0;
        Query combatQuery = world.EcsWorld.Query(in CombatQuery);
        foreach (ref Chunk chunk in combatQuery)
        {
            combatCount += chunk.Count;
        }
        writer.Write(combatCount);
        foreach (ref Chunk chunk in combatQuery)
        {
            Span<ActiveCombatComponent> combats = chunk.GetSpan<ActiveCombatComponent>();
            writer.Write(MemoryMarshal.AsBytes(combats[..chunk.Count]));
        }

        // Production Lines
        int lineCount = 0;
        Query lineQuery = world.EcsWorld.Query(in ProductionLineQuery);
        foreach (ref Chunk chunk in lineQuery)
        {
            lineCount += chunk.Count;
        }
        writer.Write(lineCount);
        foreach (ref Chunk chunk in lineQuery)
        {
            Span<ProductionLineComponent> lines = chunk.GetSpan<ProductionLineComponent>();
            writer.Write(MemoryMarshal.AsBytes(lines[..chunk.Count]));
        }

        // Naval Task Forces
        int tfCount = 0;
        Query tfQuery = world.EcsWorld.Query(in TaskForceQuery);
        foreach (ref Chunk chunk in tfQuery)
        {
            tfCount += chunk.Count;
        }
        writer.Write(tfCount);
        foreach (ref Chunk chunk in tfQuery)
        {
            Span<NavalTaskForceComponent> tfs = chunk.GetSpan<NavalTaskForceComponent>();
            writer.Write(MemoryMarshal.AsBytes(tfs[..chunk.Count]));
        }

        // Air Wings
        int wingCount = 0;
        Query wingQuery = world.EcsWorld.Query(in AirWingQuery);
        foreach (ref Chunk chunk in wingQuery)
        {
            wingCount += chunk.Count;
        }
        writer.Write(wingCount);
        foreach (ref Chunk chunk in wingQuery)
        {
            Span<AirWingComponent> wings = chunk.GetSpan<AirWingComponent>();
            writer.Write(MemoryMarshal.AsBytes(wings[..chunk.Count]));
        }
    }

    /// <summary>
    /// Deserializes a SimulationWorld from a compressed binary stream.
    /// </summary>
    public static void LoadFromStream(SimulationWorld world, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(stream);

        // 1. Validate Uncompressed 12-byte Header
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        ulong magic = BinaryPrimitives.ReadUInt64LittleEndian(header[..8]);
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header[8..12]);

        if (magic != ExpectedMagic)
        {
            throw new InvalidDataException($"Invalid savegame magic header: expected 0x{ExpectedMagic:X16}, got 0x{magic:X16}");
        }

        if (version != ExpectedVersion)
        {
            throw new InvalidDataException($"Unsupported savegame version: expected {ExpectedVersion}, got {version}");
        }

        // 2. Decompress Payload
        using ZLibStream zlib = new(stream, CompressionMode.Decompress, leaveOpen: true);
        using BinaryReader reader = new(zlib, System.Text.Encoding.UTF8, leaveOpen: true);

        // A. Calendar & Telemetry Scalars
        int totalTicks = reader.ReadInt32();
        int dailyTicks = reader.ReadInt32();
        int monthlyTicks = reader.ReadInt32();
        int commandsProcessed = reader.ReadInt32();
        int newsCount = reader.ReadInt32();
        int year = reader.ReadInt32();
        int month = reader.ReadInt32();
        int day = reader.ReadInt32();
        int hour = reader.ReadInt32();
        uint ownershipRev = reader.ReadUInt32();
        uint supplyRev = reader.ReadUInt32();
        uint economyRev = reader.ReadUInt32();
        uint airNavalRev = reader.ReadUInt32();
        ushort capitulatedMask = reader.ReadUInt16();
        ushort aiMask = reader.ReadUInt16();
        int nextCombatId = reader.ReadInt32();
        int nextDivisionId = reader.ReadInt32();
        int nextProductionLineId = reader.ReadInt32();

        world.CountryCapitulatedMask = capitulatedMask;
        world.AiControlledCountryMask = aiMask;
        world.SetIdCounters(nextCombatId, nextDivisionId, nextProductionLineId);
        world.RestoreTimeAndTelemetry(
            totalTicks,
            dailyTicks,
            monthlyTicks,
            commandsProcessed,
            newsCount,
            year,
            month,
            day,
            hour,
            ownershipRev,
            supplyRev,
            economyRev,
            airNavalRev);

        // B. Flat Contiguous State Arrays
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.Provinces.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.States.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.Countries.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.Regions.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.Templates.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.FocusTrees.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.CountryFocusCounts.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.CountryCapitals.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.CountryHomelandEscalation.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.CountryWarMasks.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.CountryWargoalMasks.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.CountryDoctrines.AsSpan()));
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.SeaZoneInterdictionPenalty.AsSpan()));
        zlib.ReadExactly(world.CountryUnlockedTechGen.AsSpan());
        zlib.ReadExactly(world.CountryTechBonuses.AsSpan());
        zlib.ReadExactly(MemoryMarshal.AsBytes(world.TriggeredNewsEventHashes.AsSpan()));
        zlib.ReadExactly(world.TriggeredNewsEventCountries.AsSpan());

        // C. CSR Adjacency Graph
        int targetCount = reader.ReadInt32();
        ushort[] targets = new ushort[targetCount];
        zlib.ReadExactly(MemoryMarshal.AsBytes(targets.AsSpan()));

        int flagCount = reader.ReadInt32();
        byte[] edgeFlags = new byte[flagCount];
        zlib.ReadExactly(edgeFlags.AsSpan());

        world.Graph.LoadCsrDirect(targets, edgeFlags);

        // D. Clear Existing Dynamic Entities & Restore from Snapshot
        world.ClearAllDynamicEntities();

        // Restore Divisions
        int divCount = reader.ReadInt32();
        Span<DivisionComponent> divBatch = stackalloc DivisionComponent[64];
        int remainingDivs = divCount;
        while (remainingDivs > 0)
        {
            int batchSize = Math.Min(remainingDivs, divBatch.Length);
            Span<DivisionComponent> slice = divBatch[..batchSize];
            zlib.ReadExactly(MemoryMarshal.AsBytes(slice));
            for (int i = 0; i < batchSize; i++)
            {
                world.SpawnDivision(in slice[i]);
            }
            remainingDivs -= batchSize;
        }

        // Restore Combats
        int combatCount = reader.ReadInt32();
        Span<ActiveCombatComponent> combatBatch = stackalloc ActiveCombatComponent[32];
        int remainingCombats = combatCount;
        while (remainingCombats > 0)
        {
            int batchSize = Math.Min(remainingCombats, combatBatch.Length);
            Span<ActiveCombatComponent> slice = combatBatch[..batchSize];
            zlib.ReadExactly(MemoryMarshal.AsBytes(slice));
            for (int i = 0; i < batchSize; i++)
            {
                world.EcsWorld.Create(slice[i]);
            }
            remainingCombats -= batchSize;
        }

        // Restore Production Lines
        int lineCount = reader.ReadInt32();
        Span<ProductionLineComponent> lineBatch = stackalloc ProductionLineComponent[32];
        int remainingLines = lineCount;
        while (remainingLines > 0)
        {
            int batchSize = Math.Min(remainingLines, lineBatch.Length);
            Span<ProductionLineComponent> slice = lineBatch[..batchSize];
            zlib.ReadExactly(MemoryMarshal.AsBytes(slice));
            for (int i = 0; i < batchSize; i++)
            {
                world.SpawnProductionLine(in slice[i]);
            }
            remainingLines -= batchSize;
        }

        // Restore Naval Task Forces
        int tfCount = reader.ReadInt32();
        Span<NavalTaskForceComponent> tfBatch = stackalloc NavalTaskForceComponent[32];
        int remainingTfs = tfCount;
        while (remainingTfs > 0)
        {
            int batchSize = Math.Min(remainingTfs, tfBatch.Length);
            Span<NavalTaskForceComponent> slice = tfBatch[..batchSize];
            zlib.ReadExactly(MemoryMarshal.AsBytes(slice));
            for (int i = 0; i < batchSize; i++)
            {
                world.SpawnNavalTaskForce(in slice[i]);
            }
            remainingTfs -= batchSize;
        }

        // Restore Air Wings
        int wingCount = reader.ReadInt32();
        Span<AirWingComponent> wingBatch = stackalloc AirWingComponent[32];
        int remainingWings = wingCount;
        while (remainingWings > 0)
        {
            int batchSize = Math.Min(remainingWings, wingBatch.Length);
            Span<AirWingComponent> slice = wingBatch[..batchSize];
            zlib.ReadExactly(MemoryMarshal.AsBytes(slice));
            for (int i = 0; i < batchSize; i++)
            {
                world.SpawnAirWing(in slice[i]);
            }
            remainingWings -= batchSize;
        }

        // E. Reconstruct Spatial Index & Trigger Cache Invalidation
        world.SpatialIndex.Rebuild(world);
        world.InvalidateOwnership();
        world.InvalidateSupply();
        world.InvalidateEconomy();
        world.InvalidateAirNaval();
    }

    /// <summary>
    /// Writes a binary savegame file (.eagsav) to disk.
    /// </summary>
    public static void SaveToFile(SimulationWorld world, string filePath)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        SaveToStream(world, fs);
    }

    /// <summary>
    /// Loads a binary savegame file (.eagsav) from disk.
    /// </summary>
    public static void LoadFromFile(SimulationWorld world, string filePath)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Save file not found at '{filePath}'", filePath);
        }

        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        LoadFromStream(world, fs);
    }
}
