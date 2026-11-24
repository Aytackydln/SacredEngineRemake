using System.Buffers.Binary;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>Object references 0x153, trigger ids 0x154, sector entries 0x148 and the environment 0x00B, checked against a
/// captured GameServer.exe session (run 8).</summary>
[TestClass]
public class WorldControlTests
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

    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));

    [TestMethod]
    public void AdmissionMatchesTheOriginalOrder()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        var roster = host.WaitFor(0x01B).Payload;
        Assert.AreEqual(0x7F000001u, U32(roster, 0));                        // record 0: server address as the client reached it
        host.WaitFor(0x043);
        var refs = host.WaitFor(0x153).Payload;
        Assert.AreEqual(12000, refs.Length);
        Assert.AreEqual(0x11u, U32(refs, 0));
        Assert.AreEqual(0xBC8u, U32(refs, refs.Length - 4));
        var environment = host.WaitFor(0x00B).Payload;
        Assert.AreEqual(508, environment.Length);
        Assert.AreEqual(1, environment[0]);
        Assert.AreEqual(1370, BinaryPrimitives.ReadInt32LittleEndian(environment.AsSpan(4)));
        Assert.AreEqual(2, BinaryPrimitives.ReadUInt16LittleEndian(environment.AsSpan(8)));
        Assert.AreEqual(14, BinaryPrimitives.ReadUInt16LittleEndian(environment.AsSpan(10)));
        Assert.AreEqual(8, BinaryPrimitives.ReadUInt16LittleEndian(environment.AsSpan(12)));
    }

    [TestMethod]
    public void InventoryPlacementIsConsumedWithoutAnUnhandledLog()
    {
        // 0x13F (SETITEM) is applied to the server's own inventory only; in relay mode it must not log "unhandled opcode".
        Assert.IsTrue(Routing.IsConsumed(0x13F));
        Assert.IsTrue(Routing.IsConsumed(0x13E));
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        var log = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(TextWriter.Synchronized(log));
        try
        {
            host.Send(0x13F, new byte[12]);
            Thread.Sleep(300);
            Assert.IsFalse(host.IsClosed());
            Assert.IsFalse(log.ToString().Contains("unhandled opcode"), log.ToString());
        }
        finally { Console.SetOut(previous); }
    }

    [TestMethod]
    public void TriggerRangeAndReferenceRequests()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        host.WaitFor(0x153);
        var request = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(request, 0x8DC);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), 2000);
        host.Send(0x154, request);
        var answer = host.WaitFor(0x154).Payload;                            // run 8: dc08/d007 -> dd08/d007
        Assert.AreEqual(0x8DDu, U32(answer, 0));
        Assert.AreEqual(2000u, U32(answer, 4));
        host.Send(0x154, request);
        Assert.AreEqual(0x8DDu + 2000, U32(host.WaitFor(0x154).Payload, 0));

        var ask = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(ask, 10);
        host.Send(0x153, ask);
        var more = host.WaitFor(0x153).Payload;
        Assert.AreEqual(40, more.Length);
        Assert.AreEqual(0xBC9u, U32(more, 0));                               // continues after the admission grant
    }

    [TestMethod]
    public void SectorEntryAnswersAndAssignsOwnership()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        host.WaitFor(0x153);
        // run 8 request: current 0x0B5D, nine entered sectors, 0x87C refs left
        var entry = Convert.FromHexString("5d0b00001f0b200b210b5e0b9d0b9c0b9b0b5c0b5d0b" + new string('0', 36 * 2) + "7c080000");
        Assert.AreEqual(62, entry.Length);
        host.Send(0x148, entry);
        var answer = host.WaitFor(0x148).Payload;
        Assert.AreEqual(0u, U32(answer, 0));
        CollectionAssert.AreEqual(entry[4..22], answer[4..22]);
        for (var i = 0; i < 9; i++) { Assert.AreEqual(1, answer[40 + i]); Assert.AreEqual(1, answer[49 + i]); }
        Assert.AreEqual(0x87Cu, U32(answer, 58));
        host.WaitFor(0x020);                                                 // first in the sector: owner

        using var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        joiner.Send(0x148, entry);
        var second = joiner.WaitFor(0x148).Payload;
        for (var i = 0; i < 9; i++) { Assert.AreEqual(0, second[40 + i]); Assert.AreEqual(0, second[49 + i]); }
    }

    private static byte[] ObjectState(uint reference, ushort sector, int dataBytes)
    {
        var p = new byte[24 + dataBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(p, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), 7);             // type
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), 100);           // x
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(12), 200);          // y
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(20), sector);
        for (var i = 24; i < p.Length; i++) p[i] = (byte)i;
        return p;
    }

    [TestMethod]
    public void SectorEntryStreamsTheSectorsObjectsFirst()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        host.WaitFor(0x153);
        var inside = ObjectState(0x20, 0x0B5D, 40);
        host.Send(0x16F, inside);                                          // uploaded by the host: it has it
        host.Send(0x16F, ObjectState(0x21, 0x0001, 40));                   // elsewhere: not streamed
        var entry = Convert.FromHexString("5d0b00001f0b200b210b5e0b9d0b9c0b9b0b5c0b5d0b" + new string('0', 36 * 2) + "7c080000");
        host.Send(0x148, entry);
        var back = host.WaitFor(0x169);                                     // run 8: 0x169 x19 before the 0x148 answer
        CollectionAssert.AreEqual(inside, back.Payload);
        host.WaitFor(0x148);
    }

    [TestMethod]
    public void SectorMessageGoesToPlayersInTheSameSector()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        using var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        var entry = Convert.FromHexString("5d0b00001f0b200b210b5e0b9d0b9c0b9b0b5c0b5d0b" + new string('0', 36 * 2) + "7c080000");
        host.Send(0x148, entry); host.WaitFor(0x148);
        joiner.Send(0x148, entry); joiner.WaitFor(0x148);
        var message = new byte[12]; message[0] = 0x20;
        host.Send(0x14C, message);
        CollectionAssert.AreEqual(message, joiner.WaitFor(0x14C).Payload);
    }

    [TestMethod]
    public void ShortStatesAreStreamedWithTheirEmbeddedSector()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        host.WaitFor(0x153);
        // run 8, ref 0x1A3: [ref][type] FEEDF00D [ref][type][flags 0x201][sector 0x0B1F][x][y] ...
        var upload = Convert.FromHexString("a3010000811300000df0edfea30100008113000001020000" + "1f0b" + "0e990200" + "e94b0200" + "aabbccdd");
        host.Send(0x16E, upload);
        var entry = Convert.FromHexString("5d0b00001f0b200b210b5e0b9d0b9c0b9b0b5c0b5d0b" + new string('0', 36 * 2) + "7c080000");
        host.Send(0x148, entry);
        var back = host.WaitFor(0x169).Payload;
        Assert.AreEqual(upload.Length + 16, back.Length);
        Assert.AreEqual("a3010000811300000e990200e94b0200000000001f0b00000df0edfea3010000811300000100400" + "01f0b0e990200e94b0200aabbccdd",
            Convert.ToHexString(back).ToLowerInvariant());
    }

    [TestMethod]
    public void AdmissionSendsOneEnvironment()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        var count = 0;
        while (host.TryReceive(400) is { } m) if (m.Opcode == 0x00B) count++;
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void HostMayRenameTheLanSessionOthersAreRefused()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        using var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        joiner.Send(0x022, System.Text.Encoding.Unicode.GetBytes("Mine\0"));
        Assert.AreEqual(0x64u, U32(joiner.WaitFor(0x009).Payload, 0));
        host.Send(0x022, System.Text.Encoding.Unicode.GetBytes("Renamed\0"));
        Assert.IsTrue(host.Silent(0x009));
        Assert.AreEqual("Renamed", server.Host.SessionName);
    }
}
