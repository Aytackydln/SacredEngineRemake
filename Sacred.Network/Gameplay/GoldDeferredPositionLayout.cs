namespace Sacred.Network.Gameplay;

/// <summary>Native creature +0x594 pending position and +0x5A8 presence byte.</summary>
public static class GoldDeferredPositionLayout
{
    public const int Size = 21;
    public const int SectorOffset = 0;
    public const int XOffset = 4;
    public const int YOffset = 8;
    public const int SurfaceOffset = 12;
    public const int FacingOffset = 16;
    public const int PresentOffset = 20;
}
