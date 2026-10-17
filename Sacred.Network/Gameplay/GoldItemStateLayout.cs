namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x5C7A50 packet item stream, including the 121-byte physical prefix.</summary>
public static class GoldItemStateLayout
{
    public const int MarkerOffset = GoldPhysicalStateLayout.Size;
    public const uint Marker = 0xBAADC0DE;
    public const int CountOffset = 125;
    public const int RecordsOffset = 127;
    public const int ItemDataSize = 130;
    public const int TrailerSize = 8;
    public const int MinimumSize = 265;
    public const int Word1E0Offset = 0;
    public const int RepeatedMarkerOffset = 4;
    public const uint WeaponMarker = 0xBAADF00D;
    public const int WeaponExtensionSize = 20;
    public const int WeaponWord19COffset = 4;
    public const int WeaponWord1ACOffset = 8;
    public const int WeaponWord1A0Offset = 12;
    public const int WeaponWord1BCOffset = 16;
}
