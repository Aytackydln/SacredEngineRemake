using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldScriptVariableEvent(GoldMessage Message, uint? Index, GoldScriptVariableRecord Variable) : GoldGameEvent(Message);
