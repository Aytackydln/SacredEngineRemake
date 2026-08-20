using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Items;

/// <summary>Header preceding the Items.pak entry-descriptor table.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct ItemsPakHeaderLayout
{
    /// <summary>Serialized header size before entry descriptors.</summary>
    public const int SerializedSize = 0x102;

    /// <summary>ASCII file signature; expected to contain <c>ITM</c>.</summary>
    [FieldOffset(0x00)]
    [BinaryString("Signature", 3, "ASCII", NullTerminated = false)]
    private readonly byte _signature;

    /// <summary>Items archive format version.</summary>
    [FieldOffset(0x03)]
    public readonly byte Version;

    /// <summary>Number of 12-byte item entry descriptors.</summary>
    [FieldOffset(0x04)]
    public readonly uint EntryCount;
}
