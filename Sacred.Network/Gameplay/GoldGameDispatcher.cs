using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Validates a whole batch before delivering any event to the game thread.</summary>
public static class GoldGameDispatcher
{
    public static void Dispatch(IReadOnlyList<GoldMessage> messages, Action<GoldGameEvent> receive)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(receive);
        var events = new GoldGameEvent[messages.Count];
        for (var i = 0; i < messages.Count; i++) events[i] = GoldGameEventDecoder.Decode(messages[i]);
        foreach (var gameEvent in events) receive(gameEvent);
    }
}
