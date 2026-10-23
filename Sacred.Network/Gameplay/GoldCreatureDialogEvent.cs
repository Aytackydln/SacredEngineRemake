using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldCreatureDialogEvent(GoldMessage Message, uint ObjectReference, uint DialogIndex, uint ObjectFlags)
    : GoldGameEvent(Message);
