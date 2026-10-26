using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EastAsiaGSG.Core.Commands;

/// <summary>
/// Deterministic command types processed at the start of every 6-hour tick.
/// </summary>
public enum CommandType : byte
{
    None = 0,
    MoveDivision = 1,
    TogglePgm = 2,
    AssignFactory = 3,
    SelectFocus = 4,
    ShiftDefcon = 5
}

/// <summary>
/// Unmanaged 16-byte discriminated union representing a player or AI command.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct SimulationCommand
{
    [FieldOffset(0)] public CommandType Type;
    [FieldOffset(1)] public byte CountryTag;
    [FieldOffset(2)] public ushort TargetId;
    [FieldOffset(4)] public int EntityId;
    [FieldOffset(8)] public int IntParam0;
    [FieldOffset(12)] public int IntParam1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SimulationCommand CreateMoveDivision(byte countryTag, int divisionEntityId, ushort targetProvinceId) => new()
    {
        Type = CommandType.MoveDivision,
        CountryTag = countryTag,
        TargetId = targetProvinceId,
        EntityId = divisionEntityId
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SimulationCommand CreateTogglePgm(byte countryTag, int divisionEntityId, bool authorized) => new()
    {
        Type = CommandType.TogglePgm,
        CountryTag = countryTag,
        EntityId = divisionEntityId,
        IntParam0 = authorized ? 1 : 0
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SimulationCommand CreateAssignFactory(byte countryTag, ushort stateOrLineId, int factoryCount) => new()
    {
        Type = CommandType.AssignFactory,
        CountryTag = countryTag,
        TargetId = stateOrLineId,
        IntParam0 = factoryCount
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SimulationCommand CreateSelectFocus(byte countryTag, short focusId, short costDays) => new()
    {
        Type = CommandType.SelectFocus,
        CountryTag = countryTag,
        TargetId = (ushort)focusId,
        IntParam0 = costDays
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SimulationCommand CreateShiftDefcon(byte countryTag, byte targetDefcon) => new()
    {
        Type = CommandType.ShiftDefcon,
        CountryTag = countryTag,
        TargetId = targetDefcon
    };
}
