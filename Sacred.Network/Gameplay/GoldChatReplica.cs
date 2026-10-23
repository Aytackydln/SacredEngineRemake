namespace Sacred.Network.Gameplay;

/// <summary>Ordered received chat for presentation. The bounded history is a managed retention policy, not a native UI limit.</summary>
public sealed class GoldChatReplica
{
    public const int HistoryLimit = 128;
    private readonly List<GoldChatEvent> _messages = [];
    public GoldChatReplica() => Messages = _messages.AsReadOnly();
    public IReadOnlyList<GoldChatEvent> Messages { get; }
    public long ReceivedCount { get; private set; }
    internal void Apply(GoldChatEvent entry)
    {
        var decoded = GoldChatCodec.Decode(entry.Message);
        _messages.Add(decoded); ReceivedCount++;
        if (_messages.Count > HistoryLimit) _messages.RemoveAt(0);
    }
    internal void Clear() { _messages.Clear(); ReceivedCount = 0; }
}
