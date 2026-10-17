using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Bounds every allocation and nested read by the actual native stream bytes.</summary>
internal ref struct GoldWorldBankReader(byte[] payload, int cursor)
{
    public int Cursor { get; private set; } = cursor;
    public void RequireRecords(uint count, int minimumSize)
    {
        if (count > (payload.Length - Cursor) / minimumSize)
            throw new InvalidDataException("Gold world bank has a truncated record array.");
    }

    public ReadOnlyMemory<byte> ReadBlock(int length)
    {
        if ((uint)length > payload.Length - Cursor) throw new InvalidDataException("Gold world bank has truncated data.");
        var block = payload.AsMemory(Cursor, length);
        Cursor += length;
        return block;
    }
    public uint ReadCount() => BinaryPrimitives.ReadUInt32LittleEndian(ReadBlock(sizeof(uint)).Span);
    public ReadOnlyMemory<byte> ReadVector(int elementSize)
    {
        var count = ReadCount();
        RequireRecords(count, elementSize);
        return ReadBlock((int)count * elementSize);
    }
    public ReadOnlyMemory<byte> ReadSince(int start) => payload.AsMemory(start, Cursor - start);
}
