using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldScriptRegionEvent(GoldMessage Message, uint Mode, uint FirstKey, uint SecondKey,
    GoldScriptRegionRecord Region) : GoldScriptWorldEvent(Message);

/// <summary>SetRG2 publication: mode and two sector keys, followed by a verbatim sRegion.</summary>
public static class GoldScriptRegionCodec
{
    public const ushort Opcode = 0x1C5;
    public const int MinimumSize = 44;
    public static GoldScriptRegionEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < MinimumSize)
            throw new InvalidDataException("Gold script-region update requires three words and a 32-byte record.");
        var bytes = message.Payload.AsSpan();
        return new(message with { Payload = bytes.ToArray() }, BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),
            GoldScriptRegionRecord.FromPayload(bytes.Slice(12, GoldScriptRegionRecord.Size)));
    }
}
