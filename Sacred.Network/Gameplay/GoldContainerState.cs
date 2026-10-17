using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Gold container 0x5CA690 appends a 32-bit count and 32-bit words to the common item body.
/// Caller must select this layout using the original resource family; it has no identifying marker.</summary>
public sealed class GoldContainerState
{
    private readonly byte[] _payload;
    private readonly int _countOffset;
    private GoldContainerState(byte[] payload, GoldItemState item, int countOffset, uint count)
        => (_payload, Item, _countOffset, Count) = (payload, item, countOffset, count);
    public GoldItemState Item { get; }
    public uint Count { get; }
    public int SerializedSize => _countOffset + 4 + (int)Count * 4;
    public ReadOnlySpan<byte> RemainingData => _payload.AsSpan(SerializedSize);

    /// <summary>Retain vector words without asserting whether they are resource or instance references.</summary>
    public uint GetWord(int index)
    {
        if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        return BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(_countOffset + 4 + index * 4));
    }

    public static GoldContainerState FromPayload(ReadOnlySpan<byte> payload)
    {
        var item = GoldItemState.FromPayload(payload);
        if (item.HasWeaponExtension) throw new InvalidDataException("Container cannot use the weapon extension layout.");
        var offset = item.SerializedSize;
        if (payload.Length < offset + 4) throw new InvalidDataException("Truncated Gold container count.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);
        if ((ulong)offset + 4 + (ulong)count * 4 > (ulong)payload.Length)
            throw new InvalidDataException("Truncated Gold container vector.");
        return new(payload.ToArray(), item, offset, count);
    }

    public byte[] ToPayload() => _payload.ToArray();

    public static GoldContainerState Create(GoldItemState item, ReadOnlySpan<uint> words)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.HasWeaponExtension || !item.RemainingData.IsEmpty)
            throw new ArgumentException("Container requires a complete common item body without extensions.", nameof(item));
        var body = item.ToPayload();
        var payload = new byte[checked(body.Length + 4 + words.Length * 4)];
        body.CopyTo(payload, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(body.Length), (uint)words.Length);
        for (var index = 0; index < words.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(body.Length + 4 + index * 4), words[index]);
        return FromPayload(payload);
    }
}
