namespace Sacred.Network.Gameplay;

/// <summary>Gold CREATURE_MAGIC (0x183), receiver 0x805FD0, event 0x207 at 0x5364F0.</summary>
public static class GoldCastLayout
{
    public const ushort Opcode = 0x183;
    public const int ReferenceOffset = 0;
    public const int ActionBytesOffset = 4;
    public const int ActionOffset = 8;
    public const int MaximumActionBytes = 64;
    public const int MaximumReferenceBytes = 80;
    // Relative to the spell bank at creature +0x154 (special moves start at +0x150).
    public const int TargetReferenceOffset = 0;
    public const int SectorOffset = 4;
    public const int TargetXOffset = 8;
    public const int TargetYOffset = 12;
    public const int SurfaceOffset = 16;
    public const int CodeOffset = 32;
}
