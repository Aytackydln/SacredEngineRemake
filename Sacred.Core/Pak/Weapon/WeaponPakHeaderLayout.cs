using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Header preceding the fixed-size equipment records in Weapon.pak.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct WeaponPakHeaderLayout
{
    /// <summary>Serialized header size before equipment records.</summary>
    public const int SerializedSize = 0x102;

    /// <summary>ASCII file signature; expected to contain <c>WPN</c>.</summary>
    [FieldOffset(0x00)]
    [BinaryString("Signature", 3, "ASCII", NullTerminated = false)]
    private readonly byte _signature;

    /// <summary>Number of 258-byte equipment records.</summary>
    [FieldOffset(0x03)]
    public readonly ushort EntryCount;
}
