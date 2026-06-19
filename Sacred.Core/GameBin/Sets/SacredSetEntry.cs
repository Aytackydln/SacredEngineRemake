using System.Buffers.Binary;

namespace Sacred.Core.GameBin.Sets;

public readonly record struct SacredSetEntry(
    int SetIndex,
    long FileOffset,
    uint SetIdentifier,
    uint PackedSetIndexAndItemCount,
    IReadOnlyList<uint> ItemIds
)
{
    public const int Size = 112;
    public const int MaxItemCount = 10;
    public const uint EmptyRecordSentinel = 0xCCCCCCCC;

    public bool IsEmpty => SetIdentifier == EmptyRecordSentinel
                           && PackedSetIndexAndItemCount == EmptyRecordSentinel;

    public int DeclaredSetIndex => IsEmpty
        ? 0
        : checked((int)(PackedSetIndexAndItemCount >> 8));

    public int DeclaredItemCount => IsEmpty
        ? 0
        : checked((int)(PackedSetIndexAndItemCount & 0xFF));

    public bool HasConsistentPackedFields => IsEmpty
        || (DeclaredSetIndex == SetIndex && DeclaredItemCount == ItemIds.Count);

    public static SacredSetEntry FromBytes(int setIndex, long fileOffset, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size)
            throw new ArgumentException($"sets.bin records are {Size} bytes, got {bytes.Length}.", nameof(bytes));

        var itemIds = new uint[MaxItemCount];
        var itemCount = 0;
        for (var i = 0; i < MaxItemCount; i++)
        {
            var itemId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(i * sizeof(uint), sizeof(uint)));
            if (itemId is 0 or EmptyRecordSentinel)
                continue;

            itemIds[itemCount++] = itemId;
        }

        Array.Resize(ref itemIds, itemCount);

        return new SacredSetEntry(
            SetIndex: setIndex,
            FileOffset: fileOffset,
            SetIdentifier: BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(40, sizeof(uint))),
            PackedSetIndexAndItemCount: BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(44, sizeof(uint))),
            ItemIds: itemIds
        );
    }
}
