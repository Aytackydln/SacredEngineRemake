namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x54C7F9/0x54CADE streams. Offsets are relative to the creature marker, after ref/type.</summary>
public static class GoldCreatureUpdateLayout
{
    public const ushort StateOpcode = 0x11A;
    public const ushort ValuesOpcode = 0x11B;
    public const int PrefixSize = 8;
    public const int MarkerSize = 4;
    public const int StateSize = 75;
    public const int Word1F0Offset = 4;
    public const int Word1F4Offset = 8;
    public const int Word1F8Offset = 12;
    public const int Byte1FCOffset = 16;
    public const int Byte1FDOffset = 17;
    public const int Word1FEOffset = 18;
    public const int Word200Offset = 20;
    public const int Word245Offset = 24;
    public const int Word249Offset = 28;
    public const int Word24DOffset = 32;
    public const int Word251Offset = 36;
    public const int Word255Offset = 40;
    public const int Word259Offset = 44;
    public const int Word25FOffset = 48;
    public const int Word261Offset = 50;
    public const int Byte2B6Offset = 52;
    public const int Word2B7Offset = 53;
    public const int Block2B9Offset = 55;
    public const int Word2C9Offset = 71;
    // Native reader retains these bits from the destination, not the incoming stream.
    public const uint Preserve1F4Mask = 0x44000000;
    public const uint Preserve200Mask = 0x00E2009C;
    public const ushort Preserve2B7Mask = 0x2BF7;
}
