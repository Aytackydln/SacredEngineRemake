using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Sets;

/// <summary>Header preceding fixed-size item-set records in <c>sets.bin</c>.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredSetHeaderLayout
{
    /// <summary>Serialized header size.</summary>
    public const int SerializedSize = sizeof(uint);

    /// <summary>Number of set records following the header.</summary>
    [FieldOffset(0x00)] public readonly uint SetCount;
}

/// <summary>Fixed-size binary record for one item set in <c>sets.bin</c>.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredSetEntryLayout
{
    /// <summary>Serialized size of one set record.</summary>
    public const int SerializedSize = 112;

    /// <summary>First item identifier in the set's ten-slot item table.</summary>
    [FieldOffset(0x00)] public readonly uint ItemId0;
    /// <summary>Second item identifier in the set's ten-slot item table.</summary>
    [FieldOffset(0x04)] public readonly uint ItemId1;
    /// <summary>Third item identifier in the set's ten-slot item table.</summary>
    [FieldOffset(0x08)] public readonly uint ItemId2;
    /// <summary>Fourth item identifier in the set's ten-slot item table.</summary>
    [FieldOffset(0x0C)] public readonly uint ItemId3;
    /// <summary>Fifth item identifier in the set's ten-slot item table.</summary>
    [FieldOffset(0x10)] public readonly uint ItemId4;
    /// <summary>Sixth item identifier in the set's ten-slot item table.</summary>
    [FieldOffset(0x14)] public readonly uint ItemId5;
    /// <summary>Seventh item identifier in the set's ten-slot item table.</summary>
    [FieldOffset(0x18)] public readonly uint ItemId6;
    /// <summary>Eighth item identifier in the set's ten-slot item table.</summary>
    [FieldOffset(0x1C)] public readonly uint ItemId7;
    /// <summary>Ninth item identifier in the set's ten-slot item table.</summary>
    [FieldOffset(0x20)] public readonly uint ItemId8;
    /// <summary>Tenth item identifier in the set's ten-slot item table.</summary>
    [FieldOffset(0x24)] public readonly uint ItemId9;

    /// <summary>Identifier of the item set.</summary>
    [FieldOffset(0x28)] public readonly uint SetIdentifier;

    /// <summary>Set-table index in the high 24 bits and item count in the low byte.</summary>
    [FieldOffset(0x2C)] public readonly uint PackedSetIndexAndItemCount;
}

/// <summary>Decoded item-set entry with its source position and populated item identifiers.</summary>
public readonly record struct SacredSetEntry(
    int SetIndex,
    long FileOffset,
    uint SetIdentifier,
    uint PackedSetIndexAndItemCount,
    IReadOnlyList<uint> ItemIds
)
{
    public const int Size = SacredSetEntryLayout.SerializedSize;
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
