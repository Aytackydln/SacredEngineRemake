using System.Buffers.Binary;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>Session/gameplay behaviour recovered from GameServer FUN_004c3f20 (see Routing.cs and GameServerHost.Gameplay.cs).</summary>
[TestClass]
public class SessionFlowTests
{
    private sealed class Server : IDisposable
    {
        public readonly GameServerHost Host;
        private readonly Thread _thread;
        public Server(Action<GameServerConfig>? configure = null)
        {
            var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 };
            configure?.Invoke(config);
            Host = new GameServerHost(config, new AdmissionRules(null), null);
            _thread = new Thread(Host.Run) { IsBackground = true };
            _thread.Start();
        }
        public int Port => Host.TcpPort;
        public void Dispose() { Host.Stop(); _thread.Join(); Host.Dispose(); }
    }

    private static byte[] Word(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); return b; }
    private static byte[] Text(string text, uint word = 0)
    {
        var b = new byte[4 + text.Length * 2 + 2];
        BinaryPrimitives.WriteUInt32LittleEndian(b, word);
        System.Text.Encoding.Unicode.GetBytes(text, b.AsSpan(4));
        return b;
    }

    private static (RawClient Host, RawClient Joiner) StartedSession(Server server)
    {
        var host = RawClient.Admit(server.Port, "Host", 1);
        Assert.AreEqual(1, host.AdmissionFlags & 1);
        host.Send(0x016, default);
        host.Send(0x018, Word(0x1964));
        var available = host.WaitFor(0x018);
        Assert.AreEqual(host.Slot, available.Origin);
        var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        Assert.AreEqual(4, joiner.AdmissionFlags & 5);   // host world available, not host
        joiner.Send(0x015, default);
        joiner.Send(0x017, default);
        Assert.AreEqual(joiner.Slot, joiner.WaitFor(0x017).Origin);
        return (host, joiner);
    }

    [TestMethod]
    public void HostStartAndParticipantApproval()
    {
        using var server = new Server();
        var (host, joiner) = StartedSession(server);
        using (host) using (joiner)
        {
            Assert.IsTrue(server.Host.HostWorldAvailable);
        }
    }

    [TestMethod]
    public void ForwardsFollowTheRoutingTable()
    {
        using var server = new Server();
        var (host, joiner) = StartedSession(server);
        using (host) using (joiner)
        {
            joiner.Send(0x1B0, Word(42));            // mode 2: in-game others
            var forwarded = host.WaitFor(0x1B0);
            Assert.AreEqual((byte)0, forwarded.Sender);
            Assert.AreEqual(joiner.Slot, forwarded.Origin);
            CollectionAssert.AreEqual(Word(42), forwarded.Payload);
            Assert.IsTrue(joiner.Silent(0x1B0));     // not echoed
            host.Send(0x11E, Word(7));               // includes the sender
            Assert.AreEqual(host.Slot, joiner.WaitFor(0x11E).Origin);
            Assert.AreEqual(host.Slot, host.WaitFor(0x11E).Origin);
            joiner.Send(0x1C9, Word(1));             // shared forward tail 0x4C81D1 (mode 2)
            Assert.AreEqual(joiner.Slot, host.WaitFor(0x1C9).Origin);
            host.Send(0x165, new byte[24]);          // position: world update + mode 8
            Assert.AreEqual(host.Slot, joiner.WaitFor(0x165).Origin);
            joiner.Send(0x122, Word(1));             // inventory op, consumed by the server's state
            Assert.IsTrue(host.Silent(0x122));
            joiner.Send(0x153, Word(1));             // internal menu command: never accepted from a client
            Assert.IsTrue(host.Silent(0x153));
            Assert.IsFalse(joiner.IsClosed());
        }
    }

    [TestMethod]
    public void InGameForwardsSkipPlayersStillLoading()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        using var loading = RawClient.Admit(server.Port, "Loading", 2);
        host.Send(0x018, Word(10));
        loading.WaitFor(0x018);
        host.Send(0x1B0, Word(1));
        Assert.IsTrue(loading.Silent(0x1B0));
    }

    [TestMethod]
    public void PublicAndPrivateChat()
    {
        using var server = new Server();
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        a.Send(0x005, Text("hello"));
        var chat = b.WaitFor(0x005);
        Assert.AreEqual(0x104, chat.Payload.Length);
        Assert.AreEqual(a.Slot, chat.Origin);
        Assert.AreEqual("hello", System.Text.Encoding.Unicode.GetString(chat.Payload, 4, 10));
        Assert.IsTrue(a.Silent(0x005));
        a.Send(0x003, Text("psst", b.Slot));
        var whisper = b.WaitFor(0x003);
        Assert.AreEqual((uint)b.Slot, BinaryPrimitives.ReadUInt32LittleEndian(whisper.Payload));
    }

    [TestMethod]
    public void HostOnlyCommandsAreRefusedForOthers()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        using var other = RawClient.Admit(server.Port, "Other", 2);
        other.Send(0x01A, default);
        Assert.AreEqual(0x64u, BinaryPrimitives.ReadUInt32LittleEndian(other.WaitFor(0x009).Payload));
        Assert.IsFalse(server.Host.IsLocked);
        host.Send(0x01A, default);
        other.WaitFor(0x01A);
        Assert.IsTrue(server.Host.IsLocked);
        using var late = new RawClient(server.Port);         // locked: new players are refused
        var contact = new byte[950];
        late.Send(0x10, contact, sender: 0);
        Assert.AreEqual(RejectionReason.LobbyRefused, BinaryPrimitives.ReadUInt32LittleEndian(late.WaitFor(0x03F).Payload));
    }

    [TestMethod]
    public void GameFlagsKeepServerOwnedBits()
    {
        using var server = new Server(c => c.Mode = PreconfigMode.PlayerKiller);
        using var host = RawClient.Admit(server.Port, "Host", 1);
        host.Send(0x01E, Word(0xFFFFFFFF));
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(host.WaitFor(0x01E).Payload);
        Assert.AreEqual(0xFFFE8F72u | 0x40u, flags);
    }

    [TestMethod]
    public void KickNotifiesVictimAndOthers()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        using var victim = RawClient.Admit(server.Port, "Victim", 2);
        using var third = RawClient.Admit(server.Port, "Third", 3);
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, victim.Slot);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 8);
        host.Send(0x157, payload);
        Assert.AreEqual(0x65u, BinaryPrimitives.ReadUInt32LittleEndian(host.WaitFor(0x009).Payload));
        Assert.AreEqual(8u, BinaryPrimitives.ReadUInt32LittleEndian(victim.WaitFor(0x157).Payload));
        var departure = third.WaitFor(0x150);
        Assert.AreEqual(victim.Slot, departure.Origin);
        Assert.IsTrue(victim.IsClosed());
    }

    [TestMethod]
    public void HostLeavingHandsOverToAnInGamePlayer()
    {
        using var server = new Server();
        var (host, joiner) = StartedSession(server);
        using (joiner)
        {
            host.Dispose();
            joiner.WaitFor(0x150);
            joiner.WaitFor(0x00D);
        }
    }

    [TestMethod]
    public void HostLeavingWithoutCandidateEndsTheSession()
    {
        using var server = new Server();
        var host = RawClient.Admit(server.Port, "Host", 1);
        using var loading = RawClient.Admit(server.Port, "Loading", 2);
        host.Dispose();
        Assert.AreEqual(0x10u, BinaryPrimitives.ReadUInt32LittleEndian(loading.WaitFor(0x157).Payload));
        Assert.IsTrue(loading.IsClosed());
    }

    [TestMethod]
    public void WorldBanksAreServedInOriginalOrder()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        for (ushort op = 0x1B1; op <= 0x1B4; op++) host.Send(op, Word(op));
        host.Send(0x018, Word(1));
        host.WaitFor(0x018);
        using var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        joiner.Send(0x1B5, default);
        foreach (ushort op in new ushort[] { 0x1B2, 0x1B4, 0x1B3, 0x1B1 })
        {
            RawMessage m;
            do m = joiner.TryReceive(3000) ?? throw new AssertFailedException("bank missing"); while (m.Opcode is < 0x1B1 or > 0x1B4);
            Assert.AreEqual(op, m.Opcode);
            CollectionAssert.AreEqual(Word(op), m.Payload);
        }
    }

    [TestMethod]
    public void TimeProbes()
    {
        using var server = new Server();
        using var client = RawClient.Admit(server.Port, "A", 1);
        client.Send(0x041, default);
        Assert.AreEqual(16, client.WaitFor(0x043).Payload.Length);
        var probe = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(probe, 11);
        BinaryPrimitives.WriteUInt32LittleEndian(probe.AsSpan(4), 22);
        client.Send(0x043, probe);
        var reply = client.WaitFor(0x044);
        Assert.AreEqual(11u, BinaryPrimitives.ReadUInt32LittleEndian(reply.Payload));
        Assert.AreEqual(22u, BinaryPrimitives.ReadUInt32LittleEndian(reply.Payload.AsSpan(4)));
    }
}
