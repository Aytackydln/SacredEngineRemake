namespace Sacred.Network.Gameplay;

/// <summary>Gold TRIGGER_INSERT_MAPICON / TRIGGER_INSERT_ATTRACTOR. Both append 24 opaque-preserving bytes.</summary>
public static class GoldScriptSpatialLayout
{
    public const ushort MapIconOpcode = 0x1CB;
    public const ushort AttractorOpcode = 0x1CC;
    public const int Size = 24;
    public const int MapIconBankSection = 2;
    public const int SectorOffset = 0;
    public const int XOffset = 4;
    public const int YOffset = 8;
    public const int SurfaceOffset = 12;
    /// <summary>Icon kind for resource zero; signed label curvature otherwise.</summary>
    public const int KindOrCurvatureOffset = 16;
    public const int LabelAngleOffset = 17;
    public const int TextResourceOffset = 20;
}
