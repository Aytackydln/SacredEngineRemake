using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Runtime;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class ListenPortIntegrationTests
{
    public static IEnumerable<object[]> Values() => new[] { "", "0", "-1", "2147483648", "4294967296", "2005tail", "\0", "  +70000", "abc" }
        .Select(value => new object[] { value });

    [TestMethod, DynamicData(nameof(Values), DynamicDataSourceType.Method)]
    public void SettingsGetterFeedsCurrentControllerAndOpenWithoutNormalization(string input)
    {
        using var evidence = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-listen-port.json")));
        var definition = SacredListenPortDefinitions.Read(ListenPortChecks.Image(evidence.RootElement));
        var values = new SacredSettingsValues();
        if (input.Length != 0) values.Write(definition.ReadKey, Encoding.Latin1.GetBytes(input));
        var writes = 0;
        var services = new WorldListenPortServices(key => values.Read(key), (key, value) =>
        {
            writes++;
            values.Write(key, Encoding.ASCII.GetBytes(unchecked((int)value).ToString(CultureInfo.InvariantCulture)));
        }, value => SacredAnsiDecimalParser.Parse(value.Span, definition.DefaultClasses.Span));
        using var opened = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-connection-open.json")));
        var row = opened.RootElement.GetProperty("cases").EnumerateArray().First(value => value.GetProperty("failure").GetString() == "success" &&
            value.GetProperty("mode").GetByte() == 0 && value.GetProperty("count").GetInt32() == 0 && value.GetProperty("active").GetByte() == 0 && value.GetProperty("mutation").GetString() == "none");
        var fixture = new ConnectionOpenFixture(row); var bytes = new byte[0x50];
        var controller = new WorldControlMemory(at => bytes[at], (at, value) => bytes[at] = value,
            at => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)at)),
            (at, value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)at), value),
            (at, value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((int)at), value));
        var expected = input.Length == 0 ? definition.DefaultReturnValue : unchecked((uint)SacredAnsiDecimalParser.Parse(Encoding.Latin1.GetBytes(input), definition.DefaultClasses.Span));
        WorldListenPort.LoadController(controller, definition, services, value => Assert.AreEqual(expected, value));
        Assert.AreEqual(expected, controller.ReadWord(0x40));
        Assert.AreEqual(input.Length == 0 ? 1 : 0, writes);
        Assert.AreEqual((byte)1, WorldConnectionOpen.Open(fixture.Services, 0, controller.ReadWord(0x40), 0));
        var bind = fixture.Events.First(value => (string)value[0] == "bind");
        var address = Convert.FromHexString((string)bind[2]);
        Assert.AreEqual(unchecked((ushort)expected), BinaryPrimitives.ReadUInt16BigEndian(address.AsSpan(2)));
        var sdk = fixture.Events.First(value => (string)value[0] == "primary" && (uint)value[1] == 8);
        Assert.AreEqual(expected, (uint)sdk[4]);
    }

    [TestMethod]
    public void SettingsContentOwnsBytesKeepsCaseAndDoesNotInsertMissingKeys()
    {
        var values = new SacredSettingsValues(); var key = Encoding.ASCII.GetBytes("A"); var value = new byte[] { 1, 0, 2 };
        values.Write(key, value); key[0] = (byte)'B'; value[0] = 9;
        Assert.AreEqual(0, values.Read(Encoding.ASCII.GetBytes("a")).Length);
        Assert.AreEqual(1, values.Count);
        var first = values.Read(Encoding.ASCII.GetBytes("A")); CollectionAssert.AreEqual(new byte[] { 1, 0, 2 }, first);
        first[0] = 8; Assert.AreEqual((byte)1, values.Read(Encoding.ASCII.GetBytes("A"))[0]);
        Assert.IsTrue(values.Remove(Encoding.ASCII.GetBytes("A"))); Assert.AreEqual(0, values.Count);
    }

    [TestMethod]
    public void DefaultCallbackDoesNotTriggerAnotherReadOrChangeLiteralReturn()
    {
        using var evidence = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-listen-port.json")));
        var definition = SacredListenPortDefinitions.Read(ListenPortChecks.Image(evidence.RootElement)); var reads = 0;
        var result = WorldListenPort.Read(definition, new(_ => { reads++; return ReadOnlyMemory<byte>.Empty; },
            (_, _) => { }, _ => throw new Exception("Missing data must not be parsed.")));
        Assert.AreEqual(1, reads); Assert.AreEqual(definition.DefaultReturnValue, result);
    }

    [TestMethod]
    public void CurrentControllerPortCanChangeDuringLogging()
    {
        using var evidence = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-listen-port.json")));
        var definition = SacredListenPortDefinitions.Read(ListenPortChecks.Image(evidence.RootElement)); uint port = 0;
        var controller = new WorldControlMemory(_ => 0, (_, _) => { }, _ => port, (_, value) => port = value, (_, _) => { });
        WorldListenPort.LoadController(controller, definition, new(_ => Encoding.ASCII.GetBytes("2005"), (_, _) => { },
            value => SacredAnsiDecimalParser.Parse(value.Span, definition.DefaultClasses.Span)), value =>
        {
            Assert.AreEqual(value, port); port = 17;
        });
        Assert.AreEqual(17u, port);
    }
}
