using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Sacred.GameServer.Config;
using Sacred.GameServer.Lan;
using Sacred.GameServer.Net;
using Sacred.GameServer.Web;
using Sacred.GameServer.Wire;
using Sacred.Network.Admission;

namespace Sacred.GameServer.Session;

/// <summary>Handler of an opcode above the native range (see <see cref="GameServerHost.RegisterOpcode"/>).</summary>
public delegate void MessageHandler(PlayerSession player, in GoldMessageView message);

/// <summary>
/// The dedicated server: accepts clients, performs TinCat login and FIRSTCONTACT admission and runs the fixed-step game loop.
/// Everything below <see cref="Run"/> executes on one thread; I/O threads only enqueue verified frames. The loop and packet paths
/// use preallocated scratch buffers, pooled arrays and reusable socket args and do not allocate.
/// </summary>
public sealed partial class GameServerHost : IDisposable
{
    public const int MaxSlots = 16;
    private readonly GameServerConfig _config;
    private readonly AdmissionRules _rules;
    private readonly ConcurrentQueue<InboundFrame> _inbox = new();
    private readonly TcpAcceptor _acceptor;
    private readonly List<IClientListener> _listeners = new(2);   // _acceptor first, then extra transports (KCP, ...)
    private readonly PlayerSession?[] _slots = new PlayerSession?[MaxSlots + 1];
    private readonly List<PlayerSession> _sessions = new(64);
    private readonly List<(IClientLink Connection, long Due)> _retired = new(16);
    private readonly byte[] _batch = new byte[GoldWire.MaximumBatchBytes + 16];   // decode scratch
    private readonly byte[] _build = new byte[256 * 1024];                           // outgoing batch scratch
    private readonly byte[] _envelope = new byte[256 * 1024 + 64];                   // outgoing envelope scratch
    private readonly ZlibInflater _inflater = new();
    private readonly ZlibDeflater _deflater = new();
    private readonly byte[] _relay = new byte[16 * 1024];
    /// <summary>
    /// Claims an opcode above the native range (0x1D5) for an extension (e.g. the Gear envelope opcodes 0x1E0/0x1E1). Its handler
    /// runs for every message with that opcode after admission, before the native routing. Native opcodes cannot be claimed.
    /// </summary>
    public void RegisterOpcode(ushort opcode, MessageHandler handler)
    {
        if (opcode <= OpcodeTable.NativeLast) throw new ArgumentOutOfRangeException(nameof(opcode), "native opcodes cannot be claimed");
        _extensions[opcode] = handler;
    }

    private readonly bool[] _seenUnhandled = new bool[4096];
    private readonly Dictionary<ushort, MessageHandler> _extensions = new();
    private readonly LanAnnouncer? _lan;
    private readonly Simulation.HostClock _clock;
    private uint _nextTinCatId = 0x100;
    private long _nextLanMs;
    private volatile bool _stop;

    public long Tick { get; private set; }
    public int TcpPort => _acceptor.Port;
    public int PlayerCount { get; private set; }
    /// <summary>Hook for gameplay modules (world simulation) invoked once per tick after packets were processed.</summary>
    public Action<long>? OnTick;

    public GameServerHost(GameServerConfig config, AdmissionRules rules, LanAnnouncer? lan, Simulation.HostClock? clock = null)
    {
        _config = config; _rules = rules; _lan = lan; _clock = clock ?? new Simulation.HostClock();
        _acceptor = new TcpAcceptor(config.TcpPort, _inbox, config.BindAddress);
        _listeners.Add(_acceptor);
        InitializeSession();
        InitializeNetOps();
    }

    private readonly CancellationTokenSource _stopCts = new();

    /// <summary>Cancelled by <see cref="Stop"/>: every background part (lobby, web UI streams) ends on it at once.</summary>
    public CancellationToken StopToken => _stopCts.Token;

    public void Stop()
    {
        if (!_stop) StopRequestedMs = _clock.ElapsedMilliseconds;
        _stop = true; try { _stopCts.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Server clock when <see cref="Stop"/> was first called (shutdown timing in the log), -1 before.</summary>
    public long StopRequestedMs { get; private set; } = -1;
    /// <summary>The transports (TCP acceptor first); tests check their local end points.</summary>
    public IReadOnlyList<IClientListener> Listeners => _listeners;

    public long ClockMs => _clock.ElapsedMilliseconds;

    /// <summary>Adds another transport (for example reliable UDP); it feeds the same inbox, so sessions work the same on it.
    /// Call before <see cref="Start"/>.</summary>
    public IClientListener AddListener(Func<ConcurrentQueue<InboundFrame>, IClientListener> create)
    {
        var listener = create(_inbox);
        _listeners.Add(listener);
        return listener;
    }

    public void Start() { foreach (var listener in _listeners) listener.Start(); }

    public void Run()
    {
        Start();
        Log.Info($"--- Gameserver Init [Ping: {_config.ListenPort}, TCP: {TcpPort}, Name: {_config.ServerName}, Password: {(_config.Password is null ? "none" : "set")}] ---");
        long next = _clock.ElapsedMilliseconds;
        while (!_stop)
        {
            RunTick();
            next += _config.TickMilliseconds;
            var wait = next - _clock.ElapsedMilliseconds;
            if (wait > 1) Thread.Sleep((int)(wait - 1));
            else if (wait < -250) next = _clock.ElapsedMilliseconds; // fell behind: do not spiral
            while (_clock.ElapsedMilliseconds < next && !_stop) Thread.SpinWait(50);
        }
        EndSessionForPlayers();
        StopScripts();
        CloseCapture();
        foreach (var s in _sessions) s.Connection.Close();
        Log.Info("--- Gameserver Shutdown ---");
    }

    /// <summary>
    /// Stopping with players connected ends the session the way the original does (FUN_004d6c50: 0x009 0x68, 0x008, 0x157 to each
    /// player; in authoritative mode every hero is saved), then waits a bounded time (at most 1.5 s) for the frames to leave. Nothing else
    /// is waited for: no players, no wait.
    /// </summary>
    private void EndSessionForPlayers()
    {
        if (PlayerCount == 0) return;
        ShutDownSession();
        foreach (var s in _sessions) s.Connection.Flush();
        var until = _clock.ElapsedMilliseconds + 1500;
        var pending = true;
        while (pending && _clock.ElapsedMilliseconds < until)
        {
            pending = false;
            foreach (var s in _sessions) if (!s.Connection.IsClosed && s.Connection.PendingBytes > 0) { s.Connection.Flush(); pending = true; }
            if (pending) Thread.Sleep(5);
        }
        Thread.Sleep(100);                                   // the last async sends of the final flush
    }

    /// <summary>One loop iteration. Public so tests can single-step the server.</summary>
    public void RunTick()
    {
        var now = _clock.ElapsedMilliseconds;
        Tick++;
        foreach (var listener in _listeners)
            while (listener.Accepted.TryDequeue(out var accepted)) Session(accepted);
        while (_inbox.TryDequeue(out var frame))
        {
            try { Handle(frame, now); }
            finally { if (frame.Payload.Length > 0) ArrayPool<byte>.Shared.Return(frame.Payload); }
        }
        for (var i = _sessions.Count - 1; i >= 0; i--)
        {
            var s = _sessions[i];
            if (s.State == SessionState.Closed) continue;
            if (now - s.LastReceiveMs > 30_000) { Log.Info($"{s.Describe()} timed out"); s.Connection.Close(); continue; }
            if (s.State >= SessionState.LoggedIn && now - s.LastKeepAliveMs >= 5_000) { s.LastKeepAliveMs = now; SendControl(s, TinCatWire.StayingAlive, 2); }
        }
        PumpLobby();
        if (_config.Auth.Enabled) PumpAuthoritative(now);
        PumpEnvironment(now);
        PumpAdmin(now);
        OnTick?.Invoke(Tick);
        PumpScripts(now);
        PumpNetOps(now);
        PumpHeroes(now);
        PumpSectorView(now);
        PumpCapture(now);
        foreach (var s in _sessions) s.Connection.Flush();
        for (var i = _retired.Count - 1; i >= 0; i--)
            if (now >= _retired[i].Due) { _retired[i].Connection.ReleaseBuffers(); _retired.RemoveAt(i); }
        if (_lan is not null && now >= _nextLanMs) { _nextLanMs = now + 3000; _lan.Announce(PlayerCount); }
    }

    private PlayerSession Session(IClientLink connection)
    {
        if (connection.Session is PlayerSession existing) return existing;
        var created = new PlayerSession { Connection = connection, LastReceiveMs = _clock.ElapsedMilliseconds, LastKeepAliveMs = _clock.ElapsedMilliseconds };
        connection.Session = created;
        _sessions.Add(created);
        if (FloodOn && !AdmitConnection(connection)) connection.Close();
        return created;
    }

    private void Handle(in InboundFrame frame, long now)
    {
        var s = Session(frame.Connection);
        if (_config.CaptureDirectory is not null) CaptureFrame(s, frame, now);   // --capture (GameServerHost.Witness.cs)
        if (frame.Type == InboundFrame.Disconnected) { Retire(s, now); return; }
        if (s.State == SessionState.Closed) return;
        s.LastReceiveMs = now;
        switch (frame.Type)
        {
            case TinCatWire.LoginRequest:
                if (s.State != SessionState.Connected || frame.Length != TinCatWire.LoginPayloadBytes) { s.Connection.Close(); return; }
                AcceptLogin(s, frame.Span);
                break;
            case TinCatWire.CustomData when s.State >= SessionState.LoggedIn:
                HandleCustomData(s, frame.Span, now);
                break;
            case TinCatWire.StayingAlive or TinCatWire.TimeAlignment:
                break;
            case TinCatWire.Logoff:
                s.Connection.Close();
                break;
            default:
                s.Connection.Close(); // unsupported control frame or data before login
                break;
        }
    }

    private void AcceptLogin(PlayerSession s, ReadOnlySpan<byte> request)
    {
        s.TinCatId = _nextTinCatId++;
        s.State = SessionState.LoggedIn;
        var frame = s.Connection.BeginFrame(TinCatWire.HeaderBytes + TinCatWire.LoginPayloadBytes);
        var body = frame[TinCatWire.HeaderBytes..];
        request.CopyTo(body);
        BinaryPrimitives.WriteUInt32LittleEndian(body, TinCatWire.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(body[4..], s.TinCatId);
        TinCatWire.SealFrame(frame, TinCatWire.ServerId, s.TinCatId, TinCatWire.LoginAccepted, TinCatWire.LoginPayloadBytes);
        s.Connection.CommitFrame(frame.Length);
    }

    private void HandleCustomData(PlayerSession s, ReadOnlySpan<byte> payload, long now)
    {
        if (FloodOn && !FloodFrame(s, payload.Length, now)) return;
        if (payload.Length < 4 + GoldWire.EnvelopeHeaderBytes || BinaryPrimitives.ReadUInt16LittleEndian(payload) != TinCatWire.GameModule ||
            BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]) != 0) { Drop(s, "bad TinCat module"); return; }
        var envelope = payload[4..];
        if (!GoldWire.TryDecodeEnvelope(envelope, _batch, _inflater, out var length)) { Drop(s, "bad envelope"); return; }
        var reader = new GoldBatchReader(_batch.AsSpan(0, length));
        while (reader.TryRead(out var message))
        {
            if (s.State == SessionState.Closed) return;
            Dispatch(s, message);
        }
        if (reader.Failed) Drop(s, "bad message batch");
    }

    private void Drop(PlayerSession s, string why) { Log.Info($"{s.Describe()} dropped: {why}"); s.Connection.Close(); }

    // ---- message dispatch -------------------------------------------------------------------------------------------

    private void Dispatch(PlayerSession s, in GoldMessageView m)
    {
        if (Log.TraceEnabled) Log.Trace("RX", s.Slot, m.Opcode, m.Sender, m.Origin, m.Payload);
        if (FloodOn && !FloodMessage(s, m)) return;
        switch (m.Opcode)
        {
            case 0x10: OnFirstContact(s, m); return;
            case 0x12: OnPassword(s, m); return;
        }
        if (s.State < SessionState.SlotAssigned) { Drop(s, $"opcode 0x{m.Opcode:X} before admission"); return; }
        if (m.Sender != s.Slot) { Reject(s, RejectionReason.InvalidSender, 0); return; }
        // Original (FUN_004eb0a0): sequence must advance by one; a gap is reported as R_MESSAGE_ID_WARNING.
        s.RxSequence = m.Sequence;
        if (_config.Password is not null && !s.PasswordAccepted) return; // locked out until the password was accepted
        Route(s, m);
    }

    private void OnFirstContact(PlayerSession s, in GoldMessageView m)
    {
        if (s.State != SessionState.LoggedIn || m.Sender != 0) { Drop(s, "unexpected FIRSTCONTACT"); return; }
        var reason = _rules.Check(m.Payload, out var mask, out var identity);
        if (reason == 0 && identity != -1 && LobbyIdentityCheck)
            foreach (var other in _sessions)
                if (other != s && other.State >= SessionState.SlotAssigned && other.LobbyIdentity == identity) { reason = RejectionReason.DuplicateIdentity; break; }
        if (reason == 0 && IsBanned(s.Connection)) reason = RejectionReason.LobbyRefused;
        byte slot = 0;
        if (reason == 0)
        {
            for (byte i = 1; i <= MaxSlots; i++) if (_slots[i] is null) { slot = i; break; }
            if (slot == 0 || PlayerCount >= _maxPlayers || _locked) { Reject(s, RejectionReason.LobbyRefused, 0); return; }
        }
        if (reason != 0) { Reject(s, reason, mask); return; }

        s.Slot = slot; _slots[slot] = s; s.State = SessionState.SlotAssigned; PlayerCount++;
        s.LobbyIdentity = identity; s.Name = AdmissionRules.PlayerName(m.Payload) ?? "";
        s.CharacterBlock = m.Payload.Length > 6 ? m.Payload[6] : (ushort)0;
        s.PasswordAccepted = _config.Password is null;
        s.IsHost = HostSession() is null;
        Span<byte> reply = stackalloc byte[6];
        // Flags (FUN_004eb0a0): 1 = assigned client host, 4 = host world available (server flag 0x80).
        // 0x08 (0x4EB93D): the lobby is connected (FUN_0048d100), i.e. an Internet game; run 10: 09 for the Open Net host.
        reply[0] = (byte)((s.IsHost ? 1 : 0) | (!s.IsHost && _hostWorldAvailable ? 4 : 0) | (LobbyIdentityCheck ? 8 : 0));
        reply[1] = slot;
        SendMessage(s, 0x11, 0, 0, reply);
        // No 0x01E here: the original sends the flags only as the echo of the host's 0x01E (checked against a real session).
        Log.Info($"FIRSTCONTACT from {s.Connection.RemoteEndPoint}: '{s.Name}' admitted as #{slot}{(s.IsHost ? " (host)" : "")}");
        RecordChat(ChatKind.System, s, -1, "joined");
        AnnounceToLobby(s);
        RequestCharacter(s);
        // The original then queues two internal commands (0x4EBE2A, 0x4EBF08): 0x101 = roster for a session without password,
        // and 0x41 = first time probe (0x43). The client waits for its own roster record before it lists itself in the lobby.
        // Then the queued internal commands, in the original's order: 0x101 (roster when no password), 0x41 (time probe 0x43),
        // 0x153 (3000 refs for the host, 1000 for a joiner once the host world exists), 0x40E (world environment 0x00B) and
        // 0x151 (announce the newcomer to the others), as seen in a captured GameServer.exe session.
        SeedRosterEntry(s, m.Payload);
        if (s.PasswordAccepted) SendRoster(s);
        SendProbe(s);
        if (s.IsHost) GrantReferences(s, HostReferenceGrant);
        else if (_hostWorldAvailable) GrantReferences(s, JoinerReferenceGrant);
        SendEnvironment(s);
        Broadcast(0x151, s.Slot, default, except: s);
        OnPlayerJoined(s);
    }

    private void OnPassword(PlayerSession s, in GoldMessageView m)
    {
        if (s.State != SessionState.SlotAssigned || m.Sender != s.Slot) { Drop(s, "unexpected password message"); return; }
        if (_config.Password is not { } expected) { HostSetsPassword(s, m); return; }
        if (m.Payload.Length == 0 || (m.Payload.Length & 1) != 0) { Log.Info($"Empty password received from player #{s.Slot}!"); SendMessage(s, 0x13, 0, 0, default); return; }
        var ok = m.Payload.Length == expected.Length * 2;
        for (var i = 0; ok && i < expected.Length; i++) ok = BinaryPrimitives.ReadUInt16LittleEndian(m.Payload[(i * 2)..]) == expected[i];
        if (ok)
        {
            s.PasswordAccepted = true; s.State = SessionState.Playing;
            Log.Info($"Player #{s.Slot} has sent the correct password.");
            SendMessage(s, 0x14, 0, 0, default);
            SendRoster(s);
            return;
        }
        Log.Info($"#{s.Slot} has sent the wrong password!");
        SendMessage(s, 0x13, 0, 0, default);
        if (++s.PasswordTries >= _config.MaxPasswordTries) { Log.Info($"#{s.Slot} kicked: too many password tries"); s.Connection.Flush(); s.Connection.Close(); }
    }

    /// <summary>
    /// 0x12 while no password is set (case 0x4CA33E): only the host, before the game started, may set one. The original stores
    /// it, sets game flag 2, answers 0x14 and sends the roster. Everybody else is ignored.
    /// </summary>
    private void HostSetsPassword(PlayerSession s, in GoldMessageView m)
    {
        if (!s.IsHost || s.InGame || _hostWorldAvailable) return;
        if (m.Payload.Length == 0 || (m.Payload.Length & 1) != 0) { Log.Info($"Empty password received from player #{s.Slot}!"); return; }
        _config.Password = System.Text.Encoding.Unicode.GetString(m.Payload).TrimEnd('\0');
        _gameFlags |= 0x2;
        s.PasswordAccepted = true;
        SendMessage(s, 0x14, 0, 0, default);
        SendRoster(s);
        Log.Info($"Player #{s.Slot} has set a new password.");
    }

    private void Reject(PlayerSession s, uint reason, uint mask)
    {
        Span<byte> payload = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reason);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[4..], mask);
        SendMessage(s, 0x3F, 0, 0, payload);
        Log.Info($"FIRSTCONTACT from {s.Connection.RemoteEndPoint} rejected: reason {reason}, data mask 0x{mask:X8}");
    }

    private void Retire(PlayerSession s, long now)
    {
        if (s.State != SessionState.Closed)
        {
            Log.Info($"{s.Describe()} disconnected");
            RemovePlayer(s, ReasonLeft, notifyVictim: false);
        }
        if (!_sessions.Remove(s)) return;
        _retired.Add((s.Connection, now + 1000));
    }

    // ---- sending ----------------------------------------------------------------------------------------------------

    private void SendControl(PlayerSession s, uint type, int payloadBytes)
    {
        var frame = s.Connection.BeginFrame(TinCatWire.HeaderBytes + payloadBytes);
        frame.Slice(TinCatWire.HeaderBytes, payloadBytes).Clear();
        TinCatWire.SealFrame(frame, TinCatWire.ServerId, s.TinCatId, type, payloadBytes);
        s.Connection.CommitFrame(frame.Length);
    }

    /// <summary>Send one server message (sender 0 = GameServer, origin = originating player).</summary>
    public void SendMessage(PlayerSession s, ushort opcode, byte sender, byte origin, ReadOnlySpan<byte> payload)
    {
        if (s.State == SessionState.Closed || GoldWire.MessageHeaderBytes + payload.Length > _build.Length) return;
        var length = GoldWire.WriteMessage(_build, opcode, sender, origin, ++s.TxSequence, payload);
        SendBatch(s, _build.AsSpan(0, length));
    }

    /// <summary>Send a pre-built batch of messages.</summary>
    public void SendBatch(PlayerSession s, ReadOnlySpan<byte> batch)
    {
        if (Log.TraceEnabled)
        {
            var reader = new GoldBatchReader(batch);
            while (reader.TryRead(out var traced)) Log.Trace("TX", s.Slot, traced.Opcode, traced.Sender, traced.Origin, traced.Payload);
        }
        var envelopeLength = GoldWire.EncodeEnvelope(batch, _envelope, _deflater);
        if (envelopeLength < 0) return;
        var frame = s.Connection.BeginFrame(TinCatWire.HeaderBytes + 4 + envelopeLength);
        var body = frame[TinCatWire.HeaderBytes..];
        BinaryPrimitives.WriteUInt16LittleEndian(body, TinCatWire.GameModule);
        BinaryPrimitives.WriteUInt16LittleEndian(body[2..], 0);
        _envelope.AsSpan(0, envelopeLength).CopyTo(body[4..]);
        TinCatWire.SealFrame(frame, TinCatWire.ServerId, s.TinCatId, TinCatWire.CustomData, 4 + envelopeLength);
        s.Connection.CommitFrame(frame.Length);
    }

    /// <summary>Relay a message to every admitted player (server sender 0, retained origin).</summary>
    public void Broadcast(ushort opcode, byte origin, ReadOnlySpan<byte> payload, PlayerSession? except = null)
    {
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { } target && target != except && target.State >= SessionState.SlotAssigned)
                SendMessage(target, opcode, 0, origin, payload);
    }

    public void Dispose()
    {
        CloseCapture();
        foreach (var listener in _listeners) listener.Dispose();
        _lan?.Dispose();
    }
}
