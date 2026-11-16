using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class ConnectionLifecycleTests
{
    public static IEnumerable<object[]> Cases() => from failure in new[] { "success", "bind", "primary-init", "rate-null" }
        from mode in new byte[] { 0, 1, 0xa5 } select new object[] { failure, mode };

    [TestMethod, DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
    public void ActualCloseComposesWithReopenAndFailedOpen(string failure, byte mode)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-connection-open.json")));
        var row = native.RootElement.GetProperty("cases").EnumerateArray().First(value => value.GetProperty("failure").GetString() == failure &&
            value.GetProperty("mode").GetByte() == mode && value.GetProperty("count").GetInt32() == 2 && value.GetProperty("active").GetByte() == 1 &&
            value.GetProperty("mutation").GetString() == "none");
        var fixture = new ConnectionOpenFixture(row);
        fixture.Memory.WriteWord(0x18, 1); fixture.Memory.WriteWord(0x24, 0x6080000); fixture.Memory.WriteWord(0x14, 55);
        var close = fixture.CreateCloseServices(); var closed = 0;
        var services = fixture.Services with { Close = () => { closed++; WorldConnectionClose.Close(close); } };
        var result = WorldConnectionOpen.Open(services, 9, 2006, mode);
        Assert.AreEqual(failure == "success" ? (byte)1 : (byte)0, result);
        var initialized = WorldAdmissionControllerSetup.Initialize(WorldAdmissionControllerSetup.Construct(0), mode, result, fixture.Memory.ReadWord(0x48));
        Assert.AreEqual(result, initialized.MultiplayerByte);
        Assert.AreEqual(result, initialized.Gates.ActiveByte0);
        Assert.AreEqual(failure == "bind" ? 2 : 1, closed);
        Assert.AreEqual(0u, (fixture.Memory.ReadWord(0x40450) - fixture.Memory.ReadWord(0x4044c)) / 52);
        if (result != 0)
        {
            Assert.AreEqual(mode, fixture.Memory.ReadByte(0x20));
            Assert.AreEqual(mode == 0 ? 0u : 0x6083000u, fixture.Backend);
        }
        WorldConnectionClose.Close(close);
        Assert.AreEqual((byte)0, fixture.Memory.ReadByte(0x2a));
        Assert.AreEqual(uint.MaxValue, fixture.Memory.ReadWord(0x14));
        Assert.AreEqual(0u, fixture.Memory.ReadWord(0x24));
        Assert.AreEqual(0u, fixture.Backend);
        Assert.AreEqual(2, fixture.Events.Count(value => (string)value[0] == "destroy-record"));
    }

    [TestMethod, DataRow(false), DataRow(true)]
    public void NativeFaultingTransportPathsCannotInventSuccess(bool removedDuringCallback)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-connection-open.json")));
        var row = native.RootElement.GetProperty("cases").EnumerateArray().First(value => value.GetProperty("failure").GetString() == "success" &&
            value.GetProperty("mode").GetByte() == 1 && value.GetProperty("count").GetInt32() == 0 && value.GetProperty("active").GetByte() == 0 &&
            value.GetProperty("mutation").GetString() == "none");
        var fixture = new ConnectionOpenFixture(row);
        var transport = fixture.Services.Transport;
        transport = removedDuringCallback
            ? transport with { InitializeLogging = _ => { fixture.Memory.WriteWord(0x24, 0); return true; } }
            : transport with { Create = () => 0 };
        Assert.ThrowsException<NotSupportedException>(() => WorldConnectionOpen.Open(fixture.Services with { Transport = transport }, 0, 0, 1));
        Assert.AreEqual((byte)0, fixture.Memory.ReadByte(0x2a));
        Assert.AreEqual(0u, fixture.Memory.ReadWord(0x24));
    }
}
