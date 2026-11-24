using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using Sacred.GameServer.Config;
using Sacred.GameServer.Net;
using Sacred.GameServer.Web;

namespace Sacred.GameServer.Session;

/// <summary>
/// Admin surface for the web UI (<see cref="IServerAdmin"/>). The game loop stays the only writer of game state:
/// <list type="bullet">
/// <item>reads (overview, players) come from a snapshot the loop refreshes every <see cref="SnapshotIntervalMs"/> into preallocated
/// arrays; the web thread copies out of it under a short lock,</item>
/// <item>chat is recorded into a fixed ring of preallocated character buffers (no allocation on the packet path),</item>
/// <item>commands arrive through a queue and run on the loop thread, between packet processing and the world tick.</item>
/// </list>
/// </summary>
public sealed partial class GameServerHost : IServerAdmin
{
    CancellationToken IServerAdmin.StopRequested => _stopCts.Token;

    private const int SnapshotIntervalMs = 250, ChatRingSize = 512, ChatTextChars = 128, ChatNameChars = 32, AdminQueueLimit = 256;

    private sealed class ChatSlot
    {
        public long Id, TimeMs;
        public ChatKind Kind;
        public int From, To;
        public int NameLength, TextLength;
        public readonly char[] Name = new char[ChatNameChars];
        public readonly char[] Text = new char[ChatTextChars];
    }

    private struct PlayerSnapshot
    {
        public bool Present;
        public byte Slot;
        public string Name, Address, Transport;
        public bool IsHost, InGame, Ready, PasswordAccepted;
        public uint LatencyMs;
        public long JoinedMs;
    }

    private readonly object _adminGate = new();
    private readonly ChatSlot[] _chat = CreateRing();
    private long _chatHead;
    private readonly PlayerSnapshot[] _playerSnapshot = new PlayerSnapshot[MaxSlots + 1];
    private readonly (PlayerSession? Owner, string Address, long JoinedMs)[] _addressCache = new (PlayerSession?, string, long)[MaxSlots + 1];
    private ServerOverview _overview = new("", 0, 0, 0, 0, 0, 0, false, false, false, false, 0, 0, 0, false, false, null, "remake");
    private long _nextSnapshotMs;
    private readonly ConcurrentQueue<AdminCommand> _adminCommands = new();
    private int _adminQueued;
    private readonly Dictionary<IPAddress, (string Reason, long CreatedMs)> _bans = new();
    private int _udpPort;

    private static ChatSlot[] CreateRing()
    {
        var ring = new ChatSlot[ChatRingSize];
        for (var i = 0; i < ring.Length; i++) ring[i] = new ChatSlot();
        return ring;
    }

    /// <summary>The UDP (Sacred Bild) port to show in the overview; set by the program after the listener is up.</summary>
    public void SetAdminUdpPort(int port) => _udpPort = port;

    // ---- recording (game loop thread) ---------------------------------------------------------------------------------

    /// <summary>Records a chat line from a UTF-16LE payload (the chat text of a 0x003/0x005 message) without allocating.</summary>
    private void RecordChat(ChatKind kind, PlayerSession? from, int to, ReadOnlySpan<byte> utf16Text)
    {
        lock (_adminGate)
        {
            var slot = NextChatSlot(kind, from?.Slot ?? 0, to);
            var count = 0;
            for (var i = 0; i + 1 < utf16Text.Length && count < ChatTextChars; i += 2)
            {
                var c = BinaryPrimitives.ReadUInt16LittleEndian(utf16Text[i..]);
                if (c == 0) break;
                slot.Text[count++] = char.IsControl((char)c) ? ' ' : (char)c;
            }
            slot.TextLength = count;
            SetChatName(slot, from);
        }
    }

    /// <summary>Records a line whose text already is a string (server messages, system events); these paths are not per packet.</summary>
    private void RecordChat(ChatKind kind, PlayerSession? from, int to, string text)
    {
        lock (_adminGate)
        {
            var slot = NextChatSlot(kind, from?.Slot ?? 0, to);
            slot.TextLength = Math.Min(text.Length, ChatTextChars);
            text.CopyTo(0, slot.Text, 0, slot.TextLength);
            SetChatName(slot, from);
        }
    }

    private ChatSlot NextChatSlot(ChatKind kind, int from, int to)
    {
        var id = _chatHead + 1;
        var slot = _chat[(int)((id - 1) % ChatRingSize)];
        slot.Id = id; slot.TimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); slot.Kind = kind; slot.From = from; slot.To = to;
        Volatile.Write(ref _chatHead, id);
        return slot;
    }

    private static void SetChatName(ChatSlot slot, PlayerSession? from)
    {
        var name = from?.Name ?? "";
        slot.NameLength = Math.Min(name.Length, ChatNameChars);
        name.CopyTo(0, slot.Name, 0, slot.NameLength);
    }

    // ---- loop hook ------------------------------------------------------------------------------------------------------

    /// <summary>Once per tick: run queued commands, refresh the snapshot four times a second.</summary>
    private void PumpAdmin(long now)
    {
        var ranCommand = false;
        while (_adminCommands.TryDequeue(out var command))
        {
            ranCommand = true;
            Interlocked.Decrement(ref _adminQueued);
            try { Execute(command); }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
            { Log.Info($"web admin: command {command.Kind} failed: {e.Message}"); }
        }
        // A command changes what the page shows: refresh the snapshot in this very tick instead of up to 250 ms later.
        if (now < _nextSnapshotMs && !ranCommand) return;
        _nextSnapshotMs = now + SnapshotIntervalMs;
        lock (_adminGate)
        {
            for (var slot = 1; slot <= MaxSlots; slot++)
            {
                ref var entry = ref _playerSnapshot[slot];
                if (_slots[slot] is not { } p || p.State < SessionState.SlotAssigned) { entry.Present = false; continue; }
                ref var cache = ref _addressCache[slot];
                if (!ReferenceEquals(cache.Owner, p)) cache = (p, p.Connection.RemoteEndPoint?.Address is { } remote ? Normalize(remote).ToString() : "?", now);
                entry = new PlayerSnapshot
                {
                    Present = true, Slot = (byte)slot, Name = p.Name, Address = cache.Address, Transport = p.Connection.Transport,
                    IsHost = p.IsHost, InGame = p.InGame, Ready = p.Ready, PasswordAccepted = p.PasswordAccepted, LatencyMs = p.LatencyMs, JoinedMs = cache.JoinedMs,
                };
            }
            _overview = new ServerOverview(
                _config.ServerName, Tick, now / 1000.0, TcpPort, _udpPort, PlayerCount, _maxPlayers, _locked, _paused, _config.Password is not null,
                _hostWorldAvailable, _gameFlags, (int)_config.Mode, _config.Level, _config.LobbyHost is not null, Lobby?.IsOnline == true, _config.LobbyHost, "remake");
        }
    }

    // ---- IServerAdmin (any thread) ----------------------------------------------------------------------------------------

    public ServerOverview Overview() { lock (_adminGate) return _overview; }

    public IReadOnlyList<PlayerInfo> Players()
    {
        var now = _clock.ElapsedMilliseconds;
        var list = new List<PlayerInfo>(MaxSlots);
        lock (_adminGate)
            foreach (var p in _playerSnapshot)
                if (p.Present)
                    list.Add(new PlayerInfo(p.Slot, p.Name, p.Address, p.Transport, p.IsHost, p.InGame, p.Ready, p.PasswordAccepted, p.LatencyMs, (now - p.JoinedMs) / 1000.0));
        return list;
    }

    public long ChatHead => Volatile.Read(ref _chatHead);

    public IReadOnlyList<ChatEntry> ChatSince(long afterId, int max)
    {
        var list = new List<ChatEntry>();
        lock (_adminGate)
        {
            var head = _chatHead;
            for (var id = Math.Max(afterId + 1, head - ChatRingSize + 1); id <= head && list.Count < max; id++)
            {
                var slot = _chat[(int)((id - 1) % ChatRingSize)];
                list.Add(new ChatEntry(slot.Id, slot.TimeMs, slot.Kind, slot.From, new string(slot.Name, 0, slot.NameLength), slot.To, new string(slot.Text, 0, slot.TextLength)));
            }
        }
        return list;
    }

    public IReadOnlyList<BanEntry> Bans()
    {
        lock (_adminGate) return _bans.Select(b => new BanEntry(b.Key.ToString(), b.Value.Reason, b.Value.CreatedMs)).OrderBy(b => b.CreatedUnixMs).ToList();
    }

    public bool Submit(in AdminCommand command)
    {
        if (command.Kind == AdminCommandKind.None || Interlocked.Increment(ref _adminQueued) > AdminQueueLimit)
        {
            if (command.Kind != AdminCommandKind.None) Interlocked.Decrement(ref _adminQueued);
            return false;
        }
        _adminCommands.Enqueue(command);
        return true;
    }

    // ---- commands (game loop thread) -----------------------------------------------------------------------------------------

    private void Execute(in AdminCommand c)
    {
        switch (c.Kind)
        {
            case AdminCommandKind.Broadcast when c.Text is { } text: AdminSay(null, text); break;
            case AdminCommandKind.Message when c.Text is { } text: AdminSay(PlayerAt(c.Slot), text); break;
            case AdminCommandKind.Kick when PlayerAt(c.Slot) is { } victim:
                Log.Info($"web admin: kicking #{victim.Slot} {victim.Name}.");
                RecordChat(ChatKind.System, null, -1, $"Kicked #{victim.Slot} {victim.Name}");
                RemovePlayer(victim, c.Value != 0 ? (uint)c.Value : ReasonLobbyKick, notifyVictim: true);
                break;
            case AdminCommandKind.Ban when PlayerAt(c.Slot) is { } victim:
                if (victim.Connection.RemoteEndPoint?.Address is { } address) AddBan(address, c.Text ?? "");
                Log.Info($"web admin: banning #{victim.Slot} {victim.Name}.");
                RecordChat(ChatKind.System, null, -1, $"Banned #{victim.Slot} {victim.Name}");
                RemovePlayer(victim, ReasonLobbyKick, notifyVictim: true);
                break;
            case AdminCommandKind.BanAddress when c.Text is { } spec:
                var bar = spec.IndexOf('|');
                if (IPAddress.TryParse(bar < 0 ? spec : spec[..bar], out var banned))
                {
                    AddBan(banned, bar < 0 ? "" : spec[(bar + 1)..]);
                    KickAddress(banned);
                }
                break;
            case AdminCommandKind.Unban when c.Text is { } spec && IPAddress.TryParse(spec, out var lifted):
                lock (_adminGate) _bans.Remove(Normalize(lifted));
                Log.Info($"web admin: ban of {lifted} lifted.");
                break;
            case AdminCommandKind.Lock:
                _locked = c.Value != 0;
                SendAll(_locked ? (ushort)0x01A : (ushort)0x019, 0, default);
                Log.Info($"Session is {(_locked ? "locked" : "unlocked")} (web admin).");
                break;
            case AdminCommandKind.Pause:
                var pause = c.Value != 0;
                if (_paused == pause) break;
                _paused = pause;
                SendAll(pause ? (ushort)0x135 : (ushort)0x134, 0, default);
                Log.Info(pause ? "Session paused (web admin)." : "Session continued (web admin).");
                break;
            case AdminCommandKind.SetMaxPlayers:
                var value = Math.Clamp(c.Value, 1, MaxSlots);
                if ((_gameFlags & 0x40010) != 0 && value > 4) value = 4;
                _maxPlayers = value;
                Span<byte> payload = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)value);
                SendAll(0x023, 0, payload);
                Log.Info($"Max. number of player is set to {value} (web admin).");
                break;
            case AdminCommandKind.SetPassword:
                _config.Password = string.IsNullOrEmpty(c.Text) ? null : c.Text;
                _gameFlags = _config.Password is null ? _gameFlags & ~0x2u : _gameFlags | 0x2u;
                Log.Info(_config.Password is null ? "Password removed (web admin)." : "Password set (web admin).");
                BroadcastGameFlags();
                break;
            case AdminCommandKind.SetName when c.Text is { } name:
                _config.ServerName = name;
                Log.Info($"Session name is now '{name}' (web admin).");
                break;
            case AdminCommandKind.SetMode:
                if (_hostWorldAvailable) { Log.Info("web admin: the game mode cannot change once the host world exists."); break; }
                _config.Mode = (PreconfigMode)Math.Clamp(c.Value, 0, 4);
                _config.Level = Math.Clamp(c.Slot, 0, 5);
                _gameFlags = (_gameFlags & ~ModeAndLevelBits) | (_config.GameFlags() & ModeAndLevelBits);
                BroadcastGameFlags();
                Log.Info($"Game mode {_config.Mode}, level {_config.Level} (web admin).");
                break;
            case AdminCommandKind.EndSession:
                Log.Info("web admin: session ended.");
                ShutDownSession();
                break;
            case AdminCommandKind.SetFlood when c.Text is { } settings: ApplyFloodSettings(settings); break;
            case AdminCommandKind.Shutdown:
                Log.Info("web admin: shutting down.");
                Stop();
                break;
        }
    }

    private const uint ModeAndLevelBits = 0x10 | 0x20 | 0x40 | 0x40000 | 0x100 | 0x200 | 0x400 | 0x800;

    private void BroadcastGameFlags()
    {
        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, _gameFlags);
        SendAll(0x01E, 0, payload);
    }

    private PlayerSession? PlayerAt(int slot) =>
        slot is >= 1 and <= MaxSlots && _slots[slot] is { } p && p.State >= SessionState.SlotAssigned ? p : null;

    /// <summary>Server chat: the same 0x005/0x003 layout as relayed player chat, with the server (slot 0) as the origin.</summary>
    private void AdminSay(PlayerSession? target, string text)
    {
        Span<byte> payload = stackalloc byte[ChatPayloadBytes];
        var bytes = System.Text.Encoding.Unicode.GetBytes(text.Length > ChatTextBytes / 2 ? text[..(ChatTextBytes / 2)] : text);
        BuildChat(payload, target?.Slot ?? 0, bytes);
        if (target is null) { SendAll(0x005, 0, payload); RecordChat(ChatKind.Broadcast, null, -1, text); }
        else { SendMessage(target, 0x003, 0, 0, payload); RecordChat(ChatKind.ServerMessage, null, target.Slot, text); }
    }

    private void AddBan(IPAddress address, string reason)
    {
        lock (_adminGate) _bans[Normalize(address)] = (reason, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private void KickAddress(IPAddress address)
    {
        var target = Normalize(address);
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { } p && p.Connection.RemoteEndPoint?.Address is { } a && Normalize(a).Equals(target))
                RemovePlayer(p, ReasonLobbyKick, notifyVictim: true);
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>Admission hook: true when the connecting address is banned.</summary>
    private bool IsBanned(IClientLink link)
    {
        if (link.RemoteEndPoint?.Address is not { } address) return false;
        lock (_adminGate) return _bans.Count > 0 && _bans.ContainsKey(Normalize(address));
    }
}
