using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldCounterEvent(GoldMessage Message) : GoldScriptWorldEvent(Message);
public sealed record GoldCounterMessageEvent(GoldMessage Message, uint Mode, uint Flags, uint TextResource) : GoldCounterEvent(Message);
public sealed record GoldCounterTargetEvent(GoldMessage Message, uint TargetResource) : GoldCounterEvent(Message);
public sealed record GoldCounterCollectEvent(GoldMessage Message, uint ObjectReference, uint TargetResource,
    bool UpdatesCounter) : GoldCounterEvent(Message);

public static class GoldCounterCodec
{
    public static GoldCounterEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            GoldCounterLayout.MessageOpcode => 12,
            GoldCounterLayout.KillNoticeOpcode or GoldCounterLayout.CollectNoticeOpcode or GoldCounterLayout.KillOpcode => 4,
            GoldCounterLayout.CollectOpcode or GoldCounterLayout.CollectAndCountOpcode => 8,
            _ => throw new InvalidDataException("Expected Gold counter/message notification.")
        };
        if (message.Payload.Length < size) throw new InvalidDataException("Truncated Gold counter/message notification.");
        var copy = message with { Payload = message.Payload.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(copy.Payload.AsSpan(offset));
        return message.Opcode switch
        {
            GoldCounterLayout.MessageOpcode => new GoldCounterMessageEvent(copy, Word(0), Word(4), Word(8)),
            GoldCounterLayout.CollectOpcode or GoldCounterLayout.CollectAndCountOpcode =>
                new GoldCounterCollectEvent(copy, Word(0), Word(4), message.Opcode == GoldCounterLayout.CollectAndCountOpcode),
            _ => new GoldCounterTargetEvent(copy, Word(0))
        };
    }
}
