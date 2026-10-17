using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldDeathScriptCodec
{
    public static GoldDeathScriptEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != GoldDeathScriptLayout.Opcode) throw new ArgumentOutOfRangeException(nameof(message));
        return new(message, GoldDeathScriptRegistration.FromPayload(message.Payload));
    }

    public static GoldMessage Create(byte playerSlot, GoldDeathScriptRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        return new(GoldDeathScriptLayout.Opcode, playerSlot, playerSlot, 0, registration.ToPayload());
    }
}
