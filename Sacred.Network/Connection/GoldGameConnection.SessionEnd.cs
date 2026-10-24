using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.Network.Connection;

public sealed partial class GoldGameConnection
{
    private static GoldSessionNoticeEvent? FindSessionEnd(IReadOnlyList<GoldMessage> messages)
    {
        if (!messages.Any(message => message.Opcode is GoldSessionNoticeCodec.DisconnectOpcode or GoldSessionNoticeCodec.EndSessionOpcode or
                GoldSessionNoticeCodec.ConnectionLostOpcode or GoldSessionNoticeCodec.ConnectionLostAlternateOpcode)) return null;
        GoldSessionNoticeEvent? result = null;
        // A malformed companion must not let a partial terminal notification escape to the game thread.
        GoldGameDispatcher.Dispatch(messages, entry =>
        {
            if (entry is GoldSessionNoticeEvent { EndsSession: true } notice) result ??= notice;
        });
        return result;
    }
}
