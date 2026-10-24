using System.Globalization;
using Sacred.Core.GameRes;

namespace Sacred.Network.Gameplay;

public sealed record GoldBuddyNotice(GoldBuddyRecord Buddy, bool IsQuery)
{
    public uint NativeChatSelector => IsQuery ? 0u : 17u;
}

/// <summary>Native first-match buddy vector updates and notification gates; UI/chat consumes Notices.</summary>
public sealed class GoldBuddyReplica
{
    private readonly List<GoldBuddyRecord> _buddies = [];
    private readonly List<GoldBuddyNotice> _notices = [];
    private GameResStore _resources = GameResStore.Empty;
    public GoldBuddyReplica() { Buddies = _buddies.AsReadOnly(); Notices = _notices.AsReadOnly(); }
    public IReadOnlyList<GoldBuddyRecord> Buddies { get; }
    public IReadOnlyList<GoldBuddyNotice> Notices { get; }
    public long ReceivedCount { get; private set; }
    /// <summary>Initial service buddy vector, including any duplicate IDs. Supply before receiving updates.</summary>
    public void Seed(IEnumerable<GoldBuddyRecord> buddies)
    {
        ArgumentNullException.ThrowIfNull(buddies);
        if (ReceivedCount != 0) throw new InvalidOperationException("Seed the buddy baseline before receipt.");
        var owned = buddies.ToArray();
        if (owned.Any(b => b is null)) throw new ArgumentException("Null buddy baseline row.", nameof(buddies));
        _buddies.Clear(); _buddies.AddRange(owned);
    }
    public void ConfigureResources(GameResStore resources) => _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    internal void Apply(GoldBuddyEvent incoming)
    {
        var entry = GoldBuddyCodec.Decode(incoming.Message); var buddy = entry.Buddy; ReceivedCount++;
        if (entry.Message.Opcode == GoldBuddyCodec.QueryOpcode) { _notices.Add(new(buddy, true)); return; }
        var index = _buddies.FindIndex(row => row.Id == buddy.Id);
        if (index < 0) { _buddies.Add(buddy); return; }
        var old = _buddies[index]; var changed = old.Status != buddy.Status || old.ChannelIndex != buddy.ChannelIndex;
        buddy = buddy with { Name = old.Name }; _buddies[index] = buddy;
        if (changed && !(buddy.Status == 1 && buddy.ChannelIndex == -1 && old.Status != 0)) _notices.Add(new(buddy, false));
    }
    public string? Resolve(GoldBuddyNotice notice)
    {
        var b = notice.Buddy;
        string? Resource(string key) => _resources.Strings.GetValueOrDefault(SacredResourceHash.Compute(key));
        var channel = unchecked(b.ChannelIndex + 1).ToString(CultureInfo.InvariantCulture);
        if (notice.IsQuery)
        {
            var key = b.Id == uint.MaxValue ? "UI_MSG_USERNOTFOUND" : b.Status switch
            {
                0 => "UI_MSG_USEROFFLINE", 1 when b.ChannelIndex >= 0 => "UI_MSG_USERFOUND",
                2 when b.GameWord != 0 => "UI_MSG_USERINGAME", 1 or 2 => "UI_MSG_USERONLINE", _ => null
            };
            if (key is null) return "";
            if (Resource(key) is not { } text) return null;
            if (b.Id == uint.MaxValue || b.Status == 0) return $"'{b.Name}' {text}";
            if (b.Status == 1 && b.ChannelIndex >= 0) return $"'{b.Name}' {text} #{channel} ('{b.ChannelName}')!";
            if (b.Status == 2 && b.GameWord != 0) return $"'{b.Name}' {text} '{b.GameName}' (#{channel} '{b.ChannelName}')!";
            return $"'{b.Name}' {text}!";
        }
        if (b.Id == uint.MaxValue || b.Status > 2) return "";
        if (Resource("UI_BUDDY_LIST") is not { } prefix) return null;
        if (b.Status == 1 && b.ChannelIndex >= 0) return $"{prefix}: {b.Name} --> #{channel} {b.ChannelName}";
        if (b.Status == 2 && b.GameWord != 0) return $"{prefix}: {b.Name} --> '{b.GameName}' (#{channel} {b.ChannelName})";
        var state = Resource(b.Status == 0 ? "UI_NET_STATUS_OFFLINE" : "UI_NET_STATUS_ONLINE");
        return state is null ? null : $"{prefix}: {b.Name} --> {state}";
    }
    internal void Clear() { _buddies.Clear(); _notices.Clear(); ReceivedCount = 0; }
}
