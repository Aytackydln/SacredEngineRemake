using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameServer.Config;

namespace Sacred.GameServer.Tests;

[TestClass]
public sealed class ConfigTests
{
    [TestMethod]
    public void DefaultsMatchGameServerExe()
    {
        var config = GameServerConfig.Load([]);
        Assert.AreEqual(2006, config.TcpPort);
        Assert.AreEqual(2005, config.ListenPort);
        Assert.AreEqual(0x20u, config.GameFlags()); // free game, as FUN_004eda60 starts it
    }

    [TestMethod]
    public void CfgLinesUseColonsAndModesReplaceTheFreeBit()
    {
        var config = new GameServerConfig();
        config.ApplyLine("NETWORK_PORT_TCP:2106");
        config.ApplyLine("NETWORK_PRECONFIG_MODE : 3");
        config.ApplyLine("NETWORK_PRECONFIG_LEVEL=2");
        config.ApplyLine("network-password=a:b");
        Assert.AreEqual(2106, config.TcpPort);
        Assert.AreEqual("a:b", config.Password); // the first separator wins
        Assert.AreEqual(0x10u | 0x200u | 0x2u, config.GameFlags());
    }
}
