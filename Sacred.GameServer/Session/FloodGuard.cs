using System.Globalization;

namespace Sacred.GameServer.Session;

/// <summary>
/// Flood protection settings (<c>FLOOD_*</c> keys). GameServer.exe has none, so it is off in plain relay games and on by
/// default with <c>--authoritative</c>; <c>--flood-protection=1</c>/<c>0</c> overrides either way.
/// </summary>
public sealed class FloodOptions
{
    private bool? _enabled;
    /// <summary>Explicit switch; null follows the authoritative mode.</summary>
    public bool? Enabled { get => _enabled; set => _enabled = value; }
    /// <summary>Sustained messages per second and the burst a player may use up at once.</summary>
    public int MessagesPerSecond { get; set; } = 600;
    public int MessageBurst { get; set; } = 3000;
    /// <summary>Sustained received bytes per second (TinCat payloads, compressed as received) and their burst.</summary>
    public int BytesPerSecond { get; set; } = 2 * 1024 * 1024;
    public int ByteBurst { get; set; } = 8 * 1024 * 1024;
    /// <summary>Chat lines (0x003/0x004/0x005) per second and burst; excess lines are dropped without a kick.</summary>
    public int ChatPerSecond { get; set; } = 3;
    public int ChatBurst { get; set; } = 8;
    /// <summary>Dropped messages or frames within <see cref="StrikeWindowSeconds"/> after which the player is removed (0 = never).</summary>
    public int KickAfterDrops { get; set; } = 500;
    public int StrikeWindowSeconds { get; set; } = 10;
    /// <summary>After admission, a player that has not entered the world yet may upload its world/hero (object states, banks,
    /// inventories) this long without limits: a host's initial upload is a large burst.</summary>
    public int UploadGraceSeconds { get; set; } = 60;
    /// <summary>Open connections from one IP address (0 = unlimited). Several players behind one NAT share an address.</summary>
    public int MaxConnectionsPerAddress { get; set; } = 8;

    public bool IsEnabled(bool authoritative) => _enabled ?? authoritative;

    public bool Apply(string key, string value)
    {
        switch (key)
        {
            case "FLOOD_PROTECTION": _enabled = Int(value, 1) != 0; return true;
            case "FLOOD_MESSAGES_PER_SECOND": MessagesPerSecond = Positive(value, MessagesPerSecond); return true;
            case "FLOOD_MESSAGE_BURST": MessageBurst = Positive(value, MessageBurst); return true;
            case "FLOOD_BYTES_PER_SECOND": BytesPerSecond = Positive(value, BytesPerSecond); return true;
            case "FLOOD_BYTE_BURST": ByteBurst = Positive(value, ByteBurst); return true;
            case "FLOOD_CHAT_PER_SECOND": ChatPerSecond = Positive(value, ChatPerSecond); return true;
            case "FLOOD_CHAT_BURST": ChatBurst = Positive(value, ChatBurst); return true;
            case "FLOOD_KICK_AFTER_DROPS": KickAfterDrops = Math.Max(0, Int(value, KickAfterDrops)); return true;
            case "FLOOD_STRIKE_WINDOW": StrikeWindowSeconds = Positive(value, StrikeWindowSeconds); return true;
            case "FLOOD_UPLOAD_GRACE": UploadGraceSeconds = Math.Max(0, Int(value, UploadGraceSeconds)); return true;
            case "MAX_CONNECTIONS_PER_ADDRESS": MaxConnectionsPerAddress = Math.Max(0, Int(value, MaxConnectionsPerAddress)); return true;
        }
        return false;
    }

    private static int Int(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    private static int Positive(string value, int fallback) => Math.Max(1, Int(value, fallback));
}

/// <summary>Token bucket in milli-tokens; allocation free, game loop only.</summary>
public struct RateBucket
{
    private long _milliTokens, _lastMs;
    private bool _started;

    /// <summary>Takes <paramref name="cost"/> tokens if available (refilled at <paramref name="perSecond"/>, capped at <paramref name="burst"/>).</summary>
    public bool TryTake(long nowMs, long cost, long perSecond, long burst)
    {
        var capacity = burst * 1000;
        if (!_started) { _started = true; _milliTokens = capacity; _lastMs = nowMs; }
        var elapsed = nowMs - _lastMs;
        if (elapsed > 0)
        {
            _milliTokens += Math.Min(elapsed * perSecond, capacity);
            _lastMs = nowMs;
        }
        if (_milliTokens > capacity) _milliTokens = capacity;     // a lowered burst applies at once
        var need = cost * 1000;
        if (_milliTokens < need) return false;
        _milliTokens -= need;
        return true;
    }
}

/// <summary>Per-session flood state (embedded in <see cref="PlayerSession"/>).</summary>
public struct FloodState
{
    public RateBucket Messages, Bytes, Chat;
    public long AdmittedMs;
    public long StrikeWindowStartMs;
    public int Strikes;
    public long DroppedTotal;
}

/// <summary>The checks themselves; pure, so they are tested without sockets.</summary>
public static class FloodGuard
{
    public enum Verdict : byte { Allow, Drop, Kick }

    public static bool IsChat(ushort opcode) => opcode is 0x003 or 0x004 or 0x005;

    /// <summary>World/hero upload messages that are free during the upload grace (before the player is in the world).</summary>
    public static bool IsUpload(ushort opcode) => opcode is 0x169 or 0x16B or 0x16C or 0x16E or 0x16F or 0x122 or 0x14F
        or 0x015 or 0x016 or 0x1B1 or 0x1B2 or 0x1B3 or 0x1B4 or 0x153 or 0x154 or 0x148;

    public static bool InUploadGrace(in FloodState state, bool inGame, long nowMs, FloodOptions options) =>
        !inGame && state.AdmittedMs > 0 && nowMs - state.AdmittedMs < options.UploadGraceSeconds * 1000L;

    /// <summary>One received TinCat payload of <paramref name="bytes"/> bytes.</summary>
    public static Verdict Frame(ref FloodState state, int bytes, bool uploadGrace, long nowMs, FloodOptions options)
    {
        if (uploadGrace || state.Bytes.TryTake(nowMs, bytes, options.BytesPerSecond, options.ByteBurst)) return Verdict.Allow;
        return Strike(ref state, nowMs, options);
    }

    /// <summary>One Gold message.</summary>
    public static Verdict Message(ref FloodState state, ushort opcode, bool uploadGrace, long nowMs, FloodOptions options)
    {
        if (IsChat(opcode) && !state.Chat.TryTake(nowMs, 1, options.ChatPerSecond, options.ChatBurst))
        {
            state.DroppedTotal++;
            return Verdict.Drop;                                   // chat spam is muted, not punished
        }
        if (uploadGrace && IsUpload(opcode)) return Verdict.Allow;
        return state.Messages.TryTake(nowMs, 1, options.MessagesPerSecond, options.MessageBurst) ? Verdict.Allow : Strike(ref state, nowMs, options);
    }

    private static Verdict Strike(ref FloodState state, long nowMs, FloodOptions options)
    {
        state.DroppedTotal++;
        if (nowMs - state.StrikeWindowStartMs >= options.StrikeWindowSeconds * 1000L) { state.StrikeWindowStartMs = nowMs; state.Strikes = 0; }
        state.Strikes++;
        return options.KickAfterDrops > 0 && state.Strikes >= options.KickAfterDrops ? Verdict.Kick : Verdict.Drop;
    }
}
