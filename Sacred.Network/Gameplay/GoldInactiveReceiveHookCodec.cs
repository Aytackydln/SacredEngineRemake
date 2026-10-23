using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>The inspected Gold client deliberately does nothing for these seven receive hooks.</summary>
public sealed record GoldInactiveReceiveHookEvent(GoldMessage Message) : GoldGameEvent(Message);

public static class GoldInactiveReceiveHookCodec
{
    public const ushort Hook18EOpcode = 0x18E;
    public const ushort Hook190Opcode = 0x190;
    public const ushort Hook191Opcode = 0x191;
    public const ushort Hook192Opcode = 0x192;
    public const ushort Hook193Opcode = 0x193;
    public const ushort Hook194Opcode = 0x194;
    public const ushort Hook195Opcode = 0x195;

    public static GoldInactiveReceiveHookEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (Hook18EOpcode or Hook190Opcode or Hook191Opcode or Hook192Opcode
            or Hook193Opcode or Hook194Opcode or Hook195Opcode))
            throw new InvalidDataException("Not an inactive Gold receive hook.");
        // Native adjusts a payload pointer and length, then calls a bare ret 12.
        // It never reads even the skipped prefix: empty and arbitrary payloads are valid on receipt.
        return new(message with { Payload = message.Payload.ToArray() });
    }
}
