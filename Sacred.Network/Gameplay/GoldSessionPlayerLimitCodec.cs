using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldSessionPlayerLimitEvent(GoldMessage Message, uint AnnouncedMaxPlayers) : GoldGameEvent(Message);

/// <summary>Gold 0x023 announces player capacity. Zero preserves capacity; native mode flags may cap it at four.</summary>
public static class GoldSessionPlayerLimitCodec
{
    public const ushort Opcode = 0x023;

    public static GoldSessionPlayerLimitEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < 4)
            throw new InvalidDataException("Gold player-limit notification requires a four-byte capacity word.");
        return new(message with { Payload = message.Payload.ToArray() }, BinaryPrimitives.ReadUInt32LittleEndian(message.Payload));
    }

    /// <summary>Use receipt-time game flags. Missing flags leave an ambiguous capacity unknown;
    /// a later flags snapshot must not retroactively reinterpret this notification.</summary>
    public static uint? Resolve(uint announced, uint? gameFlags, uint? previous)
    {
        if (announced == 0) return previous;
        var capacity = Math.Min(announced, 16u);
        if (capacity <= 4) return capacity;
        return gameFlags is { } flags ? (flags & 0x40010) != 0 ? 4u : capacity : null;
    }
}
