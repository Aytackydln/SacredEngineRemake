using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Embedded values, native +0x39C nine-byte list and copy-only creature trailer.</summary>
public sealed class GoldCreatureTail
{
    private readonly byte[] _payload;
    private readonly int _recordsOffset;
    private readonly int _trailerOffset;

    private GoldCreatureTail(byte[] payload, ushort opcode, (ushort Count, int Trailer, int ValuesSize) bounds)
    {
        _payload = payload;
        Values = GoldCreatureValuesState.FromPayload(payload.AsSpan(0, bounds.ValuesSize), opcode);
        Count = bounds.Count;
        _recordsOffset = Values.SerializedSize + GoldCreatureTailLayout.CountSize;
        _trailerOffset = bounds.Trailer;
    }

    public GoldCreatureValuesState Values { get; }
    public ushort Count { get; }
    public ReadOnlySpan<byte> Records => _payload.AsSpan(_recordsOffset, Count * GoldCreatureTailLayout.RecordSize);
    /// <summary>Native writer policy output retained without inferring character validity.</summary>
    public byte Mode => _payload[_trailerOffset + GoldCreatureTailLayout.ModeOffset];
    public uint Word570 => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(_trailerOffset + GoldCreatureTailLayout.Word570Offset));
    public uint MountPartnerReference => Word570;
    public ushort Word574 => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(_trailerOffset + GoldCreatureTailLayout.Word574Offset));
    public uint Word578 => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(_trailerOffset + GoldCreatureTailLayout.Word578Offset));
    public int SerializedSize => _trailerOffset + GoldCreatureTailLayout.TrailerSize;
    public ReadOnlySpan<byte> RemainingData => _payload.AsSpan(SerializedSize);

    public ReadOnlySpan<byte> GetRecord(int index)
    {
        if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        return Records.Slice(index * GoldCreatureTailLayout.RecordSize, GoldCreatureTailLayout.RecordSize);
    }

    public static GoldCreatureTail FromPayload(ReadOnlySpan<byte> payload, ushort opcode)
    {
        var bounds = Validate(payload, opcode);
        return new(payload.ToArray(), opcode, bounds);
    }

    public byte[] ToPayload() => _payload.ToArray();

    internal static (ushort Count, int Trailer, int ValuesSize) Validate(ReadOnlySpan<byte> payload, ushort opcode)
    {
        var values = GoldCreatureValuesBounds.Read(payload, GoldCreatureValuesLayout.IsFull(opcode));
        if (payload.Length < values.End + GoldCreatureTailLayout.CountSize)
            throw new InvalidDataException("Truncated Gold creature tail count.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(payload[values.End..]);
        var trailer = values.End + GoldCreatureTailLayout.CountSize + count * GoldCreatureTailLayout.RecordSize;
        if (payload.Length < trailer + GoldCreatureTailLayout.TrailerSize)
            throw new InvalidDataException("Truncated Gold creature tail records or trailer.");
        return (count, trailer, values.End);
    }
}
