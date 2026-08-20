using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Items;

/// <summary>Descriptor that points from the Items.pak index to a model-description record.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Length)]
public readonly struct ItemsPakEntryInfoLayout
{
    public const int Length = 12;

    /// <summary>Unresolved two-byte value at descriptor offset 0x00.</summary>
    [FieldOffset(0)]
    private readonly ushort _unknown0;

    /// <summary>Absolute file offset of the corresponding model-description record.</summary>
    [FieldOffset(2)]
    public readonly uint ModelDescOffset;

    /// <summary>Unresolved byte at descriptor offset 0x06.</summary>
    [FieldOffset(6)]
    private readonly byte _unknown6;

    /// <summary>Unresolved byte at descriptor offset 0x07.</summary>
    [FieldOffset(7)]
    private readonly byte _unknown7;

    /// <summary>Unresolved byte at descriptor offset 0x08.</summary>
    [FieldOffset(8)]
    private readonly byte _unknown8;

    /// <summary>Unresolved byte at descriptor offset 0x09.</summary>
    [FieldOffset(9)]
    private readonly byte _unknown9;

    /// <summary>Unresolved flag byte at descriptor offset 0x0A.</summary>
    [FieldOffset(10)]
    [BinaryUnknown]
    public readonly byte Byte23;

    /// <summary>Unresolved byte at descriptor offset 0x0B.</summary>
    [FieldOffset(11)]
    private readonly byte _unknown11;
}

public readonly record struct ItemsPakEntryInfo(
    ushort ItemIndex,
    uint ModelDescOffset,
    ItemsPakEntryInfoLayout Layout
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
            Layout: layout
        );
    }
}
