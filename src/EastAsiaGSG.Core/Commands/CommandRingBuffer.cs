using System.Runtime.CompilerServices;

namespace EastAsiaGSG.Core.Commands;

/// <summary>
/// Pre-allocated, zero-allocation FIFO ring buffer for deterministic SimulationCommand structs.
/// </summary>
public sealed class CommandRingBuffer
{
    private readonly SimulationCommand[] _buffer;
    private int _head;
    private int _tail;
    private int _count;

    public CommandRingBuffer(int capacity = 4096)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Ring buffer capacity must be positive.");
        }

        _buffer = new SimulationCommand[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count => _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnqueue(in SimulationCommand command)
    {
        if (_count == _buffer.Length)
        {
            return false;
        }

        _buffer[_tail] = command;
        _tail++;
        if (_tail == _buffer.Length)
        {
            _tail = 0;
        }

        _count++;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDequeue(out SimulationCommand command)
    {
        if (_count == 0)
        {
            command = default;
            return false;
        }

        command = _buffer[_head];
        _head++;
        if (_head == _buffer.Length)
        {
            _head = 0;
        }

        _count--;
        return true;
    }

    public void Clear()
    {
        _head = 0;
        _tail = 0;
        _count = 0;
    }
}
