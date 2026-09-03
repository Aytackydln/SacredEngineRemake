using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Sound;

[InlineArray(SoundProfilePakEntryLayout.NameLength)]
public struct SoundProfileNameBytes
{
    private byte _element0;
}

[InlineArray(SoundProfilePakEntryLayout.SoundSlotCount)]
public struct SoundProfileSoundIds
{
    private ushort _element0;
}

[InlineArray(SoundProfilePakEntryLayout.ReservedLength)]
public struct SoundProfileReservedBytes
{
    private byte _element0;
}

/// <summary>Header preceding the sparse descriptor table in sndProfiles.pak.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SoundProfilePakHeaderLayout
{
    public const int SerializedSize = 0x100;

    /// <summary>ASCII file signature; expected to contain <c>SPF</c>.</summary>
    [FieldOffset(0x00)]
    public readonly Signature3 Signature;

    /// <summary>Sound-profile archive format version.</summary>
    [FieldOffset(0x03)]
    public readonly byte Version;

    /// <summary>Number of profile descriptor slots.</summary>
    [FieldOffset(0x04)]
    public readonly uint EntryCount;

    public bool SignatureValid => Signature == Signature3.SoundProfile;

    public void ValidateSignature()
    {
        if (SignatureValid)
            return;

        throw new InvalidDataException(
            $"Invalid file format. Expected header 'SPF', but got '{Signature.Text}'.");
    }
}

/// <summary>Fixed-size sound selection profile stored in sndProfiles.pak.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SoundProfilePakEntryLayout
{
    public const int SerializedSize = 0xB8;
    public const uint DescriptorType = 35;
    public const int NameLength = 0x20;
    public const int ReservedLength = 0x14;
    public const int SoundSlotCount = 64;

    /// <summary>Null-terminated profile name encoded as ISO-8859-1.</summary>
    [FieldOffset(0x00)]
    [BinaryString("Name", NameLength, "ISO-8859-1")]
    public readonly SoundProfileNameBytes NameBytes;

    /// <summary>One for a defined profile and zero for an unused profile slot.</summary>
    [FieldOffset(0x20)]
    public readonly uint IsDefined;

    /// <summary>Reserved profile bytes; zero in every defined Sacred Gold profile.</summary>
    [FieldOffset(0x24)]
    [BinaryUnknown]
    public readonly SoundProfileReservedBytes Reserved;

    /// <summary>
    /// Sound.pak identifiers selected for the profile's 64 authored event/variant slots.
    /// Repeated identifiers preserve the game's weighted/random selection table.
    /// </summary>
    [FieldOffset(0x38)]
    public readonly SoundProfileSoundIds SoundIds;
}
