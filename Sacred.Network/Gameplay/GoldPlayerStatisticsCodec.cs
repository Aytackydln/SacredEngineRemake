using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldPlayerStatisticsCodec
{
    public static GoldPlayerStatisticsEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != GoldPlayerStatisticsLayout.Opcode) throw new ArgumentOutOfRangeException(nameof(message));
        return new(message, GoldPlayerStatisticsState.FromPayload(message.Payload));
    }

    public static GoldMessage Create(byte playerSlot, GoldPlayerStatisticsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        return new(GoldPlayerStatisticsLayout.Opcode, playerSlot, playerSlot, 0, state.ToPayload());
    }
}
