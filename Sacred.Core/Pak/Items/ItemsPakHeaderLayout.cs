using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Items;

/// <summary>Header preceding the Items.pak entry-descriptor table.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly record struct ItemsPakHeaderLayout
{
    /// <summary>Serialized header size before entry descriptors.</summary>
    public const int SerializedSize = 0x102;

    /// <summary>ASCII file signature; expected to contain <c>ITM</c>.</summary>
    [FieldOffset(0x00)]
    public readonly Signature3 Signature;

    /// <summary>Items archive format version.</summary>
    [FieldOffset(0x03)]
    public readonly byte Version;

    /// <summary>Number of 12-byte item entry descriptors.</summary>
    [FieldOffset(0x04)]
    public readonly int EntryCount;

    public bool SignatureValid => Signature.Compare('I', 'T', 'M');

    public void ValidateSignature()
    {
        if (SignatureValid) return;
 
        const string firstBytes = "ITM";
        var msg = $"Invalid file format. Expected header '{firstBytes}', but got '{Signature.Text}'.";
        throw new InvalidDataException(msg);
    }
}
