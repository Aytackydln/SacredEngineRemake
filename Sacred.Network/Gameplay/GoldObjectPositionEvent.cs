using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldObjectPositionEvent(GoldMessage Message, uint ObjectReference,
    GoldObjectPosition Position, uint FacingBits) : GoldGameEvent(Message);
