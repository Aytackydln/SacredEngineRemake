using Sacred.Core.Pak.Weapon;

namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Gold file 0x5C7690; current item data plus a byte-counted vector of 130-byte item records.</summary>
public static class SacredSavedItemLayout
{
    public const int MarkerOffset = 247;
    public const int CountOffset = 251;
    public const int RecordsOffset = 252;
    public const int ItemDataSize = SacredEquipmentInstanceLayout.Size;
    public const int TrailerSize = 12;
    // 0x5C7690 writes zero, zero, BAADC0DE; its reader consumes all three without assigning item state.
    public const int ReservedTrailerWord0Offset = 0;
    public const int ReservedTrailerWord1Offset = 4;
    public const int TrailerMarkerOffset = 8;
    public const int MinimumSize = 394;
    public const int WeaponExtensionSize = 20;
    // 0x5CF1A0 writes BAADF00D then these four runtime words, in this order.
    public const int WeaponWord19COffset = 4;
    public const int WeaponWord1ACOffset = 8;
    public const int WeaponWord1A0Offset = 12;
    public const int WeaponWord1BCOffset = 16;
}
