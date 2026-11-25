using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Net;

/// <summary>
/// One TCP client. Receive: a single reusable SocketAsyncEventArgs over a pooled buffer reassembles TinCat frames and queues them for
/// the game loop. Send: the game loop appends frames to a pooled fill buffer; <see cref="Flush"/> swaps it with the in-flight buffer
/// and starts one reusable SocketAsyncEventArgs send. No per-packet allocation on either path.
/// </summary>
public sealed class ClientConnection : IClientLink
{
    private const int InitialReceive = 16 * 1024, InitialSend = 16 * 1024;
    private readonly Socket _socket;
    private readonly ConcurrentQueue<InboundFrame> _inbox;
    private readonly SocketAsyncEventArgs _receive = new(), _send = new();
    private byte[] _rx = ArrayPool<byte>.Shared.Rent(InitialReceive);
    private int _rxStart, _rxEnd;
    private byte[] _fill = ArrayPool<byte>.Shared.Rent(InitialSend);
    private byte[] _flight = ArrayPool<byte>.Shared.Rent(InitialSend);
    private int _fillLength, _flightLength, _flightSent;
    private int _sending, _closed;

    public IPEndPoint? RemoteEndPoint { get; }
    /// <summary>The server address this client reached (the LAN address the original puts into roster record 0).</summary>
    public IPEndPoint? LocalEndPoint { get; }
    public string Transport => "tcp";
    /// <summary>Game loop owned state.</summary>
    public object? Session { get; set; }

    public ClientConnection(Socket socket, ConcurrentQueue<InboundFrame> inbox)
    {
        _socket = socket;
        _inbox = inbox;
        RemoteEndPoint = socket.RemoteEndPoint as IPEndPoint;
        LocalEndPoint = socket.LocalEndPoint as IPEndPoint;
        _receive.Completed += (_, e) => OnReceive(e);
        _send.Completed += (_, e) => OnSend(e);
    }

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public void Start() => ReceiveLoop();

    private void ReceiveLoop()
    {
        while (!IsClosed)
        {
            _receive.SetBuffer(_rx, _rxEnd, _rx.Length - _rxEnd);
            bool pending;
            try { pending = _socket.ReceiveAsync(_receive); }
            catch (Exception e) when (e is SocketException or ObjectDisposedException) { Close(); return; }
            if (pending) return;
            if (!Process(_receive)) return;
        }
    }

    private void OnReceive(SocketAsyncEventArgs e)
    {
        if (Process(e)) ReceiveLoop();
    }

    private bool Process(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0) { Close(); return false; }
        _rxEnd += e.BytesTransferred;
        while (true)
        {
            var available = _rxEnd - _rxStart;
            if (available < TinCatWire.HeaderBytes) break;
            var header = _rx.AsSpan(_rxStart, TinCatWire.HeaderBytes);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != TinCatWire.Magic) { Close(); return false; }
            var size = BinaryPrimitives.ReadInt32LittleEndian(header[20..]);
            if (size is <= 0 or > TinCatWire.MaximumPayloadBytes) { Close(); return false; }
            var total = TinCatWire.HeaderBytes + size;
            if (available < total) { EnsureReceiveCapacity(total); break; }
            var body = _rx.AsSpan(_rxStart + TinCatWire.HeaderBytes, size);
            if (TinCatCrc.Compute(body) != BinaryPrimitives.ReadUInt32LittleEndian(header[24..])) { Close(); return false; }
            var payload = ArrayPool<byte>.Shared.Rent(size);
            body.CopyTo(payload);
            _inbox.Enqueue(new InboundFrame(this, BinaryPrimitives.ReadUInt32LittleEndian(header[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(header[12..]), payload, size));
            _rxStart += total;
        }
        if (_rxStart == _rxEnd) _rxStart = _rxEnd = 0;
        else if (_rxStart > 0 && _rx.Length - _rxEnd < 4096)
        {
            Buffer.BlockCopy(_rx, _rxStart, _rx, 0, _rxEnd - _rxStart);
            _rxEnd -= _rxStart; _rxStart = 0;
        }
        else if (_rx.Length - _rxEnd == 0) EnsureReceiveCapacity(_rx.Length * 2);
        return !IsClosed;
    }

    private void EnsureReceiveCapacity(int frameBytes)
    {
        if (_rxStart + frameBytes <= _rx.Length) return;
        if (frameBytes <= _rx.Length) { Buffer.BlockCopy(_rx, _rxStart, _rx, 0, _rxEnd - _rxStart); _rxEnd -= _rxStart; _rxStart = 0; return; }
        var bigger = ArrayPool<byte>.Shared.Rent(frameBytes);
        Buffer.BlockCopy(_rx, _rxStart, bigger, 0, _rxEnd - _rxStart);
        _rxEnd -= _rxStart; _rxStart = 0;
        ArrayPool<byte>.Shared.Return(_rx);
        _rx = bigger;
    }

    // ---- send side (game loop thread only, except OnSend which only touches the in-flight buffer) ----

    /// <summary>Reserve <paramref name="bytes"/> in the fill buffer and return the span to write a complete TinCat frame into.</summary>
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

    public int PendingBytes => _fillLength;

    /// <summary>Start sending the fill buffer if no send is in flight.</summary>
    public void Flush()
    {
        if (_fillLength == 0 || IsClosed || Volatile.Read(ref _sending) != 0) return;
        (_fill, _flight) = (_flight, _fill);
        _flightLength = _fillLength; _fillLength = 0; _flightSent = 0;
        Volatile.Write(ref _sending, 1);
        SendNext();
    }

    private void SendNext()
    {
        while (true)
        {
            _send.SetBuffer(_flight, _flightSent, _flightLength - _flightSent);
            bool pending;
            try { pending = _socket.SendAsync(_send); }
            catch (Exception e) when (e is SocketException or ObjectDisposedException) { Close(); return; }
            if (pending) return;
            if (!AfterSend(_send)) return;
        }
    }

    private void OnSend(SocketAsyncEventArgs e) { if (AfterSend(e)) SendNext(); }

    private bool AfterSend(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0) { Close(); return false; }
        _flightSent += e.BytesTransferred;
        if (_flightSent < _flightLength) return true;
        Volatile.Write(ref _sending, 0);
        return false;
    }

    /// <summary>Closes the socket once; the game loop receives one <see cref="InboundFrame.Disconnected"/> frame.</summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        try { _socket.Shutdown(SocketShutdown.Both); } catch (SocketException) { } catch (ObjectDisposedException) { }
        _socket.Dispose();
        _inbox.Enqueue(new InboundFrame(this, 0, InboundFrame.Disconnected, [], 0));
    }

    /// <summary>Called by the game loop after it saw the disconnect frame: release pooled buffers.</summary>
    public void ReleaseBuffers()
    {
        ArrayPool<byte>.Shared.Return(_rx); _rx = [];
        ArrayPool<byte>.Shared.Return(_fill); _fill = [];
        ArrayPool<byte>.Shared.Return(_flight); _flight = [];
        _receive.Dispose(); _send.Dispose();
    }
}
