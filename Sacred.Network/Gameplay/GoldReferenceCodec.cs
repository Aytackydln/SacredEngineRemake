using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold's direction-dependent 0x153 reference request/allocation and 0x154 range reply.</summary>
public static class GoldReferenceCodec
{
    public const ushort BatchOpcode = 0x153;
    public const ushort RangeOpcode = 0x154;
    public const uint FirstObjectReference = 0x11;
    public const uint ReferenceLimit = 0x1FFFC;

    /// <summary>Client request at server 0x4C48B5/0x4D6110: one word, count strictly between zero and 0x1FFFC.</summary>
    public static GoldMessage CreateRequest(uint count, byte playerSlot)
    {
        if (count is 0 or >= ReferenceLimit) throw new ArgumentOutOfRangeException(nameof(count));
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, count);
        return new GoldMessage(BatchOpcode, playerSlot, playerSlot, 0, payload);
    }
}
