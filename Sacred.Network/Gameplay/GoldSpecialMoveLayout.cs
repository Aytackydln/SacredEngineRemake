namespace Sacred.Network.Gameplay;

/// <summary>CREATURE_SPECIALMOVE, Gold 0x805ED0 / event 0x211 at 0x536BD9.</summary>
public static class GoldSpecialMoveLayout
{
    public const ushort Opcode = 0x12D;
    public const int ReferenceOffset = 0;
    public const int ActionBytesOffset = 4;
    public const int ActionOffset = 8;
    public const int MaximumActionBytes = 84;
    public const int MaximumReferenceBytes = 80;
    // Offsets within the native +0x150 action bank. Receive replaces stage/substage.
    public const int TargetReferenceOffset = 4;
    public const int SectorOffset = 8;
    public const int TargetXOffset = 12;
    public const int TargetYOffset = 16;
    public const int SurfaceOffset = 20;
    public const int CodeOffset = 36;
}
