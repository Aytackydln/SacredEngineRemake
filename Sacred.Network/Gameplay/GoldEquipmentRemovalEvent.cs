using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Expected item zero means unconditional removal; otherwise the slot must still contain that reference.</summary>
public sealed record GoldEquipmentRemovalEvent(GoldMessage Message, uint CreatureReference,
    uint Slot, uint ExpectedItemReference) : GoldGameEvent(Message);
