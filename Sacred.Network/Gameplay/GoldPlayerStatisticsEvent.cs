using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldPlayerStatisticsEvent(GoldMessage Message,
    GoldPlayerStatisticsState State) : GoldGameEvent(Message);
