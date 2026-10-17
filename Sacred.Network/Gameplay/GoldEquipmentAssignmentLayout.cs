namespace Sacred.Network.Gameplay;

/// <summary>Gold client 0x7DD9C2 / GameServer 0x4C54F9: assign a native creature equipment reference.</summary>
public static class GoldEquipmentAssignmentLayout
{
    public const ushort Opcode = 0x11E;
    public const int Size = 12;
    public const int CreatureReferenceOffset = 0;
    public const int SlotOffset = 4;
    public const int ItemReferenceOffset = 8;
    public const int SpecialSlot = 18;
    public const uint SpecialSlotActorFlag = 0x80;
    public const uint ItemAssignmentClearFlag = 0x100000;
    public const uint ItemOutsideWorldFlag = 0x2;
}
