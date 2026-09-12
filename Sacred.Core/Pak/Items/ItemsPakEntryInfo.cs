using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Items;

/// <summary>Descriptor that points from the Items.pak index to a model-description record.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Length)]
public readonly struct ItemsPakEntryInfoLayout
{
    public const int Length = 12;

    /// <summary>Unresolved UInt16 preceding the descriptor offset. It is zero in Gold.</summary>
    [FieldOffset(0), BinaryUnknown]
    public readonly ushort Reserved0;

    /// <summary>Absolute file offset of the corresponding model-description record.</summary>
    [FieldOffset(2)]
    public readonly uint ModelDescOffset;

    /// <summary>Serialized descriptor length. Gold uses 0x80 for every entry.</summary>
    [FieldOffset(6)]
    public readonly uint ModelDescSize;

    /// <summary>Unresolved descriptor tag. Gold uses several values.</summary>
    [FieldOffset(10), BinaryUnknown]
    public readonly ushort Tag;
}

public readonly record struct ItemsPakEntryInfo(
    ushort ItemIndex,
    uint ModelDescOffset,
    uint ModelDescSize,
    ushort Tag
)
{
    public static ItemsPakEntryInfo FromBytes(ushort entryIndex, BinaryReader br)
    {
        Span<byte> bytes = stackalloc byte[ItemsPakEntryInfoLayout.Length];
        br.BaseStream.ReadExactly(bytes);

        var layout = MemoryMarshal.Cast<byte, ItemsPakEntryInfoLayout>(bytes)[0];

        return new ItemsPakEntryInfo(
            ItemIndex: entryIndex,
            ModelDescOffset: layout.ModelDescOffset,
            ModelDescSize: layout.ModelDescSize,
            Tag: layout.Tag
        );
    }
}
