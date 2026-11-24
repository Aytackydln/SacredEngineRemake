using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Tests;

/// <summary>The async lobby client: a lost connection is retried after the back-off and the game is announced again; Stop ends the back-off at once.</summary>
[TestClass]
public class LobbyReconnectTests
{
    /// <summary>A lobby that accepts any number of connections, answers the logon and message 12 like sacred-lobby, and can drop the current one.</summary>
    private sealed class MultiLobby : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private volatile TcpClient? _current;
        public int Logins, Connections;
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public MultiLobby()
        {
            _listener.Start();
            new Thread(AcceptLoop) { IsBackground = true }.Start();
        }

        private void AcceptLoop()
        {
            try
            {
                while (true)
                {
                    var tcp = _listener.AcceptTcpClient();
                    Interlocked.Increment(ref Connections);
                    _current = tcp;
                    new Thread(() => Serve(tcp)) { IsBackground = true }.Start();
                }
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException) { }
        }

        private void Serve(TcpClient tcp)
        {
            try
            {
                var stream = tcp.GetStream();
                var header = new byte[TinCatWire.HeaderBytes];
                while (true)
                {
                    stream.ReadExactly(header);
                    var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20))];
                    stream.ReadExactly(payload);
                    var type = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
                    if (type == TinCatWire.LoginRequest)
                    {
                        var accept = (byte[])payload.Clone();
                        BinaryPrimitives.WriteUInt32LittleEndian(accept.AsSpan(4), 0x4242);
                        Write(stream, TinCatWire.LoginAccepted, accept);
                    }
                    else if (type == TinCatWire.CustomData && BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2)) == LobbyProtocol.ServerLogin)
                    {
                        Interlocked.Increment(ref Logins);
                        Reply(stream, LobbyProtocol.ServerLogin, [203, 0, 113, 7]);
                        var result = new byte[8];
                        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), LobbyProtocol.ServerLogin);
                        Reply(stream, LobbyProtocol.Result, result);
                    }
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException) { }
        }

        private static void Reply(NetworkStream stream, ushort type, ReadOnlySpan<byte> body)
        {
            var payload = new byte[LobbyProtocol.PrefixBytes + body.Length];
            LobbyProtocol.WritePrefix(payload, type, body.Length);
            body.CopyTo(payload.AsSpan(LobbyProtocol.PrefixBytes));
            Write(stream, TinCatWire.CustomData, payload);
        }

        private static void Write(NetworkStream stream, uint type, byte[] payload)
        {
            var frame = new byte[TinCatWire.HeaderBytes + payload.Length];
            payload.CopyTo(frame, TinCatWire.HeaderBytes);
            TinCatWire.SealFrame(frame, TinCatWire.ServerId, 0x4242, type, payload.Length);
            lock (stream) stream.Write(frame);
        }

        public void DropCurrent() => _current?.Dispose();
        public void Dispose() { _listener.Stop(); _current?.Dispose(); }
    }

    private static bool Eventually(Func<bool> condition, int timeoutMs)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < until) Thread.Sleep(20);
        return condition();
    }

    [TestMethod]
    public void ALostConnectionIsRetriedAfterTheBackoffAndTheGameIsAnnouncedAgain()
    {
        using var lobby = new MultiLobby();
        var record = new LobbyProtocol.GameRecord("Reconnect", 0, 0, 2006, 0, 16, 0x20, 0x1234, 5);
        using var client = new LobbyClient("127.0.0.1", lobby.Port, record);
        client.Start();
        Assert.IsTrue(Eventually(() => lobby.Logins == 1 && client.IsOnline, 5000), "first login");
        lobby.DropCurrent();
        Assert.IsTrue(Eventually(() => !client.IsOnline, 3000), "offline after the drop");
        Assert.IsTrue(Eventually(() => lobby.Logins == 2 && client.IsOnline, 6000), "second login after the 2 s back-off");
        var events = new List<LobbyEventKind>();
        while (client.Events.TryDequeue(out var e)) events.Add(e.Kind);
        CollectionAssert.AreEqual(new[] { LobbyEventKind.Online, LobbyEventKind.Offline, LobbyEventKind.Online }, events);
    }

    [TestMethod]
    public void StopDuringTheBackoffEndsAtOnceAndNothingConnectsAgain()
    {
        using var lobby = new MultiLobby();
        var record = new LobbyProtocol.GameRecord("Stop", 0, 0, 2006, 0, 16, 0x20, 0x1234, 5);
        var client = new LobbyClient("127.0.0.1", lobby.Port, record);
        client.Start();
        Assert.IsTrue(Eventually(() => lobby.Logins == 1 && client.IsOnline, 5000));
        lobby.DropCurrent();
        Assert.IsTrue(Eventually(() => !client.IsOnline, 3000));          // now inside the 2 s back-off
        var watch = Stopwatch.StartNew();
        client.Dispose();
        watch.Stop();
        Assert.IsTrue(watch.ElapsedMilliseconds < 400, $"Dispose took {watch.ElapsedMilliseconds} ms");
        Thread.Sleep(2600);                                               // longer than the back-off
        Assert.AreEqual(1, lobby.Connections, "no reconnect after Stop");
    }
}
