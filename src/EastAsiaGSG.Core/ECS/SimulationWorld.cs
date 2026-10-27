using System.Runtime.CompilerServices;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.Math;

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

    public const int StartYear = 1985;
    public const int StartMonth = 1;
    public const int StartDay = 1;
    public const int StartHour = 0;

    private static ReadOnlySpan<byte> DaysPerMonthStandard => [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    // Flat Contiguous State Arrays (Index == ID)
    public readonly ProvinceData[] Provinces = new ProvinceData[MaxProvinces];
    public readonly StateData[] States = new StateData[MaxStates];
    public readonly CountryState[] Countries = new CountryState[MaxCountries];
    public readonly StrategicRegionData[] Regions = new StrategicRegionData[MaxStrategicRegions];

    // Global CSR Adjacency Graph for Provinces
    public readonly ProvinceGraph Graph = new();
    public ushort[] AdjacencyTargets => Graph.AdjacencyTargets;
    public byte[] AdjacencyEdgeFlags => Graph.AdjacencyEdgeFlags;

    // High-Performance Zero-Allocation A* Pathfinder
    public readonly ProvincePathfinder Pathfinder = new();

    // Allocation-Free Deterministic Command Queue
    public readonly CommandRingBuffer CommandQueue = new(4096);

    // Arch ECS World for Dynamic Entities (Divisions, ActiveCombats, ProductionLines)
    public readonly Arch.Core.World EcsWorld = Arch.Core.World.Create();

    // Timestamp & Stratified Tick Telemetry (Starts 1985-01-01 00:00)
    public int TotalSixHourTicks { get; private set; }
    public int DailyTicksExecuted { get; private set; }
    public int MonthlyTicksExecuted { get; private set; }
    public int CommandsProcessedCount { get; private set; }

    public int CurrentYear { get; private set; } = StartYear;
    public int CurrentMonth { get; private set; } = StartMonth;
    public int CurrentDay { get; private set; } = StartDay;
    public int CurrentHour { get; private set; } = StartHour;

    private bool _disposed;

    public SimulationWorld()
    {
        InitializeDefaultArrays();
    }

    private void InitializeDefaultArrays()
    {
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
            Countries[i].Defcon = 5;
            Countries[i].HomelandEscalation = i == CountryTag.USA ? (byte)15 : (byte)0;
            Countries[i].Stability = 0.70f;
            Countries[i].WarSupport = 0.50f;
            Countries[i].ConsumerGoodsRatio = 0.25f;
            Countries[i].ActiveFocusId = -1;
            Countries[i].FocusDaysRemaining = 0;
        }

        for (byte i = 0; i < MaxStrategicRegions; i++)
        {
            Regions[i].RegionId = i;
            Regions[i].WeatherEfficiencyModifier = 1.0f;
        }
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

        // 2. DivisionMovementSystem hook (Phase 2.2)
        // 3. CombatResolutionSystem hook (Phase 2.2)
        // 4. AirAndNavalRegionSystem hook (Phase 4.2)
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
                    ref CountryState country = ref Countries[cmd.CountryTag];
                    country.ActiveFocusId = (short)cmd.TargetId;
                    country.FocusDaysRemaining = (short)cmd.IntParam0;
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
                    }
                    break;
                }
                case CommandType.MoveDivision:
                case CommandType.TogglePgm:
                case CommandType.None:
                default:
                    break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteDailyTier()
    {
        DailyTicksExecuted++;

        // 1. SupplyGraphSystem hook (Phase 4.1)
        // 2. ProductionSystem hook (Phase 3.1)
        // 3. ReinforcementSystem hook (Phase 3.2)
        // 4. FocusAndResearchSystem baseline daily progress:
        Span<CountryState> countries = Countries.AsSpan();
        for (int i = 0; i < countries.Length; i++)
        {
            ref CountryState country = ref countries[i];
            country.PoliticalPower += 1.0f;

            if (country.ActiveFocusId >= 0 && country.FocusDaysRemaining > 0)
            {
                country.FocusDaysRemaining--;
                if (country.FocusDaysRemaining == 0)
                {
                    country.MarkFocusCompleted(country.ActiveFocusId);
                    country.ActiveFocusId = -1;
                }
            }
        }

        // 5. TacticalFrontlineAISystem hook (Phase 6.1)
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteMonthlyTier()
    {
        MonthlyTicksExecuted++;

        // 1. MacroEconomySystem hook (Phase 3 / Phase 4)
        // 2. StrategicMinisterAISystem hook (Phase 6.1)
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
            Arch.Core.World.Destroy(EcsWorld);
            _disposed = true;
        }
    }
}
