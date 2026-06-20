using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Items;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Length)]
public readonly struct ItemsPakEntryInfoLayout
{
    public const int Length = 12;

    [FieldOffset(0)]
    private readonly ushort _unknown0;

    [FieldOffset(2)]
    public readonly uint ModelDescOffset;

    [FieldOffset(6)]
    private readonly byte _unknown6;

    [FieldOffset(7)]
    private readonly byte _unknown7;

    [FieldOffset(8)]
    private readonly byte _unknown8;

    [FieldOffset(9)]
    private readonly byte _unknown9;

    [FieldOffset(10)]
    public readonly byte Byte23;

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

        var layout = MemoryMarshal.Read<ItemsPakEntryInfoLayout>(bytes);

        return new ItemsPakEntryInfo(
            ItemIndex: entryIndex,
            ModelDescOffset: layout.ModelDescOffset,
            Layout: layout
        );
    }
}
