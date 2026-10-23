using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Sacred.Network.Connection;
using Sacred.Network.Protocol;
using Sacred.Network.TinCat;

namespace Sacred.Network.Tests.Infrastructure;

/// <summary>One production connection, one private ephemeral listener, no native installation or windows.</summary>
internal sealed class GoldLoopbackPeer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(10));
    private readonly TaskCompletionSource<NetworkStream> _stream = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _server = Task.CompletedTask;
    public GoldGameConnection Connection { get; } = new();
    public CancellationToken Token => _stop.Token;

    public static async Task<GoldLoopbackPeer> StartAsync()
    {
        var peer = new GoldLoopbackPeer();
        try
        {
            peer._listener.Start(); peer._server = peer.AcceptAsync();
            peer.Connection.Start("127.0.0.1", ((IPEndPoint)peer._listener.LocalEndpoint).Port);
            var connected = await peer.Connection.Events.ReadAsync(peer.Token);
            if (connected is not GoldTransportConnected { ConnectionId: 7 })
                throw new InvalidDataException("Loopback TinCat login failed: " + connected);
            return peer;
        }
        catch { await peer.DisposeAsync(); throw; }
    }
    private async Task AcceptAsync()
    {
        try
        {
            using var socket = await _listener.AcceptTcpClientAsync(Token);
            using var stream = socket.GetStream();
            var login = await TinCatStream.ReadAsync(stream, Token);
            if (login.Type != TinCatLogin.LoginRequest) throw new InvalidDataException("Expected TinCat login request.");
            var payload = new byte[52];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, TinCatFrameCodec.Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 7);
            await TinCatStream.WriteAsync(stream, new(TinCatLogin.ServerId, 7, TinCatLogin.LoginAccepted, 0, payload), Token);
            _stream.SetResult(stream);
            await Task.Delay(Timeout.Infinite, Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { _stream.TrySetException(error); throw; }
    }
    public async Task SendAsync(params GoldMessage[] messages)
    {
        var stream = await _stream.Task.WaitAsync(Token);
        var batch = messages.SelectMany(GoldMessageCodec.Encode).ToArray();
        var payload = TinCatCustomData.Encode(TinCatLogin.GameModule, 0, GoldEnvelopeCodec.Encode(batch));
        var frame = TinCatFrameCodec.Encode(new(TinCatLogin.ServerId, 7, TinCatLogin.CustomData, 0, payload));
        // Fragment headers and bodies alike. Every packet case takes the actual stream assembly path.
        for (var offset = 0; offset < frame.Length; offset += 7)
            await stream.WriteAsync(frame.AsMemory(offset, Math.Min(7, frame.Length - offset)), Token);
    }
    public async Task<GoldMessagesReceived> ReceiveAsync()
    {
        while (true)
        {
            var entry = await Connection.Events.ReadAsync(Token);
            if (entry is GoldMessagesReceived batch) return batch;
            if (entry is GoldConnectionClosed closed) throw new IOException(closed.Reason);
        }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop();
        await Connection.DisposeAsync();
        try { await _server; } finally { _stop.Dispose(); }
    }
}
