using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Texture;

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
}
