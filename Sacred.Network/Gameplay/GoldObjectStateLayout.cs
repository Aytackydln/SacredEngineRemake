namespace Sacred.Network.Gameplay;

/// <summary>Gold cObject stream block written at 0x449D20, independent of packet prefixes.</summary>
public static class GoldObjectStateLayout
{
    public const int Size = 41;
    public const uint Marker = 0xFEEDF00D;
    public const int MarkerOffset = 0;
    public const int ReferenceOffset = 4;
    public const int TypeOffset = 8;
    /// <summary>Native +0x14, including inactive/removal bit 0x10 and container opened/broken bit 0x4000.</summary>
    public const int FlagsOffset = 12;
    public const int SectorOffset = 16;
    public const int XOffset = 18;
    public const int YOffset = 22;
    public const int SurfaceOffset = 26;
    public const int Word28Offset = 27;
    public const int FacingDegreesOffset = Word28Offset;
    public const int Word34Offset = 31;
    public const int TriggerReferenceOffset = Word34Offset;
    public const int Byte38Offset = 35;
    public const int OwnerOffset = 36;
    public const int Word3COffset = 37;
}
