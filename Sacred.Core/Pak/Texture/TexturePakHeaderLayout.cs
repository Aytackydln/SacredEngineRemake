using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Texture;

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