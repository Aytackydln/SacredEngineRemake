namespace Sacred.Network.Gameplay;

/// <summary>Gold movement streams from 0x54C49B/0x54C5BC and the six-word event prefix.</summary>
public static class GoldMovementLayout
{
    public const ushort PathOpcode = 0x161;
    public const ushort EndOpcode = 0x162;
    public const ushort ActionOpcode = 0x163;
    public const int PrefixSize = 24;
    public const int Control174Offset = 4;
    public const int XOffset = 8;
    public const int YOffset = 12;
    public const int SurfaceOffset = 16;
    public const int SectorOffset = 20;
    public const uint Marker = 0xC0DEF00D;
    public const int Word1F8Offset = 4;
    public const int Flags200Offset = 8;
    public const int PathIndexOffset = 12; // Creature +0x36D.
    public const int PathCountOffset = 16; // Creature +0x371.
    public const int PathOffset = 20; // XY pairs from creature +0x2ED.
    public const int PathPointSize = 8;
    public const int ActionOffset = 12;
    public const int ActionSize = 173; // Creature +0x2ED..+0x399, unchanged.
    public const int ActionBodySize = ActionOffset + ActionSize + 1;
    public const int EndXOffset = 4;
    public const int EndYOffset = 8;
    public const int EndSurfaceOffset = 12;
    public const int EndSectorOffset = 16;
    public const int EndFacingOffset = 20; // Projected physical direction in float degrees (0x450630).
    public const int EndRetainedOffset = 24;
}
