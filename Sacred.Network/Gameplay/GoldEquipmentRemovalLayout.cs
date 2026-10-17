namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x11D at GameServer 0x4C55BD: conditional removal from native creature +0x1A4.</summary>
public static class GoldEquipmentRemovalLayout
{
    public const ushort Opcode = 0x11D;
    public const int Size = 12;
    public const int CreatureReferenceOffset = 0;
    public const int SlotOffset = 4;
    public const int ExpectedItemReferenceOffset = 8;
    public const int SlotCount = GoldCreatureStateLayout.EquipmentCount;
}
