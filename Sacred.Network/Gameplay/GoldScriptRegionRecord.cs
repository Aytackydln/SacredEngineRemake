using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Gold's 32-byte sRegion, used by SetRG2 and bank 1 section 5. Separate from the world region singleton.</summary>
public sealed class GoldScriptRegionRecord
{
    public const int Size = 32;
    private readonly byte[] _bytes;
    private GoldScriptRegionRecord(byte[] bytes) => _bytes = bytes;
    public int FirstX => unchecked((int)Word(0));
    public int FirstY => unchecked((int)Word(4));
    public int LastX => unchecked((int)Word(8));
    public int LastY => unchecked((int)Word(12));
    public uint Resource => Word(16);
    public uint Type => Word(20);
    public uint Sound => Word(24);
    public uint Timestamp => Word(28);
    public byte[] ToPayload() => _bytes.ToArray();
    public static GoldScriptRegionRecord FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size) throw new InvalidDataException("Gold script region requires exactly 32 bytes.");
        return new(bytes.ToArray());
    }
    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(offset));
}
