using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Sacred.GameServer.Config;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Session;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Tests;

/// <summary>Lobby registration against a minimal lobby that answers like sacred-lobby (TinCat logon, 12 -> 4-byte IP + 15).</summary>
[TestClass]
public sealed class LobbyTests
{
    internal sealed class FakeLobby : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private NetworkStream? _stream;
        public readonly BlockingCollection<(ushort Type, byte[] Body)> Received = new();
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public Exception? Error;

        public FakeLobby()
        {
            _listener.Start();
            new Thread(Serve) { IsBackground = true }.Start();
        }

        private void Serve()
        {
            try
            {
                using var tcp = _listener.AcceptTcpClient();
                _stream = tcp.GetStream();
                while (true)
                {
                    var (type, payload) = Read();
                    if (type == TinCatWire.LoginRequest)
                    {
                        var accept = (byte[])payload.Clone();
                        BinaryPrimitives.WriteUInt32LittleEndian(accept.AsSpan(4), 0x4242);
                        Write(TinCatWire.LoginAccepted, accept);
                        continue;
                    }
                    if (type != TinCatWire.CustomData) continue;
                    var message = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2));
                    Assert.AreEqual(LobbyProtocol.Module, BinaryPrimitives.ReadUInt16LittleEndian(payload));
                    Assert.AreEqual(LobbyProtocol.KeyOf(message), BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(6)));
                    Assert.AreEqual((uint)(payload.Length - 4), BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(10)));
                    var body = payload[LobbyProtocol.PrefixBytes..];
                    Received.Add((message, body));
                    if (message == LobbyProtocol.ServerLogin)
                    {
                        SendLobby(LobbyProtocol.ServerLogin, [203, 0, 113, 7]);
                        var result = new byte[8];
                        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), LobbyProtocol.ServerLogin);
                        SendLobby(LobbyProtocol.Result, result);
                    }
                }
            }
            catch (Exception e) { if (e is not (IOException or ObjectDisposedException or SocketException or InvalidOperationException)) Error = e; }
        }

        public void SendLobby(ushort type, ReadOnlySpan<byte> body)
        {
            var payload = new byte[LobbyProtocol.PrefixBytes + body.Length];
            LobbyProtocol.WritePrefix(payload, type, body.Length);
            body.CopyTo(payload.AsSpan(LobbyProtocol.PrefixBytes));
            Write(TinCatWire.CustomData, payload);
        }

        public (ushort Type, byte[] Body) WaitFor(ushort type, int timeoutMs = 5000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Received.TryTake(out var m, (int)Math.Max(1, deadline - Environment.TickCount64)))
                if (m.Type == type) return m;
            throw new AssertFailedException($"lobby did not receive message {type} {Error}");
        }

        private void Write(uint type, byte[] payload)
        {
            var frame = new byte[TinCatWire.HeaderBytes + payload.Length];
            payload.CopyTo(frame, TinCatWire.HeaderBytes);
            TinCatWire.SealFrame(frame, TinCatWire.ServerId, 0x4242, type, payload.Length);
            lock (this) _stream!.Write(frame);
        }

        private (uint, byte[]) Read()
        {
            var header = new byte[TinCatWire.HeaderBytes];
            _stream!.ReadExactly(header);
            var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20))];
            _stream.ReadExactly(payload);
            return (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)), payload);
        }

        public void Dispose() { _listener.Stop(); _stream?.Dispose(); }
    }

    private sealed class Server : IDisposable
    {
        public readonly GameServerHost Host;
        public readonly LobbyClient Lobby;
        private readonly Thread _thread;
        public Server(FakeLobby fake, int channel = LobbyProtocol.OpenNetChannel)
        {
            var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5, ServerName = "Test realm", LobbyChannel = channel };
            Host = new GameServerHost(config, new AdmissionRules(null), null);
            Host.Start();
            var record = Host.LobbyRecord(0x1234, 0);
            Lobby = new LobbyClient("127.0.0.1", fake.Port, record);
            Host.AttachLobby(Lobby, record);
            Lobby.Start();
            _thread = new Thread(Host.Run) { IsBackground = true };
            _thread.Start();
            var deadline = Environment.TickCount64 + 5000;
            while (!Lobby.IsOnline && Environment.TickCount64 < deadline) Thread.Sleep(10);
            Assert.IsTrue(Lobby.IsOnline, "lobby registration");
            Thread.Sleep(50); // let the game loop see the Online event
        }
        public int Port => Host.TcpPort;
        public void Dispose() { Host.Stop(); _thread.Join(); Lobby.Dispose(); Host.Dispose(); }
    }

    [TestMethod]
    public void RegistersTheGameRecord()
    {
        using var fake = new FakeLobby();
        using var server = new Server(fake);
        var (_, body) = fake.WaitFor(LobbyProtocol.ServerLogin);
        Assert.AreEqual(LobbyProtocol.GameRecordBytes, body.Length);
        Assert.AreEqual("Test realm", LobbyProtocol.Text(body.AsSpan(0, 80)));
        CollectionAssert.AreEqual(new byte[] { 127, 0, 0, 1 }, body[80..84]);            // LAN address of the lobby socket
        Assert.AreEqual(server.Port, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(88)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(92)));
        Assert.AreEqual(LobbyProtocol.NetworkVersion, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(104)));
        Assert.AreEqual(0x1234, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(108)));
        Assert.AreEqual(LobbyProtocol.OpenNetChannel, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(112)));
        Assert.AreEqual(IPAddress.Parse("203.0.113.7"), server.Lobby.PublicAddress);
    }

    [TestMethod]
    public void AnnouncesPlayersAndUpdatesTheCount()
    {
        using var fake = new FakeLobby();
        using var server = new Server(fake);
        using (var player = RawClient.Admit(server.Port, "Hero", 1234, block: 3))
        {
            var (_, joined) = fake.WaitFor(LobbyProtocol.UserJoinedServer);
            Assert.AreEqual(1234, BinaryPrimitives.ReadInt32LittleEndian(joined));
            Assert.AreEqual(3, BinaryPrimitives.ReadUInt16LittleEndian(joined.AsSpan(4)));
            Assert.AreEqual(1, BinaryPrimitives.ReadUInt16LittleEndian(joined.AsSpan(6)));
            var (_, update) = fake.WaitFor(LobbyProtocol.ServerUpdate);
            Assert.AreEqual(1, BinaryPrimitives.ReadInt16LittleEndian(update.AsSpan(92)));
        }
        var (_, left) = fake.WaitFor(LobbyProtocol.UserLeftServer);
        Assert.AreEqual(1234, BinaryPrimitives.ReadInt32LittleEndian(left));
        var (_, after) = fake.WaitFor(LobbyProtocol.ServerUpdate);
        Assert.AreEqual(0, BinaryPrimitives.ReadInt16LittleEndian(after.AsSpan(92)));
    }

    [TestMethod]
    public void DuplicateIdentityIsRejectedWhileOnline()
    {
        using var fake = new FakeLobby();
        using var server = new Server(fake);
        using var first = RawClient.Admit(server.Port, "A", 77);
        Assert.AreEqual(RejectionReason.DuplicateIdentity, RawClient.AdmitExpectingRejection(server.Port, "B", 77));
    }

    [TestMethod]
    public void LobbyKickRemovesThePlayer()
    {
        using var fake = new FakeLobby();
        using var server = new Server(fake);
        using var player = RawClient.Admit(server.Port, "Hero", 99);
        fake.WaitFor(LobbyProtocol.UserJoinedServer);
        var kick = new byte[0x116 - LobbyProtocol.EnvelopeBytes];
        BinaryPrimitives.WriteInt32LittleEndian(kick, 99);
        fake.SendLobby(LobbyProtocol.UserKicked, kick);
        var m = player.WaitFor(0x157);
        Assert.AreEqual(8u, BinaryPrimitives.ReadUInt32LittleEndian(m.Payload));
    }

    [TestMethod]
    public void ClosedNetCharacterIsPassedOnAs0x132()
    {
        using var fake = new FakeLobby();
        using var server = new Server(fake);
        using var player = RawClient.Admit(server.Port, "Hero", 500, block: 2);
        fake.WaitFor(LobbyProtocol.UserJoinedServer);
        var data = new byte[0x300];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)i;
        var body = new byte[18 + data.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body, 500);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(6), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(14), data.Length);
        data.CopyTo(body, 18);
        fake.SendLobby(LobbyProtocol.PublicData, body);
        CollectionAssert.AreEqual(data, player.WaitFor(0x132).Payload);
    }

    [TestMethod]
    public void ClosedNetPlayerTriggersAHeroRequestAndGetsTheBlock()
    {
        using var fake = new FakeLobby();
        using var server = new Server(fake, channel: 1);
        using var player = RawClient.Admit(server.Port, "Hero", 500, block: 2);
        var request = fake.WaitFor(LobbyProtocol.ServerDataRequest).Body;
        Assert.AreEqual(14, request.Length);
        Assert.AreEqual(500, BinaryPrimitives.ReadInt32LittleEndian(request));
        Assert.AreEqual(2, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(4)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(6)));
        Assert.AreEqual(0x20000, BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(10)));
        var data = new byte[0x1800];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i * 7);
        var body = new byte[18 + data.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body, 500);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(6), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(14), data.Length);
        data.CopyTo(body, 18);
        fake.SendLobby(LobbyProtocol.PublicData, body);
        CollectionAssert.AreEqual(data, player.WaitFor(0x132).Payload);
    }

    [TestMethod]
    public void AnUnaskedPushAndALaterAnswerDeliverTheHeroOnlyOnce()
    {
        using var fake = new FakeLobby();
        using var server = new Server(fake, channel: 1);
        using var player = RawClient.Admit(server.Port, "Hero", 500, block: 2);
        fake.WaitFor(LobbyProtocol.UserJoinedServer);
        var data = new byte[0x900];
        new Random(8).NextBytes(data);
        var body = new byte[18 + data.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body, 500);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(6), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(14), data.Length);
        data.CopyTo(body, 18);
        fake.SendLobby(LobbyProtocol.PublicData, body);     // the push
        CollectionAssert.AreEqual(data, player.WaitFor(0x132).Payload);
        fake.SendLobby(LobbyProtocol.PublicData, body);     // a second copy (answer to a request)
        Assert.IsTrue(player.Silent(0x132, 500));
        Thread.Sleep(2500);                                   // the ask is skipped when the block arrived first
        while (fake.Received.TryTake(out var m)) Assert.AreNotEqual(LobbyProtocol.ServerDataRequest, m.Type);
    }

    [TestMethod]
    public void OpenNetPlayersKeepTheirLocalHero()
    {
        using var fake = new FakeLobby();
        using var server = new Server(fake);
        using var player = RawClient.Admit(server.Port, "Hero", 500, block: 2);
        fake.WaitFor(LobbyProtocol.UserJoinedServer);
        Thread.Sleep(200);
        while (fake.Received.TryTake(out var m)) Assert.AreNotEqual(LobbyProtocol.ServerDataRequest, m.Type);
    }

    [TestMethod]
    public void CharacterUploadFrameCarriesTheBlock()
    {
        using var fake = new FakeLobby();
        using var server = new Server(fake, channel: 1);
        var block = new byte[0x400];
        new Random(5).NextBytes(block);
        Assert.IsFalse(server.Lobby.UploadCharacter(9, 3, new byte[0x100]));          // not even a preview and the sizes
        Assert.IsTrue(server.Lobby.UploadCharacter(9, 3, block));
        var upload = fake.WaitFor(LobbyProtocol.ServerDataUpload).Body;
        Assert.AreEqual(18 + block.Length, upload.Length);
        Assert.AreEqual(9, BinaryPrimitives.ReadInt32LittleEndian(upload));
        Assert.AreEqual(3, BinaryPrimitives.ReadUInt16LittleEndian(upload.AsSpan(4)));
        Assert.AreEqual(block.Length, BinaryPrimitives.ReadInt32LittleEndian(upload.AsSpan(6)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt32LittleEndian(upload.AsSpan(10)));
        Assert.AreEqual(block.Length, BinaryPrimitives.ReadInt32LittleEndian(upload.AsSpan(14)));
        CollectionAssert.AreEqual(block, upload[18..]);
    }

    [TestMethod]
    public void LobbyShutdownEndsTheSession()
    {
        using var fake = new FakeLobby();
        using var server = new Server(fake);
        using var player = RawClient.Admit(server.Port, "Hero", 1);
        fake.SendLobby(LobbyProtocol.ServerShutdown, new byte[0x10E - LobbyProtocol.EnvelopeBytes]);
        player.WaitFor(0x008);
        Assert.AreEqual(8u, BinaryPrimitives.ReadUInt32LittleEndian(player.WaitFor(0x157).Payload));
    }
}
