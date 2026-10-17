namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x10D, consumed at 0x4D2C60. Coordinates are forwarded but unused by the name-deletion helper.</summary>
public static class GoldMovementTriggerDeletionLayout
{
    public const ushort Opcode = 0x10D;
    public const int SectorOffset = 0;
    public const int FirstXOffset = 4;
    public const int FirstYOffset = 8;
    public const int SecondXOffset = 12;
    public const int SecondYOffset = 16;
    public const int NameOffset = 20;
    public const int MinimumSize = NameOffset + 1;
    /// <summary>Native sector table contains entries 0..6500, inclusive.</summary>
    public const uint MaximumSector = 6500;
}
