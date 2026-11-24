using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Lobby;

public enum LobbyEventKind { Online, Offline, Kick, Shutdown, Character, Text }

/// <summary>Something the lobby told the game server; consumed by the game loop.</summary>
public readonly record struct LobbyEvent(LobbyEventKind Kind, int PermId = 0, ushort Block = 0, byte[]? Data = null, string? Text = null, IPAddress? Address = null);

/// <summary>
/// Registers the game with a Sacred lobby (Open/Closed Internet) the way GameServer.exe's lobby module does
/// (<c>FUN_0048b880</c> and its vtable at 0x533E6C): TinCat logon, game record 12, updates 13, player announcements 45/46 and
/// logout 14. It runs on its own thread with its own socket, so the game loop only enqueues commands and reads events.
/// Lobby traffic is rare (a few messages per minute), so it may allocate; the game and packet loops stay allocation free.
/// </summary>
public sealed class LobbyClient : IDisposable
{
    private const uint LogMeOff = 4;
    private const int KeepAliveMs = 15_000, MaxBackoffMs = 60_000;
    private readonly string _host;
    private readonly int _port;
    private readonly Thread _thread;
    private readonly ConcurrentQueue<byte[]> _outbox = new();
    private readonly object _gate = new();
    private LobbyProtocol.GameRecord _game, _sentGame;
    private bool _gameChanged;
    private volatile bool _stop;
    private volatile bool _online;
    private Socket? _socket;

    public ConcurrentQueue<LobbyEvent> Events { get; } = new();
    /// <summary>True between the lobby's answer to message 12 and the loss of the connection (<c>FUN_0048d100</c>).</summary>
    public bool IsOnline => _online;
    public IPAddress? PublicAddress { get; private set; }

    public LobbyClient(string host, int port, in LobbyProtocol.GameRecord initial)
    {
        _host = host; _port = port; _game = initial;
        _thread = new Thread(Run) { IsBackground = true, Name = "lobby" };
    }

    public void Start() => _thread.Start();

    /// <summary>Called by the game loop when players, max players, flags or the name changed; message 13 follows.</summary>
    public void UpdateGame(in LobbyProtocol.GameRecord game)
    {
        lock (_gate) { _game = game with { LanAddress = _game.LanAddress, PublicAddress = _game.PublicAddress }; _gameChanged = true; }
    }

    /// <summary>Message 45 (<c>FUN_0048d130</c>): a player with a lobby identity joined; <paramref name="first"/> false re-announces.</summary>
    public void PlayerJoined(int permId, ushort block, bool first)
    {
        var frame = Build(LobbyProtocol.UserJoinedServer, 8, out var body);
        LobbyProtocol.WriteUserJoined(body, permId, block, first);
        _outbox.Enqueue(frame);
    }

    /// <summary>Message 19: asks sacred-lobby for a Closed Net character block (answered with PublicData 11, which the game loop
    /// receives as a <see cref="LobbyEventKind.Character"/> event).</summary>
    public void RequestCharacter(int permId, ushort block)
    {
        var frame = Build(LobbyProtocol.ServerDataRequest, 14, out var body);
        LobbyProtocol.WriteDataRequest(body, permId, block, 0, LobbyProtocol.MaxBlock);   // GameServer.exe asks for offset 0, length 0x20000
        _outbox.Enqueue(frame);
    }

    /// <summary>Message 18: stores a changed character block (preview + sizes + zlib save) at the lobby. At most
    /// <see cref="LobbyProtocol.MaxBlock"/> bytes: sacred-lobby refuses partial uploads.</summary>
    public bool UploadCharacter(int permId, ushort block, ReadOnlySpan<byte> data)
    {
        if (data.Length is <= 0x230 or > LobbyProtocol.MaxBlock) return false;
        var frame = Build(LobbyProtocol.ServerDataUpload, 18 + data.Length, out var body);
        LobbyProtocol.WriteDataUpload(body, permId, block, data);
        _outbox.Enqueue(frame);
        return true;
    }

    /// <summary>Message 46 (<c>FUN_0048d1c0</c>): the player left.</summary>
    public void PlayerLeft(int permId)
    {
        var frame = Build(LobbyProtocol.UserLeftServer, 4, out var body);
        BinaryPrimitives.WriteInt32LittleEndian(body, permId);
        _outbox.Enqueue(frame);
    }

    private void Run()
    {
        var backoff = 2_000;
        while (!_stop)
        {
            try
            {
                Session();
                backoff = 2_000;
            }
            catch (Exception e) when (e is SocketException or IOException or InvalidDataException or ObjectDisposedException)
            {
                if (_stop) break;
                Log.Info($"LOBBY: {e.Message}");
            }
            if (_online) { _online = false; Events.Enqueue(new LobbyEvent(LobbyEventKind.Offline)); }
            _socket?.Dispose(); _socket = null;
            while (_outbox.TryDequeue(out _)) { } // announcements are repeated after the next login
            for (var waited = 0; waited < backoff && !_stop; waited += 100) Thread.Sleep(100);
            backoff = Math.Min(backoff * 2, MaxBackoffMs);
        }
    }

    private void Session()
    {
        Log.Info($"LOBBY: connecting to {_host}:{_port}");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _socket = socket;
        socket.Connect(_host, _port);
        var reader = new FrameReader(socket);

        // TinCat logon (the game logs on as user/passwor, the lobby assigns the id).
        var logon = new byte[TinCatWire.HeaderBytes + TinCatWire.LoginPayloadBytes];
        var p = logon.AsSpan(TinCatWire.HeaderBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(p, TinCatWire.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(p[4..], TinCatWire.UnassignedId);
        "user"u8.CopyTo(p[8..]);
        "passwor"u8.CopyTo(p[40..]);
        BinaryPrimitives.WriteUInt32LittleEndian(p[48..], 1);
        TinCatWire.SealFrame(logon, TinCatWire.UnassignedId, TinCatWire.ServerId, TinCatWire.LoginRequest, TinCatWire.LoginPayloadBytes);
        socket.Send(logon);
        uint id = 0;
        var deadline = Environment.TickCount64 + 15_000;
        while (id == 0)
        {
            if (Environment.TickCount64 > deadline) throw new IOException("no TinCat logon answer");
            if (!reader.TryRead(1000, out var type, out var payload)) continue;
            if (type == TinCatWire.LoginAccepted && payload.Length == TinCatWire.LoginPayloadBytes)
                id = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4));
        }
        _id = id;

        LobbyProtocol.GameRecord game;
        lock (_gate)
        {
            var local = (socket.LocalEndPoint as IPEndPoint)?.Address;
            if (local is { IsIPv4MappedToIPv6: true }) local = local.MapToIPv4();
            _game = _game with { LanAddress = local is { AddressFamily: AddressFamily.InterNetwork } ? ToUInt(local) : 0 };
            game = _game; _sentGame = game; _gameChanged = false;
        }
        var login = Build(LobbyProtocol.ServerLogin, LobbyProtocol.GameRecordBytes, out var body);
        LobbyProtocol.WriteLogin(body, game);
        Send(socket, login);
        Log.Info($"LOBBY: logging in game '{game.Name}' (channel {game.Channel})");

        long lastSend = Environment.TickCount64, lastReceive = lastSend;
        while (!_stop)
        {
            if (reader.TryRead(50, out var type, out var payload)) { lastReceive = Environment.TickCount64; OnFrame(type, payload); }
            // No silence time-out: the lobby only answers our messages, and GameServer.exe idles on the connection indefinitely
            // (checked against sacred-lobby for 100+ s). A dead lobby shows as a socket error (TCP keep-alive below).
            while (_online && _outbox.TryDequeue(out var frame)) { Send(socket, frame); lastSend = Environment.TickCount64; }
            byte[]? update = null;
            lock (_gate)
            {
                if (_online && _gameChanged)
                {
                    _gameChanged = false;
                    if (_game != _sentGame)
                    {
                        _sentGame = _game;
                        update = Build(LobbyProtocol.ServerUpdate, LobbyProtocol.GameRecordBytes, out var ub);
                        LobbyProtocol.WriteUpdate(ub, _game);
                    }
                }
            }
            if (update is not null) { Send(socket, update); lastSend = Environment.TickCount64; }
            if (Environment.TickCount64 - lastSend >= KeepAliveMs)
            {
                var alive = new byte[TinCatWire.HeaderBytes + 2];
                Send(socket, alive, TinCatWire.StayingAlive); lastSend = Environment.TickCount64;
            }
        }
        // Orderly shutdown: message 14 (FUN_0048ca70), then TinCat log off.
        if (_online)
        {
            Send(socket, Build(LobbyProtocol.ServerLogout, LobbyProtocol.GameRecordBytes, out _));
            var off = new byte[TinCatWire.HeaderBytes];
            Send(socket, off, LogMeOff);
        }
    }

    private uint _id;

    private void OnFrame(uint type, byte[] payload)
    {
        if (type != TinCatWire.CustomData) return; // keep-alives, time alignment
        if (payload.Length < LobbyProtocol.PrefixBytes || BinaryPrimitives.ReadUInt16LittleEndian(payload) != LobbyProtocol.Module) return;
        var message = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2));
        var body = payload.AsSpan(LobbyProtocol.PrefixBytes);
        switch (message)
        {
            case LobbyProtocol.ServerLoginResult:                    // 38: [public IP] (internal 0x25, "Internet IP: %s.")
            case LobbyProtocol.ServerLogin when body.Length == 4:     // sacred-lobby/kryotek answer 12 with the 4-byte address
                if (body.Length < 4) return;
                PublicAddress = new IPAddress(body[..4]);
                Log.Info($"LOBBY: Internet IP: {PublicAddress}.");
                if (!_online) { _online = true; Events.Enqueue(new LobbyEvent(LobbyEventKind.Online, Address: PublicAddress)); }
                return;
            case LobbyProtocol.Result:                               // 15: result code, request type
                if (body.Length < 8) return;
                var code = BinaryPrimitives.ReadInt32LittleEndian(body);
                var last = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                if (code != 0 && code != 12) Log.Info($"LOBBY: result={code}, last={last}.");
                if (last == LobbyProtocol.ServerLogin && code == 0 && !_online)
                { _online = true; Events.Enqueue(new LobbyEvent(LobbyEventKind.Online)); }
                if (last == LobbyProtocol.ServerLogin && code != 0) throw new IOException($"lobby refused the game (result {code})");
                return;
            case LobbyProtocol.UserKicked:                           // 47: [perm id][reason][text] -> internal 0x34
                if (body.Length >= 4) Events.Enqueue(new LobbyEvent(LobbyEventKind.Kick, BinaryPrimitives.ReadInt32LittleEndian(body)));
                return;
            case LobbyProtocol.ClientImportant: case LobbyProtocol.ServerImportant: // 48/49: [popup u8][text 256][to]
                if (body.Length > 1) Events.Enqueue(new LobbyEvent(LobbyEventKind.Text, Text: LobbyProtocol.Text(body[1..Math.Min(body.Length, 257)])));
                return;
            case LobbyProtocol.ServerShutdown:                       // 50 -> internal 0x3E
                Events.Enqueue(new LobbyEvent(LobbyEventKind.Shutdown, Text: LobbyProtocol.Text(body)));
                return;
            case LobbyProtocol.PublicData:                           // 11: [perm][block u16][size][offset][length][data] -> internal 0x29
                if (body.Length < 18) return;
                var perm = BinaryPrimitives.ReadInt32LittleEndian(body);
                var block = BinaryPrimitives.ReadUInt16LittleEndian(body[4..]);
                var length = BinaryPrimitives.ReadInt32LittleEndian(body[14..]);
                var available = body.Length - 18;
                if (length <= 0 || length > available) length = available;
                Events.Enqueue(new LobbyEvent(LobbyEventKind.Character, perm, block, body.Slice(18, length).ToArray()));
                return;
        }
    }

    private byte[] Build(ushort type, int bodyLength, out Span<byte> body)
    {
        var frame = new byte[TinCatWire.HeaderBytes + LobbyProtocol.PrefixBytes + bodyLength];
        LobbyProtocol.WritePrefix(frame.AsSpan(TinCatWire.HeaderBytes), type, bodyLength);
        body = frame.AsSpan(TinCatWire.HeaderBytes + LobbyProtocol.PrefixBytes);
        return frame;
    }

    private void Send(Socket socket, byte[] frame, uint type = TinCatWire.CustomData)
    {
        TinCatWire.SealFrame(frame, _id, TinCatWire.ServerId, type, frame.Length - TinCatWire.HeaderBytes);
        var sent = 0;
        while (sent < frame.Length) sent += socket.Send(frame, sent, frame.Length - sent, SocketFlags.None);
    }

    private static uint ToUInt(IPAddress address)
    {
        Span<byte> b = stackalloc byte[4];
        address.TryWriteBytes(b, out _);
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive && !_thread.Join(3000)) _socket?.Dispose();
        _socket?.Dispose();
    }

    /// <summary>Blocking TinCat frame reader with a poll timeout (lobby thread only).</summary>
    private sealed class FrameReader(Socket socket)
    {
        private readonly byte[] _header = new byte[TinCatWire.HeaderBytes];

        public bool TryRead(int timeoutMs, out uint type, out byte[] payload)
        {
            type = 0; payload = [];
            if (!socket.Poll(timeoutMs * 1000, SelectMode.SelectRead)) return false;
            Fill(_header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(_header) != TinCatWire.Magic) throw new InvalidDataException("bad TinCat magic from lobby");
            type = BinaryPrimitives.ReadUInt32LittleEndian(_header.AsSpan(12));
            var length = BinaryPrimitives.ReadInt32LittleEndian(_header.AsSpan(20));
            if (length < 0 || length > 0x30000) throw new InvalidDataException($"lobby frame of {length} bytes");
            payload = new byte[length];
            Fill(payload);
            if (TinCatCrc.Compute(payload) != BinaryPrimitives.ReadUInt32LittleEndian(_header.AsSpan(24))) throw new InvalidDataException("lobby frame CRC mismatch");
            return true;
        }

        private void Fill(byte[] buffer)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = socket.Receive(buffer, read, buffer.Length - read, SocketFlags.None);
                if (n == 0) throw new IOException("lobby closed the connection");
                read += n;
            }
        }
    }
}
