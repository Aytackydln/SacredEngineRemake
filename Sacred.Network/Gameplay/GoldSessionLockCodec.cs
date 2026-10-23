using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldSessionLockEvent(GoldMessage Message) : GoldGameEvent(Message)
{
    public bool IsLocked => Message.Opcode == GoldSessionLockCodec.LockOpcode;
}

/// <summary>Gold session lock/unlock notifications have no consumed payload fields. Native receivers ignore suffix bytes.</summary>
public static class GoldSessionLockCodec
{
    public const ushort UnlockOpcode = 0x019;
    public const ushort LockOpcode = 0x01A;
    public static GoldSessionLockEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (LockOpcode or UnlockOpcode)) throw new InvalidDataException("Not a Gold session lock notification.");
        return new(message with { Payload = message.Payload.ToArray() });
    }
}
