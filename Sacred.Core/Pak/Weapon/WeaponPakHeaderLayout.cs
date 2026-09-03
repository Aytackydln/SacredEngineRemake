using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Header preceding the fixed-size equipment records in Weapon.pak.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct WeaponPakHeaderLayout
{
    /// <summary>Serialized header size before equipment records.</summary>
    public const int SerializedSize = 0x102;

    /// <summary>ASCII file signature; expected to contain <c>WPN</c>.</summary>
    [FieldOffset(0x00)]
    public readonly Signature3 Signature;

    /// <summary>Number of 258-byte equipment records.</summary>
    [FieldOffset(0x03)]
    public readonly ushort EntryCount;

    public bool SignatureValid => Signature == Signature3.Weapon;

    public void ValidateSignature()
    {
        if (SignatureValid)
            return;

        throw new InvalidDataException(
            $"Invalid file format. Expected header 'WPN', but got '{Signature.Text}'.");
    }
}
