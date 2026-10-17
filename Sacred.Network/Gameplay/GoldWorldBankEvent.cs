using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldWorldBankEvent(GoldMessage Message, GoldWorldBankSnapshot Snapshot) : GoldGameEvent(Message);
