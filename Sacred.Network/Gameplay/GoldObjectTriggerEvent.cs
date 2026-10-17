using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldObjectTriggerEvent(GoldMessage Message, uint ObjectReference,
    uint TriggerReference) : GoldGameEvent(Message);
