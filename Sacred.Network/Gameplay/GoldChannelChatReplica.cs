namespace Sacred.Network.Gameplay;

public sealed record GoldChannelChatDelivery(GoldChannelChatEvent Event, string? SpeakerName, bool IncludeSpeaker);

/// <summary>Channel chat applies native origin and local ignore-mask gates before recording presentation work.</summary>
public sealed class GoldChannelChatReplica
{
    private readonly List<GoldChannelChatDelivery> _messages = [];
    private readonly string[] _names = new string[17];
    private bool _usePlayerNames;
    public GoldChannelChatReplica() => Messages = _messages.AsReadOnly();
    public IReadOnlyList<GoldChannelChatDelivery> Messages { get; }
    public ushort IgnoredPlayersMask { get; private set; }
    public long ReceivedCount { get; private set; }
    public long SuppressedCount { get; private set; }
    public GoldChannelChatDelivery? LastDelivery { get; private set; }
    internal event Action<GoldChannelChatDelivery>? Published;

    /// <summary>Match the native active connection query, including chat received before Sacred slot assignment.</summary>
    public void SetConnectionActive(bool active) => _usePlayerNames = active;

    /// <summary>Local chat preference, matching native slot-1 bits. This never sends a packet.</summary>
    public void SetPlayerIgnored(byte slot, bool ignored)
    {
        if (slot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(slot));
        var bit = (ushort)(1 << (slot - 1));
        IgnoredPlayersMask = ignored ? (ushort)(IgnoredPlayersMask | bit) : (ushort)(IgnoredPlayersMask & ~bit);
    }

    internal void Apply(GoldChannelChatEvent entry)
    {
        var decoded = GoldChannelChatCodec.Decode(entry.Message); var origin = decoded.Message.Origin;
        ReceivedCount++; LastDelivery = null;
        if (origin > 16 || origin > 0 && (IgnoredPlayersMask & (1 << (origin - 1))) != 0)
        { SuppressedCount++; return; }
        var includeSpeaker = origin == 0 || _usePlayerNames;
        LastDelivery = new(decoded, origin == 0 || !_usePlayerNames ? null : _names[origin] ?? string.Empty, includeSpeaker);
        _messages.Add(LastDelivery);
        if (_messages.Count > GoldChatReplica.HistoryLimit) _messages.RemoveAt(0);
        Published?.Invoke(LastDelivery);
    }

    internal void ApplyRoster(GoldPlayerRosterEvent entry)
    {
        var roster = GoldGameEventDecoder.Decode(entry.Message) as GoldPlayerRosterEvent
            ?? throw new InvalidDataException("Expected a complete Gold roster for chat names.");
        for (var slot = 1; slot <= 16; slot++) _names[slot] = roster.Roster.GetEntry(slot).Name;
    }
    internal void ClearPlayer(byte slot) { if (slot <= 16) _names[slot] = string.Empty; }
    internal void EnterSession() => _usePlayerNames = true;
    internal void Clear()
    {
        _messages.Clear(); Array.Clear(_names); _usePlayerNames = false;
        IgnoredPlayersMask = 0; ReceivedCount = SuppressedCount = 0; LastDelivery = null;
    }
}
