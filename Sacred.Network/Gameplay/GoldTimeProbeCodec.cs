using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x43 probe and 0x44 reply, produced by client 0x7E3BC3 and consumed by server 0x4CC4FD.</summary>
public static class GoldTimeProbeCodec
{
    public const ushort RequestOpcode = 0x43;
    public const ushort ReplyOpcode = 0x44;
    public const int RequestBytes = 16;
    public const int ReplyBytes = 12;

    /// <summary>Echo the first two native words, followed by the client's elapsed millisecond clock.</summary>
    public static GoldMessage CreateReply(GoldTimeProbeEvent probe, byte playerSlot, uint clientMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var payload = new byte[ReplyBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, probe.Word0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), probe.Word1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), clientMilliseconds);
        return new GoldMessage(ReplyOpcode, playerSlot, playerSlot, 0, payload);
    }
}
