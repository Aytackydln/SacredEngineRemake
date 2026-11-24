using System.Buffers.Binary;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>Object world of the dispatcher (cases 0x4C4D07 / 0x4C4D86, streamer FUN_004cd040).</summary>
[TestClass]
public sealed class WorldTests
{
    private sealed class Server : IDisposable
    {
        public readonly GameServerHost Host;
        private readonly Thread _thread;
        public Server()
        {
            Host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 }, new AdmissionRules(null), null);
            _thread = new Thread(Host.Run) { IsBackground = true };
            _thread.Start();
        }
        public int Port => Host.TcpPort;
        public void Dispose() { Host.Stop(); _thread.Join(); Host.Dispose(); }
    }

    private static RawClient InGame(Server server, string name, int identity, bool host)
    {
        var c = RawClient.Admit(server.Port, name, identity);
        if (host) { c.Send(0x016, default); c.Send(0x018, new byte[4]); c.WaitFor(0x018); }
        else { c.Send(0x015, default); c.Send(0x017, default); c.WaitFor(0x017); }
        return c;
    }

    private static byte[] State(uint reference, uint type, int x = 0, int y = 0, bool longPrefix = false, byte fill = 0xAB)
    {
        var p = new byte[(longPrefix ? 24 : 8) + 16];
        BinaryPrimitives.WriteUInt32LittleEndian(p, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), type);
        if (longPrefix) { BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(8), x); BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(12), y); }
        p.AsSpan(longPrefix ? 24 : 8).Fill(fill);
        return p;
    }

    [TestMethod]
    public void ObjectStatesReachEachInGamePlayerOnce()
    {
        using var server = new Server();
        using var host = InGame(server, "Host", 1, host: true);
        using var joiner = InGame(server, "Joiner", 2, host: false);
        host.Send(0x16E, State(0x500, 7));
        CollectionAssert.AreEqual(State(0x500, 7), joiner.WaitFor(0x16E).Payload);
        host.Send(0x16E, State(0x500, 7, fill: 0xCD));      // known: the server only updates its copy
        Assert.IsTrue(joiner.Silent(0x16E));
        Assert.AreEqual(1, server.Host.WorldObjectCount);
    }

    [TestMethod]
    public void LateJoinerGetsTheWorldBeforeItsStart()
    {
        using var server = new Server();
        using var host = InGame(server, "Host", 1, host: true);
        host.Send(0x16F, State(0x900, 3, x: 100, y: 200, longPrefix: true));
        host.Send(0x16E, State(0x600, 4));
        host.Send(0x16F, State(0x700, 5, longPrefix: true));  // no position: streamed as 0x16E
        Thread.Sleep(100);
        using var late = RawClient.Admit(server.Port, "Late", 3);
        late.Send(0x015, default);
        late.Send(0x017, default);
        var stream = new List<RawMessage>();
        while (late.TryReceive(3000) is { } m && m.Opcode != 0x017) if (m.Opcode is 0x16E or 0x16F) stream.Add(m);
        CollectionAssert.AreEqual(new uint[] { 0x600, 0x700, 0x900 }, stream.Select(m => BinaryPrimitives.ReadUInt32LittleEndian(m.Payload)).ToArray());
        CollectionAssert.AreEqual(new ushort[] { 0x16E, 0x16E, 0x16F }, stream.Select(m => m.Opcode).ToArray());
        Assert.AreEqual(8 + 16, stream[1].Payload.Length);
        CollectionAssert.AreEqual(State(0x900, 3, x: 100, y: 200, longPrefix: true), stream[2].Payload);
    }

    [TestMethod]
    public void OnlyTheOwnerRemovesObjects()
    {
        using var server = new Server();
        using var host = InGame(server, "Host", 1, host: true);
        using var joiner = InGame(server, "Joiner", 2, host: false);
        host.Send(0x16E, State(0x800, 9));
        joiner.WaitFor(0x16E);
        var reference = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(reference, 0x800);
        joiner.Send(0x166, reference);                       // not the owner: only the joiner's "has it" bit is cleared
        Assert.IsTrue(host.Silent(0x166));
        Assert.AreEqual(1, server.Host.WorldObjectCount);
        host.Send(0x16E, State(0x800, 9));                   // so the joiner gets it again
        joiner.WaitFor(0x16E);
        host.Send(0x167, reference);
        CollectionAssert.AreEqual(reference, joiner.WaitFor(0x167).Payload);
        Thread.Sleep(50);
        Assert.AreEqual(0, server.Host.WorldObjectCount);
    }

    [TestMethod]
    public void ServerOnlyObjectMessagesAreAbsorbed()
    {
        using var server = new Server();
        using var host = InGame(server, "Host", 1, host: true);
        using var joiner = InGame(server, "Joiner", 2, host: false);
        host.Send(0x16B, State(0xA00, 1, longPrefix: true));
        host.Send(0x16D, new byte[8]);
        Assert.IsTrue(joiner.Silent(0x16B));
        Assert.IsTrue(joiner.Silent(0x16D));
        host.Send(0x169, State(0xB00, 1, x: 5, y: 5, longPrefix: true));
        joiner.WaitFor(0x169);                                // absorbed and forwarded (mode 6)
    }
}
