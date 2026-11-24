using Sacred.GameServer.Config;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>
/// The original dispatcher's case table (<see cref="OpcodeTable"/>) against our handling: every native opcode has a routing entry
/// of the right kind and none reaches the "unhandled opcode" path, in relay mode and in authoritative mode.
/// </summary>
[TestClass]
public class OpcodeTableTests
{
    [TestMethod]
    public void TheTableMatchesTheExecutable()
    {
        Assert.AreEqual(229, OpcodeTable.Entries.Length);
        Assert.AreEqual(OpcodeTable.Entries.Length, OpcodeTable.Entries.Select(e => e.Opcode).Distinct().Count());
        Assert.IsTrue(OpcodeTable.Entries.All(e => e.Opcode >= OpcodeTable.NativeFirst && e.Opcode <= OpcodeTable.NativeLast));
        Assert.IsTrue(OpcodeTable.IsNative(0x150) && OpcodeTable.IsNative(0x13F) && OpcodeTable.IsNative(0x198));
        Assert.IsFalse(OpcodeTable.IsNative(0x1E0), "ids above the native range belong to extensions");
        Assert.IsFalse(OpcodeTable.IsNative(0x008), "0x008 reaches the original's default case");
    }

    [TestMethod]
    public void EveryNativeOpcodeHasARoutingEntryOfItsClass()
    {
        foreach (var e in OpcodeTable.Entries)
        {
            var text = $"0x{e.Opcode:X3} {e.Class}";
            switch (e.Class)
            {
                case OpcodeClass.Forward:
                case OpcodeClass.ForwardAfterState:
                    // 0x17D forwards with a mode that depends on the payload (creature or hero), see ForwardKill.
                    Assert.IsTrue(Routing.Mode(e.Opcode) != RouteMode.None || e.Opcode == 0x17D || Routing.IsConsumed(e.Opcode), text);
                    break;
                case OpcodeClass.Consumed: Assert.IsTrue(Routing.IsConsumed(e.Opcode), text); break;
                case OpcodeClass.Internal: Assert.IsTrue(Routing.IsInternal(e.Opcode), text); break;
                case OpcodeClass.Todo: Assert.IsTrue(Routing.IsPending(e.Opcode), text); break;
                default: break;                                   // Answered, Admission and NoOp have explicit handlers
            }
        }
    }

    private static string Run(bool authoritative)
    {
        var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5, LobbyChannel = 1 };
        config.Auth.Enabled = authoritative;
        var host = new GameServerHost(config, new AdmissionRules(null), null);
        var thread = new Thread(host.Run) { IsBackground = true };
        thread.Start();
        var log = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(TextWriter.Synchronized(log));
        try
        {
            foreach (var e in OpcodeTable.Entries)
            {
                if (e.Class == OpcodeClass.Admission) continue;
                using var client = RawClient.Admit(host.TcpPort, "Probe", 1);
                var payload = new byte[96];
                client.Send(e.Opcode, payload);
                Thread.Sleep(25);
            }
            Thread.Sleep(200);
        }
        finally { Console.SetOut(previous); host.Stop(); thread.Join(); host.Dispose(); }
        return log.ToString();
    }

    [TestMethod]
    public void NoNativeOpcodeIsUnhandledInRelayMode() => Assert.IsFalse(Run(authoritative: false).Contains("unhandled opcode"), "relay mode");

    [TestMethod]
    public void NoNativeOpcodeIsUnhandledInAuthoritativeMode() => Assert.IsFalse(Run(authoritative: true).Contains("unhandled opcode"), "authoritative mode");

    [TestMethod]
    public void OpcodesAboveTheNativeRangeCanBeClaimedByExtensions()
    {
        var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 };
        var host = new GameServerHost(config, new AdmissionRules(null), null);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => host.RegisterOpcode(0x150, (PlayerSession _, in Sacred.GameServer.Wire.GoldMessageView _) => { }));
        var seen = 0;
        host.RegisterOpcode(0x1E0, (PlayerSession _, in Sacred.GameServer.Wire.GoldMessageView m) => { if (m.Payload.Length == 5) seen++; });
        var thread = new Thread(host.Run) { IsBackground = true };
        thread.Start();
        try
        {
            using var client = RawClient.Admit(host.TcpPort, "Probe", 1);
            client.Send(0x1E0, new byte[5]);
            var deadline = Environment.TickCount64 + 2000;
            while (seen == 0 && Environment.TickCount64 < deadline) Thread.Sleep(10);
            Assert.AreEqual(1, seen);
        }
        finally { host.Stop(); thread.Join(); host.Dispose(); }
    }
}
