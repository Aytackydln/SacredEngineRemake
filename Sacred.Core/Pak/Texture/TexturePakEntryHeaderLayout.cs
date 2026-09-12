using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Texture;

[InlineArray(39)]
public struct TexturePakEntryReservedBytes
{
    private byte _element0;
}

/// <summary>Metadata prefix stored before one Texture.pak image payload.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct TexturePakEntryHeaderLayout
{
    /// <summary>Serialized metadata size before encoded pixel bytes.</summary>
    public const int SerializedSize = 0x50;

    /// <summary>Null-terminated texture name encoded as ASCII.</summary>
    [FieldOffset(0x00)]
    [BinaryString("Name", 0x20, "ASCII")]
    private readonly byte _name;

    /// <summary>Image width in pixels.</summary>
    [FieldOffset(0x20)]
    public readonly ushort Width;

    /// <summary>Image height in pixels.</summary>
    [FieldOffset(0x22)]
    public readonly ushort Height;

    /// <summary>Encoded pixel storage format.</summary>
    [FieldOffset(0x24)]
    public readonly SacredTextureStorageFormat StorageFormat;

    /// <summary>
    /// Encoded pixel payload size, repeated from the entry descriptor. The Demo
    /// calls this <c>RLEsize</c>, although Gold primarily stores zlib payloads.
    /// </summary>
    [FieldOffset(0x25)]
    public readonly uint EncodedSize;

    /// <summary>
    /// Native <c>sTextureEntry::reserved[39]</c>. Every byte is zero in all
    /// 4,465 populated January-demo entries, all 25,534 Gold entries, and both
    /// entries in Gold's Texture03.pak. These bytes contain no serialized blend
    /// mode or colour parameters.
    /// </summary>
    [FieldOffset(0x29)]
    public readonly TexturePakEntryReservedBytes Reserved;
}
