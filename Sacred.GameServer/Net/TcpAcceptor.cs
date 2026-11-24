using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Sacred.GameServer.Net;

/// <summary>Accepts TCP clients with one reusable SocketAsyncEventArgs and queues them for the game loop.</summary>
public sealed class TcpAcceptor : IClientListener
{
    private readonly Socket _listener;
    private readonly SocketAsyncEventArgs _accept = new();
    private readonly ConcurrentQueue<InboundFrame> _inbox;
    private readonly ConcurrentQueue<IClientLink> _accepted = new();
    private volatile bool _stopped;

    public int Port { get; }
    public string Transport => "tcp";
    public ConcurrentQueue<IClientLink> Accepted => _accepted;

    public IPEndPoint? LocalEndPoint => (IPEndPoint?)_listener.LocalEndPoint;

    /// <param name="bind">Address to bind (null: <see cref="BindPolicy.Default"/>, normally every interface).</param>
    public TcpAcceptor(int port, ConcurrentQueue<InboundFrame> inbox, IPAddress? bind = null)
    {
        _inbox = inbox;
        var (socket, endPoint) = BindPolicy.Create(SocketType.Stream, ProtocolType.Tcp, bind, port);
        _listener = socket;
        _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Bind(endPoint);
        _listener.Listen(32);
        Port = ((IPEndPoint)_listener.LocalEndPoint!).Port;
        _accept.Completed += (_, e) => OnAccept(e);
    }

    private int _started;

    /// <summary>Starts accepting; further calls do nothing.</summary>
    public void Start() { if (Interlocked.Exchange(ref _started, 1) == 0) AcceptLoop(); }

    private void AcceptLoop()
    {
        while (!_stopped)
        {
            _accept.AcceptSocket = null;
            bool pending;
            try { pending = _listener.AcceptAsync(_accept); }
            catch (ObjectDisposedException) { return; }
            if (pending) return;
            Handle(_accept);
        }
    }

    private void OnAccept(SocketAsyncEventArgs e) { Handle(e); AcceptLoop(); }

    private void Handle(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success || e.AcceptSocket is not { } socket) return;
        socket.NoDelay = true;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        var connection = new ClientConnection(socket, _inbox);
        _accepted.Enqueue(connection);
        connection.Start();
    }

    public void Dispose() { _stopped = true; _listener.Dispose(); _accept.Dispose(); }
}
