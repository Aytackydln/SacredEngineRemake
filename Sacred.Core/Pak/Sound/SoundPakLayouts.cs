using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Sound;

/// <summary>Storage type recorded in a Sound.pak entry descriptor.</summary>
public enum SacredSoundStorageFormat : uint
{
    /// <summary>A complete RIFF/WAVE file, commonly PCM or Microsoft IMA ADPCM.</summary>
    Wave = 32,

    /// <summary>Raw MPEG Layer III audio frames without an ID3 container.</summary>
    Mp3 = 33
}

/// <summary>Header preceding the sparse descriptor table in Sound.pak.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SoundPakHeaderLayout
{
    public const int SerializedSize = 0x100;

    /// <summary>ASCII file signature; expected to contain <c>SND</c>.</summary>
    [FieldOffset(0x00)]
    public readonly Signature3 Signature;

    /// <summary>Sound archive format version.</summary>
    [FieldOffset(0x03)]
    public readonly byte Version;

    /// <summary>
    /// Number of descriptor slots. Empty sound identifiers have an all-zero descriptor,
    /// so this is a slot count rather than the number of stored payloads.
    /// </summary>
    [FieldOffset(0x04)]
    public readonly uint EntryCount;

    public bool SignatureValid => Signature.Compare('S', 'N', 'D');

    public void ValidateSignature()
    {
        if (SignatureValid)
            return;

        throw new InvalidDataException(
            $"Invalid file format. Expected header 'SND', but got '{Signature.Text}'.");
    }
}
