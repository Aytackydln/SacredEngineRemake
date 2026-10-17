using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldCreatureUpdateEvent(GoldMessage Message, uint ObjectReference, uint ObjectType,
    GoldCreatureUpdateState State) : GoldGameEvent(Message);

/// <summary>0x11B carries the compact embedded values only; no following creature list/trailer.</summary>
public sealed record GoldCreatureValuesEvent(GoldMessage Message, uint ObjectReference, uint ObjectType,
    GoldCreatureValuesState Values) : GoldGameEvent(Message);
