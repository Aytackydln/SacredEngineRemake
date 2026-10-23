using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Incremental script tables also imported by world banks.</summary>
public abstract record GoldScriptWorldEvent(GoldMessage Message) : GoldGameEvent(Message);
