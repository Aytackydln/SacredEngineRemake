using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldEquipmentAssignmentEvent(GoldMessage Message, uint CreatureReference,
    uint Slot, uint ItemReference) : GoldGameEvent(Message);
