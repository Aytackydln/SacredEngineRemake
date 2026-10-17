using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Client 0x61DED0/0x61E070, server 0x4C723B/0x4C7274:
/// OR/AND-NOT the region record's +8 flags, then relay the original body.</summary>
public static class GoldWorldRegionFlagsCodec
{
    public const ushort SetOpcode = 0x185;
    public const ushort ClearOpcode = 0x186;
    public const int Size = 8;

    public static GoldWorldRegionFlagsEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (SetOpcode or ClearOpcode) || message.Payload.Length != Size)
            throw new InvalidDataException("Gold region flags require opcode 0x185/0x186 and eight payload bytes.");
        return new(message, BinaryPrimitives.ReadUInt32LittleEndian(message.Payload),
            BinaryPrimitives.ReadUInt32LittleEndian(message.Payload.AsSpan(4)), message.Opcode == SetOpcode);
    }

    public static GoldMessage Create(byte playerSlot, uint region, uint mask, bool set)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var data = new byte[Size];
        // The original writer masks the region before transmitting it.
        BinaryPrimitives.WriteUInt32LittleEndian(data, unchecked((byte)region));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), mask);
        return new(set ? SetOpcode : ClearOpcode, playerSlot, playerSlot, 0, data);
    }
}

