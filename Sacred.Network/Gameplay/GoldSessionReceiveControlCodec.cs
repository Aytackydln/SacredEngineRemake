using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldSessionReceiveControlEvent(GoldMessage Message, bool Enabled) : GoldGameEvent(Message);

/// <summary>Gold 01F/020 write singleton AA4544's first byte through 7F5E10/7F5E00.
/// Its wider gameplay purpose is unresolved; it is independent of pause, locks and world approval.</summary>
public static class GoldSessionReceiveControlCodec
{
    public const ushort DisableOpcode = 0x01F;
    public const ushort EnableOpcode = 0x020;

    public static GoldSessionReceiveControlEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (DisableOpcode or EnableOpcode))
            throw new InvalidDataException("Not a Gold singleton receive control.");
        return new(message with { Payload = message.Payload.ToArray() }, message.Opcode == EnableOpcode);
    }
}
