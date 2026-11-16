namespace Sacred.GameplayHost.Runtime;

/// <summary>Gold fgets/refill using an established caller-owned FILE buffer.
/// FILE flags101/descriptor state are explicit; fopen/allocator/locks are separate.</summary>
public sealed class SacredCrtBufferedInput(SacredCrtInputState state, SacredCrtReadServices services, int bufferSize)
{
    private readonly byte[] _buffer = bufferSize > 0 ? new byte[bufferSize] : throw new ArgumentOutOfRangeException(nameof(bufferSize));
    private int _position;
    private int _remaining;
    public uint FileFlags { get; private set; } = 0x101;
    public static SacredCrtBufferedInput Create(SacredCrtInputState state, SacredCrtReadServices services, int bufferSize, uint flags)
        => new(state, services, bufferSize) { FileFlags = flags };

    public ReadOnlyMemory<byte>? ReadLine(int capacity)
    {
        if (capacity <= 0) return null;
        var bytes = new List<byte>();
        while (--capacity != 0)
        {
            var value = ReadByte();
            if (value < 0)
            {
                if (bytes.Count == 0) return null;
                return bytes.ToArray();
            }
            bytes.Add((byte)value);
            if (value == 10) break;
        }
        return bytes.ToArray();
    }
    private int ReadByte()
    {
        if (--_remaining >= 0) return _buffer[_position++];
        _position = 0;
        var count = SacredCrtInput.Read(state, services, _buffer);
        if (count <= 0)
        {
            FileFlags |= count == 0 ? 0x10u : 0x20u; _remaining = 0; return -1;
        }
        if ((state.Flags & 0x82) == 0x82) FileFlags |= 0x2000;
        _remaining = count - 1; return _buffer[_position++];
    }
}
