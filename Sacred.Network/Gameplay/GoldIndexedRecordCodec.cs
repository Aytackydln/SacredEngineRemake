using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldIndexedRecordEvent(GoldMessage Message, uint Index) : GoldScriptWorldEvent(Message);

/// <summary>Gold 15D assigns a 188-byte record using field copies, preserving alignment holes.</summary>
public static class GoldIndexedRecordCodec
{
    public const ushort Opcode = 0x15D;
    public const int RecordSize = 188;
    public const int Size = 4 + RecordSize;
    public static GoldIndexedRecordEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < Size)
            throw new InvalidDataException("Gold indexed record requires an index and 188 bytes.");
        var index = BinaryPrimitives.ReadUInt32LittleEndian(message.Payload);
        GoldQuestLayout.RequireIndex(index, 1, RecordSize);
        return new(message with { Payload = message.Payload.ToArray() }, index);
    }
}
