using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold's direction-dependent 0x153 object reservations and 0x154 world-trigger ranges.</summary>
public static class GoldReferenceCodec
{
    public const ushort BatchOpcode = 0x153;
    public const ushort RangeOpcode = 0x154;
    public const uint FirstObjectReference = 0x11;
    public const uint ReferenceLimit = 0x1FFFC;
    /// <summary>Gold client 0x6387B0 requests this many trigger IDs when fewer than six remain.</summary>
    public const uint TriggerRangeRequestCount = 200;

    /// <summary>Client request at server 0x4C48B5/0x4D6110: one word, count strictly between zero and 0x1FFFC.</summary>
    public static GoldMessage CreateRequest(uint count, byte playerSlot)
    {
        if (count is 0 or >= ReferenceLimit) throw new ArgumentOutOfRangeException(nameof(count));
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, count);
        return new GoldMessage(BatchOpcode, playerSlot, playerSlot, 0, payload);
    }

    /// <summary>Native server selector 0x66 compares the first word to its last issued trigger ID,
    /// then replies with last+1/count. The request's first word is not the reply's start ID.</summary>
    public static GoldMessage CreateTriggerRangeRequest(uint lastKnownBound, uint count, byte playerSlot)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, lastKnownBound);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), count);
        return new(RangeOpcode, playerSlot, playerSlot, 0, payload);
    }
}
