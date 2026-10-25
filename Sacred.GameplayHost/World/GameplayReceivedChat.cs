using Sacred.Core.GameRes;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>Visible received chat. Ignore gates and receipt-time names are supplied by the replica.</summary>
public sealed class GameplayReceivedChat
{
    private readonly GoldWorldReplica _world;
    private readonly GoldConsumerCursor _cursor;
    private readonly List<GameplayHudNotice> _lines = [];
    private long _session = -1;
    public GameplayReceivedChat(GoldWorldReplica world)
    { _world = world; _cursor = world.Consumers.CreateCursor("chat-lines", w => w.Family == "chat"); }
    public IReadOnlyList<GameplayHudNotice> Lines => _lines;

    public void Advance(float seconds, GameResStore resources)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (_session != _world.Consumers.Session) { _lines.Clear(); _session = _world.Consumers.Session; }
        foreach (var line in _lines) line.Advance(seconds);
        _lines.RemoveAll(line => line.Expired);
        while (_world.Consumers.TryPeek(_cursor, out var work))
        {
            var (text, server, channel) = work.Value switch
            {
                GoldChannelChatDelivery delivery => (Format(delivery, resources), delivery.Event.IsServerMessage, delivery.Event.Channel),
                GoldChatEvent chat => (Format(chat, resources), chat.IsServerMessage, chat.IsPrivate ? GoldChatChannel.Private : GoldChatChannel.Public),
                _ => ((string?)null, false, GoldChatChannel.Public)
            };
            if (text is null)
            { _world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Deferred, "waiting for original chat labels"); break; }
            // Gold 7ED4A0 gives server status priority over private/team colour.
            var color = server ? 0xFFFF6347u : channel == GoldChatChannel.Private ? 0xFF00A0FFu :
                channel == GoldChatChannel.Team ? 0xFFFFE400u : 0xFFFFFFFFu;
            _lines.Add(new(text, 1, work.Order, 1, color));
            // Rendering retention, independently of the replica's diagnostic history.
            if (_lines.Count > 128) _lines.RemoveAt(0);
            _world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, text);
        }
    }

    private static string? Format(GoldChannelChatDelivery delivery, GameResStore resources)
    {
        var channel = delivery.Event.Channel;
        var label = channel == GoldChatChannel.Public ? string.Empty : Label(resources,
            channel == GoldChatChannel.Private ? "UI_NET_PRIVATE" : "UI_NET_TEAM");
        var speaker = delivery.Event.IsServerMessage ? Label(resources, "UI_NET_SERVER") : delivery.SpeakerName;
        if (label is null || delivery.Event.IsServerMessage && speaker is null) return null;
        return Prefix(label, delivery.IncludeSpeaker ? speaker : null, delivery.Event.Text);
    }
    private static string? Format(GoldChatEvent chat, GameResStore resources)
    {
        var label = chat.IsPrivate ? Label(resources, "UI_NET_PRIVATE") : string.Empty;
        var speaker = chat.IsServerMessage ? Label(resources, "UI_NET_SERVER") : chat.Name;
        return label is null || chat.IsServerMessage && speaker is null ? null : Prefix(label, speaker, chat.Text);
    }
    private static string? Label(GameResStore resources, string key) => resources.Strings.GetValueOrDefault(SacredResourceHash.Compute(key));
    private static string Prefix(string? label, string? speaker, string text) =>
        (string.IsNullOrEmpty(label) ? "" : label + " ") + (speaker is null ? "" : speaker + ": ") + text;
}
