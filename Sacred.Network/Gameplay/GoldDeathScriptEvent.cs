using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldDeathScriptEvent(GoldMessage Message,
    GoldDeathScriptRegistration Registration) : GoldGameEvent(Message);
