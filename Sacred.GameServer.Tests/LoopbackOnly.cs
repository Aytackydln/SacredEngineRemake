using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Sacred.GameServer.Config;
using Sacred.GameServer.Net;
using Sacred.GameServer.Net.Sbt;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>
/// Tests never open a wildcard socket (a "0.0.0.0" or "[::]" listener makes Windows Firewall ask again for every new test host):
/// every listener that is not told an address binds loopback, and a test that really needs the product default says so.
/// </summary>
internal static class LoopbackOnly
{
    [ModuleInitializer]
    internal static void Install() => BindPolicy.Default = IPAddress.Loopback;
}

[TestClass]
public class LoopbackOnlyTests
{
    [TestMethod]
    public void EveryListenerOfATestHostIsBoundToLoopback()
    {
        var host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 }, new AdmissionRules(null), null);
        try
        {
            var udp = host.AddListener(inbox => new SbtListener(0, inbox, () => host.TcpPort));
            Assert.IsTrue(host.Listeners.Count >= 2);
            foreach (var listener in host.Listeners)
            {
                var local = listener.LocalEndPoint;
                Assert.IsNotNull(local, listener.Transport);
                Assert.IsTrue(IPAddress.IsLoopback(local.Address), $"{listener.Transport} listens on {local}");
                Assert.IsFalse(local.Address.Equals(IPAddress.Any) || local.Address.Equals(IPAddress.IPv6Any));
            }
            Assert.AreEqual(AddressFamily.InterNetwork, host.Listeners[0].LocalEndPoint!.AddressFamily, "no dual-stack socket on an IPv4 loopback bind");
            Assert.IsNotNull(udp);
        }
        finally { host.Dispose(); }
    }

    [TestMethod]
    public void TheBindOptionIsParsedValidatedAndHonoured()
    {
        Assert.IsNull(CommandLine.Validate(["--bind=127.0.0.1"]));
        Assert.IsNull(CommandLine.Validate(["--network-bind=::1"]));
        StringAssert.Contains(CommandLine.Validate(["--bind=localhost"])!, "needs an IP address");
        StringAssert.Contains(CommandLine.Validate(["--bind"])!, "needs an IP address");
        Assert.AreEqual(IPAddress.Loopback, GameServerConfig.Load(["--bind=127.0.0.1"]).BindAddress);
        Assert.IsNull(GameServerConfig.Load([]).BindAddress, "the default is every interface");
        var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, BindAddress = IPAddress.Loopback };
        var host = new GameServerHost(config, new AdmissionRules(null), null);
        try { Assert.AreEqual(IPAddress.Loopback, host.Listeners[0].LocalEndPoint!.Address); }
        finally { host.Dispose(); }
    }
}
