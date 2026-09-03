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

    /// <summary>Compressed pixel payload size, repeated from the entry descriptor.</summary>
    [FieldOffset(0x25)]
    public readonly uint CompressedSize;
}

/// <summary>Header preceding the descriptor table in Texture.pak.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct TexturePakHeaderLayout
{
    /// <summary>Serialized header size before entry descriptors.</summary>
    public const int SerializedSize = 0x100;

    /// <summary>ASCII file signature; expected to contain <c>TEX</c>.</summary>
    [FieldOffset(0x00)] public readonly Signature3 Signature;

    /// <summary>Texture archive format version.</summary>
    [FieldOffset(0x03)] public readonly byte Version;

    /// <summary>Number of 12-byte texture entry descriptors.</summary>
    [FieldOffset(0x04)] public readonly uint EntryCount;

    public bool SignatureValid => Signature == Signature3.Texture;

    public void ValidateSignature()
    {
        if (SignatureValid)
            return;

        throw new InvalidDataException(
            $"Invalid file format. Expected header 'TEX', but got '{Signature.Text}'.");
    }
}
