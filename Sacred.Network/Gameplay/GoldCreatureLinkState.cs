using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.Network.Gameplay;

/// <summary>Lossless ordered +39C roster. Unknown differs from a received empty vector.</summary>
public sealed class GoldCreatureLinkState
{
    private readonly byte[] _bytes;
    private GoldCreatureLinkState(byte[] bytes) => _bytes = bytes;
    public int Count => _bytes.Length / SacredCreatureLinkRecordLayout.SerializedSize;
    public ReadOnlySpan<byte> GetRecord(int index)
    {
        if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _bytes.AsSpan(index * SacredCreatureLinkRecordLayout.SerializedSize, SacredCreatureLinkRecordLayout.SerializedSize);
    }
    public byte[] ToPayload() => _bytes.ToArray();
    public static GoldCreatureLinkState FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length % SacredCreatureLinkRecordLayout.SerializedSize != 0) throw new InvalidDataException("Incomplete Gold creature link record.");
        return new(bytes.ToArray());
    }
    internal GoldCreatureLinkState Apply(uint reference, bool add, bool option)
    {
        var stride = SacredCreatureLinkRecordLayout.SerializedSize;
        var index = Enumerable.Range(0, Count).FirstOrDefault(i => BinaryPrimitives.ReadUInt32LittleEndian(GetRecord(i)) == reference, -1);
        if (add)
        {
            if (index >= 0) return this; // Native duplicate leaves the old record and option intact.
            var bytes = new byte[_bytes.Length + stride]; _bytes.CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(_bytes.Length), reference);
            bytes[^1] = option ? (byte)1 : (byte)0;
            return new(bytes);
        }
        if (index < 0) return this;
        var copy = _bytes.ToArray(); copy.AsSpan(copy.Length - stride).CopyTo(copy.AsSpan(index * stride));
        return new(copy[..^stride]);
    }
}
