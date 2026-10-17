using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldWorldBankCodec
{
    public static GoldWorldBankEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new(message, message.Sender == 0 ? GoldWorldBankSnapshot.FromServerPayload(message.Opcode, message.Payload) :
            GoldWorldBankSnapshot.FromPayload(message.Opcode, message.Payload));
    }

    /// <summary>Retains every header, record and unread suffix byte. Does not initialize a host world.</summary>
    public static GoldMessage Create(byte sender, byte origin, GoldWorldBankSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(snapshot.Opcode, sender, origin, 0, snapshot.ToPayload());
    }
}
