namespace Sacred.Network.Gameplay;

/// <summary>Bank 3 section 4's 280-byte records, visited by Gold EnterSector
/// at 0x4946D0. Other fields remain opaque until their restore effects are implemented.</summary>
public static class GoldSectorObjectRestoreLayout
{
    public const int Size = 280;
    public const int ObjectTypeOffset = 0;
    public const int XOffset = 4;
    public const int YOffset = 8;
    public const int SurfaceOffset = 12;
    public const int SectorOffset = 16;
    public const int HeightOffset = 20;
}
