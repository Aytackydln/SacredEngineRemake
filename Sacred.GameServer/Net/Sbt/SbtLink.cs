using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Net.Sbt;

/// <summary>
/// One Sacred Bild UDP session as an <see cref="IClientLink"/>: a KCP stream (conv = sessionId) carrying the same TinCat
/// frames the game sends over TCP. The game loop writes frames and flushes; the listener's thread feeds datagrams in, runs
/// the KCP clock and the keep-alive/time-out rules. KCP state is guarded by the link's own lock (game loop vs. UDP thread).
/// </summary>
public sealed class SbtLink : IClientLink
{
    private readonly SbtListener _owner;
    private readonly ConcurrentQueue<InboundFrame> _inbox;
    private readonly Kcp.Kcp _kcp;
    private readonly Lock _gate = new();
    private byte[] _fill = ArrayPool<byte>.Shared.Rent(16 * 1024);
    private int _fillLength;
    private byte[] _rx = ArrayPool<byte>.Shared.Rent(32 * 1024);
    private int _rxStart, _rxEnd;
    private int _closed;
    private SocketAddress _peer;

    public readonly uint Id, Nonce;
    public readonly ulong Token;
    internal long LastReceiveMs, LastSendMs;
    internal bool Removed;

    public string Transport => "kcp";
    public IPEndPoint? RemoteEndPoint { get; private set; }
    public IPEndPoint? LocalEndPoint => null;
    public object? Session { get; set; }
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public int PendingBytes => _fillLength;
    internal SocketAddress Peer => _peer;

    internal SbtLink(SbtListener owner, ConcurrentQueue<InboundFrame> inbox, uint id, ulong token, uint nonce, SocketAddress peer, long now)
    {
        _owner = owner; _inbox = inbox; Id = id; Token = token; Nonce = nonce;
        _peer = Copy(peer);
        RemoteEndPoint = EndPoint(peer);
        LastReceiveMs = LastSendMs = now;
        _kcp = new Kcp.Kcp(id, Output, SbtWire.HeaderBytes) { Stream = true, DeadLink = uint.MaxValue };
        _kcp.SetMtu(SbtWire.KcpMtu);
        _kcp.WndSize(128, 256);
        _kcp.NoDelay(1, 10, 1, 1);
        _kcp.Update((uint)now);
    }

    private static SocketAddress Copy(SocketAddress a)
    {
        var copy = new SocketAddress(a.Family, a.Size);
        a.Buffer.Span.CopyTo(copy.Buffer.Span);
        return copy;
    }

    private static IPEndPoint? EndPoint(SocketAddress a)
    {
        try
        {
            var endPoint = (IPEndPoint)new IPEndPoint(a.Family == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0).Create(a);
            return endPoint.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(endPoint.Address.MapToIPv4(), endPoint.Port) : endPoint;
        }
        catch (Exception e) when (e is ArgumentException or SocketException) { return null; }
    }

    /// <summary>KCP output: the reserved 16 bytes become the DATA header.</summary>
    private void Output(byte[] buffer, int length)
    {
        SbtWire.SessionHeader(buffer, SbtWire.Data, Id, Token);
        _owner.SendTo(buffer.AsSpan(0, length), _peer);
        LastSendMs = _owner.Now;
    }

    // ---- game loop side ----

    public Span<byte> BeginFrame(int bytes)
    {
        if (_fillLength + bytes > _fill.Length)
        {
            var bigger = ArrayPool<byte>.Shared.Rent(Math.Max(_fill.Length * 2, _fillLength + bytes));
            Buffer.BlockCopy(_fill, 0, bigger, 0, _fillLength);
            ArrayPool<byte>.Shared.Return(_fill);
            _fill = bigger;
        }
        return _fill.AsSpan(_fillLength, bytes);
    }

    public void CommitFrame(int bytes) => _fillLength += bytes;

    /// <summary>The tick's frames go into KCP in pieces of at most 64 segments (ikcp_send takes fewer than 128 at once).</summary>
    public void Flush()
    {
        if (_fillLength == 0 || IsClosed) return;
        lock (_gate)
        {
            var data = _fill.AsSpan(0, _fillLength);
            var piece = 64 * _kcp.Mss;
            while (data.Length > 0)
            {
                var n = Math.Min(piece, data.Length);
                _kcp.Send(data[..n]);
                data = data[n..];
            }
            _fillLength = 0;
            _kcp.Flush();
            if (_kcp.WaitSnd > 16 * 1024) { Log.Info($"UDP: session {Id:X8} does not keep up, closing"); CloseCore(sendClose: true); }
        }
    }

    public void Close() { lock (_gate) CloseCore(sendClose: true); }

    private void CloseCore(bool sendClose)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        if (sendClose)
        {
            Span<byte> close = stackalloc byte[SbtWire.HeaderBytes];
            SbtWire.SessionHeader(close, SbtWire.Close, Id, Token);
            _owner.SendTo(close, _peer);
        }
        _owner.Retire(this);
        _inbox.Enqueue(new InboundFrame(this, 0, InboundFrame.Disconnected, [], 0));
    }

    public void ReleaseBuffers()
    {
        lock (_gate)
        {
            ArrayPool<byte>.Shared.Return(_fill); _fill = [];
            ArrayPool<byte>.Shared.Return(_rx); _rx = [];
        }
    }

    // ---- UDP thread side ----

    /// <summary>A datagram with this session's id and token arrived from <paramref name="from"/>.</summary>
    internal void Receive(byte type, ReadOnlySpan<byte> datagram, SocketAddress from, long now, Span<byte> scratch)
    {
        lock (_gate)
        {
            if (IsClosed) return;
            if (!from.Equals(_peer)) { _peer = Copy(from); RemoteEndPoint = EndPoint(from); }   // roaming: follow the last valid datagram
            var silent = now - LastReceiveMs >= 300;
            LastReceiveMs = now;
            switch (type)
            {
                case SbtWire.Close: CloseCore(sendClose: false); return;
                case SbtWire.KeepAlive: break;
                case SbtWire.Data:
                    if (_kcp.Input(datagram[SbtWire.HeaderBytes..]) < 0) return;
                    int n;
                    while ((n = _kcp.Recv(scratch)) > 0) if (!Assemble(scratch[..n])) { CloseCore(sendClose: true); return; }
                    break;
            }
            if (silent) _kcp.ResendNow();      // heard again after 300 ms of silence: resend at once
        }
    }

    /// <summary>The KCP clock, keep-alives (1 s idle, 200 ms while the peer is silent) and the 45 s time-out.</summary>
    internal void Tick(long now)
    {
        lock (_gate)
        {
            if (IsClosed) return;
            if (now - LastReceiveMs > 45_000) { Log.Info($"UDP: session {Id:X8} timed out"); CloseCore(sendClose: false); return; }
            _kcp.Update((uint)now);
            var interval = now - LastReceiveMs > 200 ? 200 : 1000;
            if (now - LastSendMs >= interval)
            {
                Span<byte> alive = stackalloc byte[SbtWire.HeaderBytes];
                SbtWire.SessionHeader(alive, SbtWire.KeepAlive, Id, Token);
                _owner.SendTo(alive, _peer);
                LastSendMs = now;
            }
        }
    }

    /// <summary>TinCat frame reassembly over the KCP byte stream (same checks as the TCP path).</summary>
    private bool Assemble(ReadOnlySpan<byte> bytes)
    {
        if (_rxEnd + bytes.Length > _rx.Length)
        {
            var live = _rxEnd - _rxStart;
            if (live + bytes.Length > _rx.Length)
            {
                if (live + bytes.Length > TinCatWire.HeaderBytes + TinCatWire.MaximumPayloadBytes) return false;
                var bigger = ArrayPool<byte>.Shared.Rent(live + bytes.Length);
                Buffer.BlockCopy(_rx, _rxStart, bigger, 0, live);
                ArrayPool<byte>.Shared.Return(_rx);
                _rx = bigger;
            }
            else Buffer.BlockCopy(_rx, _rxStart, _rx, 0, live);
            _rxEnd = live; _rxStart = 0;
        }
        bytes.CopyTo(_rx.AsSpan(_rxEnd));
        _rxEnd += bytes.Length;
        while (_rxEnd - _rxStart >= TinCatWire.HeaderBytes)
        {
            var header = _rx.AsSpan(_rxStart, TinCatWire.HeaderBytes);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != TinCatWire.Magic) return false;
            var size = BinaryPrimitives.ReadInt32LittleEndian(header[20..]);
            if (size is <= 0 or > TinCatWire.MaximumPayloadBytes) return false;
            if (_rxEnd - _rxStart < TinCatWire.HeaderBytes + size) break;
            var body = _rx.AsSpan(_rxStart + TinCatWire.HeaderBytes, size);
            if (TinCatCrc.Compute(body) != BinaryPrimitives.ReadUInt32LittleEndian(header[24..])) return false;
            var payload = ArrayPool<byte>.Shared.Rent(size);
            body.CopyTo(payload);
            _inbox.Enqueue(new InboundFrame(this, BinaryPrimitives.ReadUInt32LittleEndian(header[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(header[12..]), payload, size));
            _rxStart += TinCatWire.HeaderBytes + size;
        }
        if (_rxStart == _rxEnd) _rxStart = _rxEnd = 0;
        return true;
    }
}
