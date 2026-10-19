using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldItemPickupEvent(GoldMessage Message, uint ObjectReference, uint FacingBits) : GoldGameEvent(Message);
