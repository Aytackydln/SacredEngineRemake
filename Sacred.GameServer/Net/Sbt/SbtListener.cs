using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Sacred.GameServer.Net.Sbt;

/// <summary>
/// Host side of Sacred Bild's UDP transport (SBT, sacred-bild src/net/udp_transport.cpp): one dual-stack UDP socket and one
/// thread. HELLO without a valid cookie gets a CHALLENGE (never larger than the HELLO: no amplification), a valid one creates
/// a session (one per clientNonce, repeats get the same WELCOME) that is handed to the game loop like an accepted TCP
/// client. DATA/KEEPALIVE/CLOSE are accepted only with the session's id and token; the session answers to the address of
/// the last valid datagram (roaming). No allocation per datagram: one receive buffer, one SocketAddress, a session map.
/// </summary>
public sealed class SbtListener : IClientListener
{
    public const int MaxSessions = 64;
    private readonly Socket _socket;
    private readonly ConcurrentQueue<InboundFrame> _inbox;
    private readonly SbtCookies _cookies = new();
    private readonly Dictionary<uint, SbtLink> _sessions = new(MaxSessions);
    private readonly List<SbtLink> _tickList = new(MaxSessions);
    private readonly ConcurrentQueue<SbtLink> _retired = new();
    private readonly Func<int> _tcpPort;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly byte[] _receive = new byte[2048];
    private readonly byte[] _reply = new byte[64];
    private readonly byte[] _kcpScratch = new byte[256 * 1024];
    private readonly Lock _sendGate = new();
    private Thread? _thread;
    private volatile bool _stopped;

    public string Transport => "kcp";
    public int Port { get; }
    public ConcurrentQueue<IClientLink> Accepted { get; } = new();
    public int SessionCount { get { lock (_sessions) return _sessions.Count; } }
    internal long Now => _clock.ElapsedMilliseconds;

    /// <param name="tcpPort">The game's TCP port; HELLO must name it (Sacred Bild redirects the game's connect() to it).</param>
    public SbtListener(int port, ConcurrentQueue<InboundFrame> inbox, Func<int> tcpPort)
    {
        _inbox = inbox; _tcpPort = tcpPort;
        _socket = new Socket(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (Socket.OSSupportsIPv6) _socket.DualMode = true;
        if (OperatingSystem.IsWindows()) _socket.IOControl(-1744830452 /* SIO_UDP_CONNRESET */, [0, 0, 0, 0], null);
        _socket.Bind(new IPEndPoint(Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, port));
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
    }

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "sbt" };
        _thread.Start();
    }

    internal void SendTo(ReadOnlySpan<byte> datagram, SocketAddress to)
    {
        lock (_sendGate)
        {
            try { _socket.SendTo(datagram, SocketFlags.None, to); }
            catch (SocketException) { }               // unreachable peer: KCP resends, the time-out decides
            catch (ObjectDisposedException) { }
        }
    }

    internal void Retire(SbtLink link) => _retired.Enqueue(link);

    private void Loop()
    {
        var from = new SocketAddress(_socket.AddressFamily);
        long nextTick = 0;
        while (!_stopped)
        {
            try
            {
                if (_socket.Poll(5_000, SelectMode.SelectRead))
                {
                    var n = _socket.ReceiveFrom(_receive, SocketFlags.None, from);
                    if (n > 0) Handle(_receive.AsSpan(0, n), from);
                    continue;
                }
            }
            catch (SocketException) { continue; }
            catch (ObjectDisposedException) { return; }
            finally
            {
                var now = Now;
                if (now >= nextTick) { nextTick = now + 10; TickSessions(now); }
            }
        }
    }

    private void TickSessions(long now)
    {
        while (_retired.TryDequeue(out var gone)) lock (_sessions) if (!gone.Removed) { gone.Removed = true; _sessions.Remove(gone.Id); }
        _tickList.Clear();
        lock (_sessions) foreach (var link in _sessions.Values) _tickList.Add(link);
        foreach (var link in _tickList) link.Tick(now);
    }

    private void Handle(ReadOnlySpan<byte> d, SocketAddress from)
    {
        if (d.Length > SbtWire.MaxDatagram + 64 || !SbtWire.IsTransport(d)) return;
        var type = d[3];
        switch (type)
        {
            case SbtWire.Hello: OnHello(d, from); return;
            case SbtWire.Data: case SbtWire.KeepAlive: case SbtWire.Close:
                if (d.Length < SbtWire.HeaderBytes) return;
                var id = BinaryPrimitives.ReadUInt32LittleEndian(d[4..]);
                var token = BinaryPrimitives.ReadUInt64LittleEndian(d[8..]);
                SbtLink? link;
                lock (_sessions) _sessions.TryGetValue(id, out link);
                if (link is null || link.Token != token) return;
                link.Receive(type, d, from, Now, _kcpScratch);
                return;
        }
    }

    private void OnHello(ReadOnlySpan<byte> p, SocketAddress from)
    {
        if (p.Length < SbtWire.HelloBytes) return;                 // shorter than the answers: could amplify
        var nonce = BinaryPrimitives.ReadUInt32LittleEndian(p[8..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(p[4..]) != SbtWire.Version) { Refuse(from, nonce, SbtWire.RefusedVersion); return; }
        var tcpPort = BinaryPrimitives.ReadUInt16LittleEndian(p[12..]);
        if (tcpPort != _tcpPort()) { Refuse(from, nonce, SbtWire.RefusedNotListening); return; }
        if (!_cookies.Valid(from, nonce, p.Slice(16, 16)))
        {
            var c = _reply.AsSpan(0, SbtWire.ChallengeBytes);
            c.Clear();
            SbtWire.Magic(c, SbtWire.Challenge);
            BinaryPrimitives.WriteUInt32LittleEndian(c[4..], SbtWire.Version);
            BinaryPrimitives.WriteUInt32LittleEndian(c[8..], nonce);
            _cookies.Write(from, nonce, c[16..]);
            SendTo(c, from);
            return;
        }
        SbtLink? session = null;
        lock (_sessions)
        {
            foreach (var s in _sessions.Values) if (s.Nonce == nonce && !s.IsClosed) { session = s; break; }
            if (session is null)
            {
                if (_sessions.Count >= MaxSessions) { Refuse(from, nonce, SbtWire.RefusedFull); return; }
                Span<byte> random = stackalloc byte[12];
                uint id;
                do { RandomNumberGenerator.Fill(random); id = BinaryPrimitives.ReadUInt32LittleEndian(random); }
                while (id == 0 || _sessions.ContainsKey(id));
                session = new SbtLink(this, _inbox, id, BinaryPrimitives.ReadUInt64LittleEndian(random[4..]), nonce, from, Now);
                _sessions.Add(id, session);
                Accepted.Enqueue(session);
                Log.Info($"UDP: {session.RemoteEndPoint} connected (session {id:X8})");
            }
        }
        var w = _reply.AsSpan(0, SbtWire.WelcomeBytes);
        w.Clear();
        SbtWire.Magic(w, SbtWire.Welcome);
        BinaryPrimitives.WriteUInt32LittleEndian(w[4..], SbtWire.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(w[8..], nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(w[12..], session.Id);
        BinaryPrimitives.WriteUInt64LittleEndian(w[16..], session.Token);
        SendTo(w, from);
    }

    private void Refuse(SocketAddress to, uint nonce, uint reason)
    {
        var r = _reply.AsSpan(0, SbtWire.RefusedBytes);
        SbtWire.Magic(r, SbtWire.Refused);
        BinaryPrimitives.WriteUInt32LittleEndian(r[4..], SbtWire.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(r[8..], nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(r[12..], reason);
        SendTo(r, to);
    }

    public void Dispose()
    {
        _stopped = true;
        _socket.Dispose();
        _thread?.Join(1000);
    }
}
