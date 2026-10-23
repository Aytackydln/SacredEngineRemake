using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldClientHostRoleEvent(GoldMessage Message) : GoldGameEvent(Message)
{
    public bool IsClientHost => Message.Opcode == GoldClientHostRoleCodec.GrantedOpcode;
}

/// <summary>Gold 00D/00E switch the client's host-role byte. Their payload is not read.</summary>
public static class GoldClientHostRoleCodec
{
    public const ushort GrantedOpcode = 0x00D;
    public const ushort RevokedOpcode = 0x00E;
    public static GoldClientHostRoleEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (GrantedOpcode or RevokedOpcode))
            throw new InvalidDataException("Expected Gold client-host role notification.");
        return new(message with { Payload = message.Payload.ToArray() });
    }
}
