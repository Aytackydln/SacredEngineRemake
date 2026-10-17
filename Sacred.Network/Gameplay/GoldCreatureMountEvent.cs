using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Ordered bidirectional requests. Zero in either position can clear the other creature's link.</summary>
public sealed record GoldCreatureMountEvent(GoldMessage Message, uint FirstReference,
    uint SecondReference) : GoldGameEvent(Message);
