namespace Sacred.Network.Gameplay;

/// <summary>Client 0x148 request built by Gold 0x6263B0; distinct from the server's sector-entry reply.</summary>
public static class GoldSectorInterestLayout
{
    public const ushort Opcode = 0x148;
    public const int Size = 62;
    public const int SlotCount = 9;
    public const int CurrentSectorOffset = 0;
    public const int PreviousSectorOffset = 2;
    public const int EnteredSectorsOffset = 4;
    public const int LeftSectorsOffset = 22;
    public const int ReservedOffset = 40;
    public const int ReservedSize = 18;
    public const int FreeReferenceCountOffset = 58;
}
