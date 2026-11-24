using System.Buffers;
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
/// logout 14. One async loop (no thread of its own) owns the socket: connect, a receive loop and a send loop that sleeps on a signal
/// the game loop sets, so nothing polls. The game loop only enqueues commands and reads events. Everything ends on the cancellation
/// token: no connect or retry after Stop. Lobby traffic is rare (a few messages per minute); frames are pooled, the game and
/// packet loops stay allocation free.
/// </summary>
public sealed class LobbyClient : IDisposable
{
    private const uint LogMeOff = 4;
    private const int KeepAliveMs = 15_000, MaxBackoffMs = 60_000, LogonTimeoutMs = 15_000, MaxFrameBytes = 0x30000;
    private readonly string _host;
    private readonly int _port;
    private readonly CancellationTokenSource _cts;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly ConcurrentQueue<Frame> _outbox = new();
    private readonly object _gate = new();
    private LobbyProtocol.GameRecord _game, _sentGame;
    private bool _gameChanged;
    private volatile bool _online;
    private Task? _loop;
    private Socket? _socket;

    /// <summary>A pooled outgoing TinCat frame (header + prefix + body); returned to the pool after it was sent.</summary>
    private readonly record struct Frame(byte[] Buffer, int Length);

    public ConcurrentQueue<LobbyEvent> Events { get; } = new();
    /// <summary>True between the lobby's answer to message 12 and the loss of the connection (<c>FUN_0048d100</c>).</summary>
    public bool IsOnline => _online;

    /// <summary>Status callback (code, value) for the game that started the server: 4 up and running, 8 lobby unreachable, 9 login refused
    /// (GameServer.exe posts these to the game's window, see Platform/GameStatus.cs).</summary>
    public Action<int, int>? Status { get; init; }
    private bool _unreachableReported;
    private void Report(int code, int value) { try { Status?.Invoke(code, value); } catch (Exception e) when (e is not OutOfMemoryException) { } }
    public IPAddress? PublicAddress { get; private set; }

    /// <param name="stop">Cancelled when the server stops (the host's <c>StopToken</c>): the lobby ends at once.</param>
    public LobbyClient(string host, int port, in LobbyProtocol.GameRecord initial, CancellationToken stop = default)
    {
        _host = host; _port = port; _game = initial;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
    }

    public void Start() => _loop ??= Task.Run(RunAsync);

    private void Wake()
    {
        try { if (_signal.CurrentCount == 0) _signal.Release(); } catch (Exception e) when (e is SemaphoreFullException or ObjectDisposedException) { }
    }

    /// <summary>Called by the game loop when players, max players, flags or the name changed; message 13 follows.</summary>
    public void UpdateGame(in LobbyProtocol.GameRecord game)
    {
        lock (_gate) { _game = game with { LanAddress = _game.LanAddress, PublicAddress = _game.PublicAddress }; _gameChanged = true; }
        Wake();
    }

    private void Enqueue(Frame frame) { _outbox.Enqueue(frame); Wake(); }

    /// <summary>Message 45 (<c>FUN_0048d130</c>): a player with a lobby identity joined; <paramref name="first"/> false re-announces.</summary>
    public void PlayerJoined(int permId, ushort block, bool first)
    {
        var frame = Build(LobbyProtocol.UserJoinedServer, 8, out var body);
        LobbyProtocol.WriteUserJoined(body, permId, block, first);
        Enqueue(frame);
    }

    /// <summary>Message 19: asks sacred-lobby for a Closed Net character block (answered with PublicData 11, which the game loop
    /// receives as a <see cref="LobbyEventKind.Character"/> event).</summary>
    public void RequestCharacter(int permId, ushort block)
    {
        var frame = Build(LobbyProtocol.ServerDataRequest, 14, out var body);
        LobbyProtocol.WriteDataRequest(body, permId, block, 0, LobbyProtocol.MaxBlock);   // GameServer.exe asks for offset 0, length 0x20000
        Enqueue(frame);
    }

    /// <summary>Message 18: stores a changed character block (preview + sizes + zlib save) at the lobby. At most
    /// <see cref="LobbyProtocol.MaxBlock"/> bytes: sacred-lobby refuses partial uploads.</summary>
    public bool UploadCharacter(int permId, ushort block, ReadOnlySpan<byte> data)
    {
        if (data.Length is <= 0x230 or > LobbyProtocol.MaxBlock) return false;
        var frame = Build(LobbyProtocol.ServerDataUpload, 18 + data.Length, out var body);
        LobbyProtocol.WriteDataUpload(body, permId, block, data);
        Enqueue(frame);
        return true;
    }

    /// <summary>Message 46 (<c>FUN_0048d1c0</c>): the player left.</summary>
    public void PlayerLeft(int permId)
    {
        var frame = Build(LobbyProtocol.UserLeftServer, 4, out var body);
        BinaryPrimitives.WriteInt32LittleEndian(body, permId);
        Enqueue(frame);
    }

    private async Task RunAsync()
    {
        var token = _cts.Token;
        var backoff = 2_000;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await SessionAsync(token).ConfigureAwait(false);
                backoff = 2_000;
            }
            catch (Exception e) when (e is SocketException or IOException or InvalidDataException or ObjectDisposedException or OperationCanceledException)
            {
                if (token.IsCancellationRequested) break;
                Log.Info($"LOBBY: {e.Message}");
                if (e is SocketException && !_unreachableReported && !_online) { _unreachableReported = true; Report(8, 0); }
            }
            if (_online) { _online = false; Events.Enqueue(new LobbyEvent(LobbyEventKind.Offline)); }
            _socket?.Dispose(); _socket = null;
            while (_outbox.TryDequeue(out var dropped)) ArrayPool<byte>.Shared.Return(dropped.Buffer);   // announcements are repeated after the next login
            if (token.IsCancellationRequested) break;
            try { await Task.Delay(backoff, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            backoff = Math.Min(backoff * 2, MaxBackoffMs);
        }
    }

    private async Task SessionAsync(CancellationToken token)
    {
        Log.Info($"LOBBY: connecting to {_host}:{_port}");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _socket = socket;
        await socket.ConnectAsync(_host, _port, token).ConfigureAwait(false);
        var header = new byte[TinCatWire.HeaderBytes];

        // TinCat logon (the game logs on as user/passwor, the lobby assigns the id).
        var logon = new byte[TinCatWire.HeaderBytes + TinCatWire.LoginPayloadBytes];
        var p = logon.AsSpan(TinCatWire.HeaderBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(p, TinCatWire.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(p[4..], TinCatWire.UnassignedId);
        "user"u8.CopyTo(p[8..]);
        "passwor"u8.CopyTo(p[40..]);
        BinaryPrimitives.WriteUInt32LittleEndian(p[48..], 1);
        TinCatWire.SealFrame(logon, TinCatWire.UnassignedId, TinCatWire.ServerId, TinCatWire.LoginRequest, TinCatWire.LoginPayloadBytes);
        await socket.SendAsync(logon, SocketFlags.None, token).ConfigureAwait(false);
        uint id = 0;
        using (var logonLimit = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            logonLimit.CancelAfter(LogonTimeoutMs);
            try
            {
                while (id == 0)
                {
                    var (type, payload, length) = await ReadFrameAsync(socket, header, logonLimit.Token).ConfigureAwait(false);
                    try
                    {
                        if (type == TinCatWire.LoginAccepted && length == TinCatWire.LoginPayloadBytes)
                            id = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4));
                    }
                    finally { ArrayPool<byte>.Shared.Return(payload); }
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("no TinCat logon answer"); }
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
        await SendAsync(socket, login, token).ConfigureAwait(false);
        Log.Info($"LOBBY: logging in game '{game.Name}' (channel {game.Channel})");

        // Receive loop and send loop run side by side; the session ends with whichever fails first (or the token).
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var receive = ReceiveLoopAsync(socket, header, sessionCts.Token);
        long lastSend = Environment.TickCount64;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var wait = (int)Math.Clamp(KeepAliveMs - (Environment.TickCount64 - lastSend), 0, KeepAliveMs);
                var signalled = _signal.WaitAsync(wait, sessionCts.Token);
                if (await Task.WhenAny(signalled, receive).ConfigureAwait(false) == receive) { await receive.ConfigureAwait(false); break; }
                await signalled.ConfigureAwait(false);
                // No silence time-out: the lobby only answers our messages, and GameServer.exe idles on the connection indefinitely
                // (checked against sacred-lobby for 100+ s). A dead lobby shows as a socket error (TCP keep-alive).
                while (_online && _outbox.TryDequeue(out var frame)) { await SendAsync(socket, frame, token).ConfigureAwait(false); lastSend = Environment.TickCount64; }
                Frame update = default;
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
                if (update.Buffer is not null) { await SendAsync(socket, update, token).ConfigureAwait(false); lastSend = Environment.TickCount64; }
                if (Environment.TickCount64 - lastSend >= KeepAliveMs)
                {
                    await SendAsync(socket, Build(0, 2, out _, raw: true), token, TinCatWire.StayingAlive).ConfigureAwait(false);
                    lastSend = Environment.TickCount64;
                }
            }
        }
        finally
        {
            sessionCts.Cancel();
            try { await receive.ConfigureAwait(false); } catch (Exception e) when (e is SocketException or IOException or InvalidDataException or ObjectDisposedException or OperationCanceledException) { }
        }
        // Stop: best effort, one non-blocking attempt at message 14 and the log off, never waited on.
        if (_online && token.IsCancellationRequested) TryGoodbye(socket);
    }

    private async Task ReceiveLoopAsync(Socket socket, byte[] header, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var (type, payload, length) = await ReadFrameAsync(socket, header, token).ConfigureAwait(false);
            try { OnFrame(type, payload.AsSpan(0, length)); }
            finally { ArrayPool<byte>.Shared.Return(payload); }
        }
    }

    /// <summary>One TinCat frame: 28-byte header into <paramref name="header"/>, payload into a rented buffer (the caller returns it).</summary>
    private static async Task<(uint Type, byte[] Payload, int Length)> ReadFrameAsync(Socket socket, byte[] header, CancellationToken token)
    {
        await ReadExactAsync(socket, header, TinCatWire.HeaderBytes, token).ConfigureAwait(false);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != TinCatWire.Magic) throw new InvalidDataException("bad TinCat magic from lobby");
        var type = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20));
        if (length < 0 || length > MaxFrameBytes) throw new InvalidDataException($"lobby frame of {length} bytes");
        var payload = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
        try
        {
            await ReadExactAsync(socket, payload, length, token).ConfigureAwait(false);
            if (TinCatCrc.Compute(payload.AsSpan(0, length)) != BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24))) throw new InvalidDataException("lobby frame CRC mismatch");
        }
        catch { ArrayPool<byte>.Shared.Return(payload); throw; }
        return (type, payload, length);
    }

    private static async Task ReadExactAsync(Socket socket, byte[] buffer, int count, CancellationToken token)
    {
        var read = 0;
        while (read < count)
        {
            var n = await socket.ReceiveAsync(buffer.AsMemory(read, count - read), SocketFlags.None, token).ConfigureAwait(false);
            if (n == 0) throw new IOException("lobby closed the connection");
            read += n;
        }
    }

    private uint _id;

    private void OnFrame(uint type, ReadOnlySpan<byte> payload)
    {
        if (type != TinCatWire.CustomData) return; // keep-alives, time alignment
        if (payload.Length < LobbyProtocol.PrefixBytes || BinaryPrimitives.ReadUInt16LittleEndian(payload) != LobbyProtocol.Module) return;
        var message = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
        var body = payload[LobbyProtocol.PrefixBytes..];
        switch (message)
        {
            case LobbyProtocol.ServerLoginResult:                    // 38: [public IP] (internal 0x25, "Internet IP: %s.")
            case LobbyProtocol.ServerLogin when body.Length == 4:     // sacred-lobby/kryotek answer 12 with the 4-byte address
                if (body.Length < 4) return;
                PublicAddress = new IPAddress(body[..4]);
                Log.Info($"LOBBY: Internet IP: {PublicAddress}.");
                if (!_online) { _online = true; Events.Enqueue(new LobbyEvent(LobbyEventKind.Online, Address: PublicAddress)); Report(4, 0); }
                return;
            case LobbyProtocol.Result:                               // 15: result code, request type
                if (body.Length < 8) return;
                var code = BinaryPrimitives.ReadInt32LittleEndian(body);
                var last = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                if (code != 0 && code != 12) Log.Info($"LOBBY: result={code}, last={last}.");
                if (last == LobbyProtocol.ServerLogin && code == 0 && !_online)
                { _online = true; Events.Enqueue(new LobbyEvent(LobbyEventKind.Online)); Report(4, 0); }
                if (last == LobbyProtocol.ServerLogin && code != 0) { Report(9, code); throw new IOException($"lobby refused the game (result {code})"); }
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

    /// <summary>A pooled frame with room for <paramref name="bodyLength"/> body bytes after the TinCat header and the lobby prefix
    /// (<paramref name="raw"/>: no lobby prefix, for TinCat's own keep-alive). The body is zeroed.</summary>
    private static Frame Build(ushort type, int bodyLength, out Span<byte> body, bool raw = false)
    {
        var prefix = raw ? 0 : LobbyProtocol.PrefixBytes;
        var total = TinCatWire.HeaderBytes + prefix + bodyLength;
        var buffer = ArrayPool<byte>.Shared.Rent(total);
        buffer.AsSpan(0, total).Clear();
        if (!raw) LobbyProtocol.WritePrefix(buffer.AsSpan(TinCatWire.HeaderBytes), type, bodyLength);
        body = buffer.AsSpan(TinCatWire.HeaderBytes + prefix, bodyLength);
        return new Frame(buffer, total);
    }

    private async Task SendAsync(Socket socket, Frame frame, CancellationToken token, uint type = TinCatWire.CustomData)
    {
        try
        {
            TinCatWire.SealFrame(frame.Buffer, _id, TinCatWire.ServerId, type, frame.Length - TinCatWire.HeaderBytes);
            await socket.SendAsync(frame.Buffer.AsMemory(0, frame.Length), SocketFlags.None, token).ConfigureAwait(false);
        }
        finally { ArrayPool<byte>.Shared.Return(frame.Buffer); }
    }

    /// <summary>Message 14 and the TinCat log off as one non-blocking attempt; nothing is retried or awaited.</summary>
    private void TryGoodbye(Socket socket)
    {
        try
        {
            if (!socket.Poll(0, SelectMode.SelectWrite)) return;
            var logout = Build(LobbyProtocol.ServerLogout, LobbyProtocol.GameRecordBytes, out _);
            try { TinCatWire.SealFrame(logout.Buffer, _id, TinCatWire.ServerId, TinCatWire.CustomData, logout.Length - TinCatWire.HeaderBytes); socket.Send(logout.Buffer.AsSpan(0, logout.Length), SocketFlags.None); }
            finally { ArrayPool<byte>.Shared.Return(logout.Buffer); }
            var off = new byte[TinCatWire.HeaderBytes];
            TinCatWire.SealFrame(off, _id, TinCatWire.ServerId, LogMeOff, 0);
            socket.Send(off, SocketFlags.None);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException) { }
    }

    private static uint ToUInt(IPAddress address)
    {
        Span<byte> b = stackalloc byte[4];
        address.TryWriteBytes(b, out _);
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    /// <summary>Stops the lobby at once (the host's stop token does the same when the client was created with it).</summary>
    public void RequestStop()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Cancels and waits at most 300 ms; the loop ends on the token.</summary>
    public void Dispose()
    {
        RequestStop();
        try { _loop?.Wait(300); } catch (AggregateException) { }
        _socket?.Dispose();
    }
}
