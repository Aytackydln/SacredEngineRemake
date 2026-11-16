namespace Sacred.Core.Runtime;

/// <summary>Original putc/_flsbuf/fflush with an established caller-owned FILE
/// buffer. The character that triggers a flush is retained even on failure.</summary>
public sealed class SacredCrtBufferedOutput(SacredCrtOutputState state, SacredCrtWriteServices services, int bufferSize)
{
    private readonly byte[] _buffer = bufferSize > 0 ? new byte[bufferSize] : throw new ArgumentOutOfRangeException(nameof(bufferSize));
    private int _position;
    private int _remaining = bufferSize;
    public uint FileFlags { get; private set; } = 0x102;
    public static SacredCrtBufferedOutput Create(SacredCrtOutputState state, SacredCrtWriteServices services, int bufferSize, uint flags)
        => new(state, services, bufferSize) { FileFlags = flags, _remaining = (flags & 0x100) != 0 ? bufferSize : 0 };

    public int Write(ReadOnlyMemory<byte> bytes)
    {
        foreach (var value in bytes.Span)
        {
            if (--_remaining >= 0) { _buffer[_position++] = value; continue; }
            var count = _position; _position = 1; _remaining = _buffer.Length - 1;
            if (count == 0 && (state.Flags & 0x20) != 0 && services.Seek(0, 2)) state.Flags &= 0xfd;
            var written = count > 0 ? SacredCrtOutput.Write(state, services, _buffer.AsMemory(0, count)) : 0;
            _buffer[0] = value;
            if (written != count) { FileFlags |= 0x20; return -1; }
        }
        return bytes.Length;
    }
    public int Flush()
    {
        var result = 0;
        if (_position > 0 && SacredCrtOutput.Write(state, services, _buffer.AsMemory(0, _position)) != _position)
        {
            FileFlags |= 0x20; result = -1;
        }
        _position = 0; _remaining = 0;
        return result;
    }
}
