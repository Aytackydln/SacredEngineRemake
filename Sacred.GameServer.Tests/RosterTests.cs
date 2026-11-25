using System.Buffers.Binary;
using System.Text;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>Roster 0x1B (FUN_004d3030) and the admission follow-ups the original queues (internal 0x101 and 0x41).</summary>
[TestClass]
public class RosterTests
{
    private sealed class Server : IDisposable
    {
        public readonly GameServerHost Host;
        private readonly Thread _thread;
        public Server(Action<GameServerConfig>? configure = null)
        {
            var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5, ServerName = "Realm" };
            configure?.Invoke(config);
            Host = new GameServerHost(config, new AdmissionRules(null), null);
            _thread = new Thread(Host.Run) { IsBackground = true };
            _thread.Start();
        }
        public int Port => Host.TcpPort;
        public void Dispose() { Host.Stop(); _thread.Join(); Host.Dispose(); }
    }

    private static string Name(byte[] roster, int entry) =>
        Encoding.Unicode.GetString(roster.AsSpan(entry * 0x342 + 0x16, 0x100)).TrimEnd('\0');

    [TestMethod]
    public void AdmissionSendsRosterAndTimeProbe()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 7, block: 3);
        var roster = host.WaitFor(0x01B).Payload;
        Assert.AreEqual(GameServerHost.RosterBytes, roster.Length);
        Assert.AreEqual("Realm", Name(roster, 0));
        var e = host.Slot * 0x342;
        Assert.AreEqual("Host", Name(roster, host.Slot));
        Assert.AreEqual(3, roster[e + 5]);                                   // character block
        Assert.AreEqual(1, roster[e + 7]);                                   // host
        Assert.AreEqual(7, BinaryPrimitives.ReadInt32LittleEndian(roster.AsSpan(e + 0x12)));
        Assert.AreEqual(16, host.WaitFor(0x043).Payload.Length);

        using var joiner = RawClient.Admit(server.Port, "Joiner", 8);
        var both = host.WaitFor(0x01B).Payload;                              // changed roster goes to everybody
        Assert.AreEqual("Joiner", Name(both, joiner.Slot));
        Assert.AreEqual("Host", Name(joiner.WaitFor(0x01B).Payload, host.Slot));
    }

    [TestMethod]
    public void ReadyAndPlayerDataUpdateTheRoster()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 7);
        host.WaitFor(0x01B);
        host.Send(0x00F, default);
        Assert.AreEqual(1, host.WaitFor(0x01B).Payload[host.Slot * 0x342 + 6]);
        var record = new byte[0x342];
        record[4] = 2; record[5] = 9;
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0x12), 77);
        host.Send(0x01B, record);
        var roster = host.WaitFor(0x01B).Payload;
        Assert.AreEqual(2, roster[host.Slot * 0x342 + 4]);
        Assert.AreEqual(9, roster[host.Slot * 0x342 + 5]);
        Assert.AreEqual(77, BinaryPrimitives.ReadInt32LittleEndian(roster.AsSpan(host.Slot * 0x342 + 0x12)));
    }

    [TestMethod]
    public void PasswordProtectedRosterWaitsForThePassword()
    {
        using var server = new Server(c => c.Password = "pw");
        using var client = RawClient.Admit(server.Port, "Guest", 5);
        client.Send(0x012, Encoding.Unicode.GetBytes("pw"));
        client.WaitFor(0x014);
        Assert.AreEqual("Guest", Name(client.WaitFor(0x01B).Payload, client.Slot));
    }
}
