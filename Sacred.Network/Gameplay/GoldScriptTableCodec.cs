using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldScriptTableEvent(GoldMessage Message, GoldScriptTableSnapshot Snapshot) : GoldScriptWorldEvent(Message);

public static class GoldScriptTableCodec
{
    public static GoldScriptTableEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        GoldScriptTableLayout.GetRecordSize(message.Opcode);
        var copy = message with { Payload = message.Payload.ToArray() };
        return new(copy, GoldScriptTableSnapshot.FromPayload(copy.Opcode, copy.Payload));
    }
}
