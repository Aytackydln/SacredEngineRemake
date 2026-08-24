using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Items;

/// <summary>Descriptor that points from the Items.pak index to a model-description record.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Length)]
public readonly struct ItemsPakEntryInfoLayout
{
    public const int Length = 12;

    /// <summary>Absolute file offset of the corresponding model-description record.</summary>
    [FieldOffset(2)]
    public readonly uint ModelDescOffset;

}

public readonly record struct ItemsPakEntryInfo(
    ushort ItemIndex,
    uint ModelDescOffset
)
{
    public static ItemsPakEntryInfo FromBytes(ushort entryIndex, BinaryReader br)
    {
        Span<byte> bytes = stackalloc byte[ItemsPakEntryInfoLayout.Length];
        br.BaseStream.ReadExactly(bytes);

        var layout = MemoryMarshal.Cast<byte, ItemsPakEntryInfoLayout>(bytes)[0];

        return new ItemsPakEntryInfo(
            ItemIndex: entryIndex,
            ModelDescOffset: layout.ModelDescOffset
        );
    }
}
