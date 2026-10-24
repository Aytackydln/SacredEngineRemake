namespace Sacred.Network.Gameplay;

/// <summary>188-byte native record. New slots have unknown alignment bytes until a bank supplies them.</summary>
public sealed class GoldIndexedRecordState
{
    private readonly byte[] _bytes;
    private readonly bool[] _known;
    private GoldIndexedRecordState(byte[] bytes, bool[] known) => (_bytes, _known) = (bytes, known);
    public bool IsComplete => _known.All(value => value);
    public bool TryGetByte(int offset, out byte value)
    {
        if ((uint)offset >= GoldIndexedRecordCodec.RecordSize) throw new ArgumentOutOfRangeException(nameof(offset));
        value = _bytes[offset]; return _known[offset];
    }
    public byte[]? ToPayload() => IsComplete ? _bytes.ToArray() : null;
    internal static GoldIndexedRecordState FromBank(ReadOnlySpan<byte> bytes) => new(bytes.ToArray(),
        Enumerable.Repeat(true, GoldIndexedRecordCodec.RecordSize).ToArray());
    internal static GoldIndexedRecordState Patch(GoldIndexedRecordState? previous, ReadOnlySpan<byte> source)
    {
        var bytes = previous?._bytes.ToArray() ?? new byte[GoldIndexedRecordCodec.RecordSize];
        var known = previous?._known.ToArray() ?? new bool[GoldIndexedRecordCodec.RecordSize];
        // Gold 0x7F5720: scalar/array copies skip the alignment holes, including those in six 16-byte entries.
        for (var i = 0; i < bytes.Length; i++)
        {
            var copied = i < 65 || i is >= 68 and < 92 || i >= 92 && (i - 92) % 16 is < 2 or >= 4 and <= 12;
            if (copied) { bytes[i] = source[i]; known[i] = true; }
        }
        return new(bytes, known);
    }
}
