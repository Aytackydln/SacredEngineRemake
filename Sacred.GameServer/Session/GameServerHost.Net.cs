using System.Runtime.CompilerServices;
using Sacred.GameServer.Scripting;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Session;

/// <summary>A named mod channel the server knows (registered by scripts or code). Clients may use any channel name between
/// themselves; the server only needs the registry for channels addressed to the server and for <see cref="ServerOnly"/>.</summary>
public sealed class NetChannel
{
    public required string Name { get; init; }
    public required uint Id { get; init; }
    /// <summary>Clients cannot relay through it (no target other than the server).</summary>
    public bool ServerOnly { get; set; }
}

/// <summary>Counters of everything the custom net ops dropped or did (diagnostics, tests).</summary>
public sealed class NetOpsStats
{
    public long HelloAccepted, Received, Delivered, Relayed, Sent, RateLimited, UnknownChannel, ServerOnlyRefused, Malformed, TooManyInFlight,
        TooManyChannels, TooLarge, ReassemblyLimit, TimedOut, Orphans, NotCapable, NoRecipient, ChatFramesSent;
}

/// <summary>Per session state of the custom net ops.</summary>
public sealed class NetOpsSession
{
    public bool Capable { get; internal set; }
    public byte Protocol { get; internal set; }
    public uint Features { get; internal set; }
    public uint ModHash { get; internal set; }
    internal double Tokens;
    internal long RefillMs;
    internal bool Primed;
    internal readonly HashSet<uint> Channels = new();
    internal readonly Dictionary<ushort, Inbound> InFlight = new();
    internal int ReassemblyBytes;
    internal ushort NextId;
    internal int SentPeers = -1;
    // the chat carrier (GameServerHost.NetChat.cs)
    /// <summary>The client speaks net channels (it answered the query or sent a channel message): messages can go over chat.</summary>
    public bool ChatCapable { get; internal set; }
    /// <summary>The client's chat answer accepted the server's custom message types: its HELLO is welcome.</summary>
    public bool Agreed { get; internal set; }
    /// <summary>The client announced itself over chat (so it may be written to over chat) / answered the server's query.</summary>
    public bool Announced { get; internal set; }
    public bool Answered { get; internal set; }
    internal int QueriesSent;
    internal long NextQueryMs;
    internal readonly Dictionary<int, GameServerHost.ChatInbound> ChatIn = new();
    internal readonly HashSet<int> BlockedChat = new();
    internal readonly Queue<byte[]> ChatOut = new();
    internal int ChatOutBytes, NextChatNumber;
    internal double ChatCredit;
    internal long ChatCreditMs;

    internal sealed class Inbound
    {
        public uint Channel;
        public byte Target;
        public bool Json;
        public bool Aborted;
        public int Total;
        public byte[]? Buffer;
        public int Length;
        public long LastMs;
        public uint Recipients;   // bit n = slot n+1 (relayed messages)
    }
}

/// <summary>
/// Custom net opcodes on the game server (<see cref="NetOpsWire"/>): handshake, peer list, server channels and relaying between
/// capable clients. Only players that sent HELLO ever receive the custom opcodes; everything else about a message (sender slot, size,
/// rate, channel count, memory) is decided here, never by the sender. Everything runs on the game loop thread.
/// </summary>
public sealed partial class GameServerHost
{
    public delegate void NetMessageHandler(PlayerSession from, NetChannel channel, bool json, byte[] data);

    private readonly ConditionalWeakTable<PlayerSession, NetOpsSession> _netSessions = new();
    private readonly Dictionary<uint, NetChannel> _netChannels = new();
    private readonly byte[] _netFrame = new byte[NetOpsWire.DataHeaderBytes + 4096];
    private long _nextNetSweepMs;

    public NetOpsStats NetStats { get; } = new();
    public NetOpsOptions NetOpsConfig => _config.NetOps;
    /// <summary>A complete message for a registered server channel (after reassembly). Tests and the script host listen here.</summary>
    public event NetMessageHandler? NetMessage;

    public NetOpsSession NetStateOf(PlayerSession s) => _netSessions.GetOrCreateValue(s);

    /// <summary>The session of <paramref name="slot"/> when it said HELLO.</summary>
    private PlayerSession? CapablePlayer(int slot) =>
        slot is >= 1 and <= MaxSlots && _slots[slot] is { State: >= SessionState.SlotAssigned } p && !p.Connection.IsClosed
        && _netSessions.TryGetValue(p, out var state) && state.Capable ? p : null;

    public bool NetSupports(int slot) => CapablePlayer(slot) is not null;

    /// <summary>The session of <paramref name="slot"/> when it can be sent net channel messages at all (custom types or chat).</summary>
    private PlayerSession? NetReachable(int slot) =>
        slot is >= 1 and <= MaxSlots && _slots[slot] is { State: >= SessionState.SlotAssigned } p && !p.Connection.IsClosed
        && _netSessions.TryGetValue(p, out var state) && (state.Capable || state.ChatCapable) ? p : null;

    /// <summary>"custom" when the player is on the custom message types, "chat" when it only speaks channels over chat, else null.</summary>
    public string? NetTransport(int slot) => CapablePlayer(slot) is not null ? "custom" : NetReachable(slot) is not null ? "chat" : null;

    // ---- channel registry -------------------------------------------------------------------------------------------

    public IReadOnlyCollection<NetChannel> NetChannels => _netChannels.Values;

    /// <summary>Registers (or returns) a channel. Throws <see cref="ArgumentException"/> for a bad name or when its id collides with
    /// another name.</summary>
    public NetChannel RegisterNetChannel(string name, bool serverOnly = false)
    {
        var limit = _config.NetOps.MaxNameLength;
        if (name.Length == 0 || name.Length > limit) throw new ArgumentException($"a channel name has 1 to {limit} characters");
        foreach (var c in name) if (c < 0x20 || c > 0x7E) throw new ArgumentException("a channel name uses printable ASCII only");
        var id = NetOpsWire.ChannelId(name);
        if (_netChannels.TryGetValue(id, out var existing))
        {
            if (existing.Name != name) throw new ArgumentException($"channel '{name}' collides with '{existing.Name}' (same id 0x{id:X8}); rename one");
            existing.ServerOnly |= serverOnly;
            return existing;
        }
        return _netChannels[id] = new NetChannel { Name = name, Id = id, ServerOnly = serverOnly };
    }

    public void ClearNetChannels() => _netChannels.Clear();

    // ---- receiving --------------------------------------------------------------------------------------------------

    /// <summary>Claims the two opcodes (the constructor calls it); with net ops off they stay unhandled like in the original server.</summary>
    private void InitializeNetOps()
    {
        var options = _config.NetOps;
        if (!options.Enabled) return;
        RegisterOpcode(options.Control, (PlayerSession s, in GoldMessageView m) => { NetStats.Received++; OnNetControl(s, NetStateOf(s), m.Payload); });
        RegisterOpcode(options.Data, (PlayerSession s, in GoldMessageView m) => { NetStats.Received++; OnNetData(s, NetStateOf(s), m, _clock.ElapsedMilliseconds); });
    }

    private void OnNetControl(PlayerSession s, NetOpsSession state, ReadOnlySpan<byte> payload)
    {
        if (!state.Agreed) { NetStats.NotCapable++; return; }       // HELLO is welcome only after the chat handshake agreed on the numbers
        if (!NetOpsWire.TryReadHello(payload, out var hello) || hello.Protocol < 1) { NetStats.Malformed++; return; }
        var options = _config.NetOps;
        state.Capable = true;
        state.Protocol = Math.Min(hello.Protocol, NetOpsWire.ProtocolVersion);
        state.Features = hello.Features;
        state.ModHash = hello.ModHash;
        state.SentPeers = -1;
        foreach (var message in state.InFlight.Values) ReleaseInbound(state, message);
        state.InFlight.Clear();
        NetStats.HelloAccepted++;
        var features = NetOpsWire.FeatureFragments | NetOpsWire.FeatureJson | NetOpsWire.FeatureRelay | (_scripts is not null ? NetOpsWire.FeatureServerChannels : 0u);
        Span<byte> ack = stackalloc byte[NetOpsWire.HelloAckBytes];
        NetOpsWire.WriteHelloAck(ack, new(state.Protocol, features, options.Data, (ushort)options.MaxFragment, (uint)options.MaxMessage,
            (uint)options.BytesPerSecond, (byte)options.MaxInFlight));
        SendMessage(s, options.Control, 0, 0, ack);
        Log.Info($"{s.Describe()} net ops HELLO: protocol {hello.Protocol}, features 0x{hello.Features:X}, mod set 0x{hello.ModHash:X8}");
    }

    private bool TakeNetTokens(NetOpsSession state, int bytes, long now)
    {
        var options = _config.NetOps;
        if (!state.Primed) { state.Primed = true; state.Tokens = options.Burst; state.RefillMs = now; }
        state.Tokens = Math.Min(options.Burst, state.Tokens + (now - state.RefillMs) * (options.BytesPerSecond / 1000.0));
        state.RefillMs = now;
        if (state.Tokens < bytes) return false;
        state.Tokens -= bytes;
        return true;
    }

    private void ReleaseInbound(NetOpsSession state, NetOpsSession.Inbound message)
    {
        state.ReassemblyBytes -= message.Length;
        message.Buffer = null; message.Length = 0;
    }

    private int NetReassemblyTotal()
    {
        var total = 0;
        foreach (var session in _sessions)
            if (session.State != SessionState.Closed && _netSessions.TryGetValue(session, out var other)) total += other.ReassemblyBytes;
        return total;
    }

    private void OnNetData(PlayerSession s, NetOpsSession state, in GoldMessageView m, long now)
    {
        var options = _config.NetOps;
        if (!state.Capable) { NetStats.NotCapable++; return; }
        if (!NetOpsWire.TryReadData(m.Payload, out var header, out var data) || data.Length > options.MaxFragment) { NetStats.Malformed++; return; }
        state.InFlight.TryGetValue(header.MessageId, out var message);
        if (!TakeNetTokens(state, m.Payload.Length, now))
        {
            NetStats.RateLimited++;
            if (message is not null) { message.Aborted = true; ReleaseInbound(state, message); if (header.Last) state.InFlight.Remove(header.MessageId); }
            else if (header.First && !header.Last && state.InFlight.Count < options.MaxInFlight)
                state.InFlight[header.MessageId] = new NetOpsSession.Inbound { Channel = header.Channel, Aborted = true, LastMs = now };
            return;
        }
        if (header.First)
        {
            if (message is not null) { ReleaseInbound(state, message); state.InFlight.Remove(header.MessageId); }
            if (state.InFlight.Count >= options.MaxInFlight) { NetStats.TooManyInFlight++; return; }
            message = StartInbound(s, state, header, now);
            if (!header.Last) state.InFlight[header.MessageId] = message;
        }
        else if (message is null) { NetStats.Orphans++; return; }
        else if (message.Channel != header.Channel) { message.Aborted = true; ReleaseInbound(state, message); NetStats.Malformed++; }
        message.LastMs = now;

        if (!message.Aborted)
        {
            message.Total += data.Length;
            if (message.Total > options.MaxMessage) { message.Aborted = true; ReleaseInbound(state, message); NetStats.TooLarge++; }
        }
        if (!message.Aborted)
        {
            if (message.Target == NetOpsWire.TargetServer) AppendInbound(state, message, data);
            else RelayNet(s, message, m.Payload);
        }
        if (!header.Last) return;
        state.InFlight.Remove(header.MessageId);
        if (!message.Aborted && message.Target == NetOpsWire.TargetServer && message.Buffer is not null)
            if (_netChannels.TryGetValue(message.Channel, out var channel)) DeliverNet(s, channel, message.Json, message.Buffer.AsSpan(0, message.Length).ToArray());
        ReleaseInbound(state, message);
    }

    private NetOpsSession.Inbound StartInbound(PlayerSession s, NetOpsSession state, in NetOpsWire.DataHeader header, long now)
    {
        var options = _config.NetOps;
        var message = new NetOpsSession.Inbound { Channel = header.Channel, Target = header.Target, Json = header.Json, LastMs = now };
        if (!state.Channels.Contains(header.Channel))
        {
            if (state.Channels.Count >= options.MaxChannelsPerClient) { NetStats.TooManyChannels++; message.Aborted = true; return message; }
            state.Channels.Add(header.Channel);
        }
        _netChannels.TryGetValue(header.Channel, out var channel);
        if (header.Target == NetOpsWire.TargetServer)
        {
            if (channel is null) { NetStats.UnknownChannel++; message.Aborted = true; }
            return message;
        }
        if (channel is { ServerOnly: true }) { NetStats.ServerOnlyRefused++; message.Aborted = true; return message; }
        uint recipients = 0;
        if (header.Target == NetOpsWire.TargetHost) { if (HostSession() is { } host && host != s && CapablePlayer(host.Slot) is not null) recipients = 1u << (host.Slot - 1); }
        else if (header.Target == NetOpsWire.TargetAll)
        {
            for (var slot = 1; slot <= MaxSlots; slot++) if (slot != s.Slot && CapablePlayer(slot) is not null) recipients |= 1u << (slot - 1);
        }
        else if (NetOpsWire.IsSlotTarget(header.Target))
        {
            var slot = header.Target - NetOpsWire.TargetSlotBase;
            if (slot != s.Slot && CapablePlayer(slot) is not null) recipients = 1u << (slot - 1);
        }
        else { NetStats.Malformed++; message.Aborted = true; return message; }
        if (recipients == 0) { NetStats.NoRecipient++; message.Aborted = true; return message; }
        message.Recipients = recipients;
        return message;
    }

    private void AppendInbound(NetOpsSession state, NetOpsSession.Inbound message, ReadOnlySpan<byte> data)
    {
        var options = _config.NetOps;
        if (state.ReassemblyBytes + data.Length > options.ReassemblyPerSession || NetReassemblyTotal() + data.Length > options.ReassemblyTotal)
        {
            message.Aborted = true; ReleaseInbound(state, message); NetStats.ReassemblyLimit++;
            return;
        }
        var needed = message.Length + data.Length;
        if (message.Buffer is null) message.Buffer = new byte[Math.Max(needed, 256)];
        else if (message.Buffer.Length < needed) Array.Resize(ref message.Buffer, Math.Max(needed, message.Buffer.Length * 2));
        data.CopyTo(message.Buffer.AsSpan(message.Length));
        message.Length += data.Length;
        state.ReassemblyBytes += data.Length;
    }

    /// <summary>Forwards one fragment unchanged; the real sender goes into the header's origin, the payload's claims are not read.</summary>
    private void RelayNet(PlayerSession from, NetOpsSession.Inbound message, ReadOnlySpan<byte> payload)
    {
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            if ((message.Recipients & (1u << (slot - 1))) == 0 || CapablePlayer(slot) is not { } target) continue;
            SendMessage(target, _config.NetOps.Data, 0, from.Slot, payload);
            NetStats.Relayed++;
        }
    }

    private void DeliverNet(PlayerSession s, NetChannel channel, bool json, byte[] data)
    {
        NetStats.Delivered++;
        NetMessage?.Invoke(s, channel, json, data);
        if (_scripts is null || !_scripts.HasNetHandler(channel.Id)) return;
        try
        {
            if (!_scripts.FireNet(channel.Id, ScriptPlayerOf(s), json, data)) NetStats.Malformed++;
            ExecuteScriptKicks();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { ScriptFault($"net channel {channel.Name}", e); }
    }

    // ---- sending ----------------------------------------------------------------------------------------------------

    /// <summary>Sends one message to a capable player, in fragments. Returns the number of wire messages (0 with <paramref name="error"/>).</summary>
    public int SendNet(PlayerSession to, uint channel, bool json, ReadOnlySpan<byte> data, out string? error)
    {
        error = null;
        var options = _config.NetOps;
        if (!options.Enabled) { error = "net ops are off"; return 0; }
        if (to.State == SessionState.Closed || !_netSessions.TryGetValue(to, out var state) || !(state.Capable || state.ChatCapable)) { error = "the player has no custom net support"; return 0; }
        if (!state.Capable)       // not (yet) on the custom message types: the chat carrier
        {
            if (!_netChannels.TryGetValue(channel, out var named)) { error = "the channel is not registered"; return 0; }
            if (!QueueNetChat(to, json ? NetChatWire.KindJson : NetChatWire.KindBytes, named.Name, data, out error)) return 0;
            NetStats.Sent++;
            return 1;
        }
        if (data.Length > options.MaxMessage) { error = $"message larger than {options.MaxMessage:N0} bytes"; return 0; }
        var id = ++state.NextId;
        var target = NetOpsWire.SlotTarget(to.Slot);
        var fragment = Math.Min(options.MaxFragment, 4096);
        var sent = 0;
        var offset = 0;
        do
        {
            var take = Math.Min(fragment, data.Length - offset);
            var flags = (byte)((offset == 0 ? NetOpsWire.FlagFirst : 0) | (offset + take >= data.Length ? NetOpsWire.FlagLast : 0) | (json ? NetOpsWire.FlagJson : 0));
            var length = NetOpsWire.WriteData(_netFrame, new(channel, flags, target, id), data.Slice(offset, take));
            SendMessage(to, options.Data, 0, 0, _netFrame.AsSpan(0, length));
            offset += take; sent++;
        } while (offset < data.Length);
        NetStats.Sent += sent;
        return sent;
    }

    /// <summary>Sends one message to the player in <paramref name="slot"/> (false with <paramref name="error"/> when it cannot get it).</summary>
    public bool SendNetToSlot(int slot, uint channel, bool json, ReadOnlySpan<byte> data, out string? error)
    {
        error = null;
        if (slot is < 1 or > MaxSlots || _slots[slot] is not { } player) { error = "no such player"; return false; }
        return SendNet(player, channel, json, data, out error) > 0;
    }

    /// <summary>Sends the message to every capable player (the targets of a message for 'all'); returns how many players got it.</summary>
    public int SendNetToAll(uint channel, bool json, ReadOnlySpan<byte> data, out string? error)
    {
        error = null;
        var players = 0;
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (NetReachable(slot) is { } target && SendNet(target, channel, json, data, out error) > 0) players++;
        return players;
    }

    // ---- IScriptServer: the net table of the scripts ---------------------------------------------------------------------------

    uint IScriptServer.NetRegister(string name, bool serverOnly) => RegisterNetChannel(name, serverOnly).Id;
    void IScriptServer.NetReset() => ClearNetChannels();
    bool IScriptServer.NetSupports(int slot) => NetSupports(slot);
    string? IScriptServer.NetTransport(int slot) => NetTransport(slot);
    int IScriptServer.NetHandlerBudget => _config.NetOps.HandlerBudget;

    int IScriptServer.NetSend(int slot, uint channel, bool json, byte[] data, out string? error)
    {
        error = null;
        if (slot == 0)
        {
            var players = SendNetToAll(channel, json, data, out error);
            if (players == 0 && error is null) error = "no player can use custom net messages";
            return players;
        }
        return SendNetToSlot(slot, channel, json, data, out error) ? 1 : 0;
    }

    // ---- housekeeping -----------------------------------------------------------------------------------------------

    /// <summary>Every tick: the peer list when it changed; twice a second: unfinished messages that stalled.</summary>
    private void PumpNetOps(long now)
    {
        var options = _config.NetOps;
        if (!options.Enabled) return;
        var mask = 0;
        for (var slot = 1; slot <= MaxSlots; slot++) if (CapablePlayer(slot) is not null) mask |= 1 << (slot - 1);
        Span<byte> peers = stackalloc byte[NetOpsWire.PeersBytes];
        if (mask != 0)
            for (var slot = 1; slot <= MaxSlots; slot++)
                if (CapablePlayer(slot) is { } target && _netSessions.TryGetValue(target, out var state) && state.SentPeers != mask)
                {
                    state.SentPeers = mask;
                    NetOpsWire.WritePeers(peers, (ushort)mask);
                    SendMessage(target, options.Control, 0, 0, peers);
                }
        PumpNetChat(now);
        if (now < _nextNetSweepMs) return;
        _nextNetSweepMs = now + 500;
        var limit = options.FragmentTimeoutSeconds * 1000L;
        foreach (var session in _sessions)
        {
            if (!_netSessions.TryGetValue(session, out var state) || state.InFlight.Count == 0) continue;
            foreach (var (id, message) in state.InFlight.ToArray())
            {
                if (session.State != SessionState.Closed && now - message.LastMs <= limit) continue;
                if (session.State != SessionState.Closed) NetStats.TimedOut++;
                ReleaseInbound(state, message);
                state.InFlight.Remove(id);
            }
        }
    }
}
