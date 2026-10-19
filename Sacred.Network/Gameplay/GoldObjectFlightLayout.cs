namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x7E5950: a world position, six SetupFly operands and initial model height.</summary>
public static class GoldObjectFlightLayout
{
    public const ushort DropOpcode = 0x140;
    public const ushort ForcedOpcode = 0x147;
    public const int Size = 48;
    public const int ReferenceOffset = 0;
    public const int XOffset = 4;
    public const int YOffset = 8;
    public const int SurfaceOffset = 12;
    public const int SectorOffset = 16;
    /// <summary>Native +0xB4: velocity for free flight, or target XY for an item arc.</summary>
    public const int TranslationOffset = 20;
    /// <summary>Native +0xC0: angular rates for free flight. X == -1000 selects the
    /// item arc; Y then supplies peak height and Z supplies duration in seconds.</summary>
    public const int RotationOffset = 32;
    public const int HeightOffset = 44;
    public const float ItemArcMode = -1000;
    public const uint FlyingFlag = 0x100000;
}
