using System.Buffers.Binary;
using System.Text.Json;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Tests;

public partial class NetOpsTests
{
    private static string AchievementsSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples", "achievements.lua");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("achievements.lua");
    }

    private static JsonElement NextJson(RawClient c, string op)
    {
        for (var i = 0; i < 20; i++)
        {
            var (_, json, data) = NextMessage(c);
            Assert.IsTrue(json, "the mod sends JSON tables");
            var root = JsonDocument.Parse(data).RootElement.Clone();
            if (root.GetProperty("op").GetString() == op) return root;
        }
        throw new AssertFailedException("no message with op " + op);
    }

    [TestMethod]
    public void AchievementsModTalksToAGearClientOverItsChannel()
    {
        using var server = new NetServer(AchievementsSource());
        using var host = RawClient.Admit(server.Port, "Host", 1);
        Handshake(host);
        var word = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(word, 0x1964);
        host.Send(0x016, default); host.Send(0x018, word); host.WaitFor(0x018);
        // Entering the world pays the first day of the streak and tells the client.
        var streak = NextJson(host, "streak");
        Assert.AreEqual(1, streak.GetProperty("streak").GetInt32());
        Assert.AreEqual("100 gold", streak.GetProperty("reward").GetString());
        var state = NextJson(host, "state");
        Assert.IsTrue(state.GetProperty("claimed").GetBoolean());
        Assert.IsTrue(state.GetProperty("ach").GetArrayLength() >= 25);
        // A kill unlocks the first achievement: a toast message, then the new state.
        var death = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(death, 9001); BinaryPrimitives.WriteUInt32LittleEndian(death.AsSpan(4), host.Slot);
        host.Send(0x17D, death);
        var unlock = NextJson(host, "unlock");
        Assert.AreEqual("kills_1", unlock.GetProperty("id").GetString());
        Assert.AreEqual("First Blood", unlock.GetProperty("name").GetString());
        // The client's request is answered with the same table.
        SendData(host, "achievements:sync", NetOpsWire.TargetServer, "{\"op\":\"get\"}"u8, 1, JsonFlags[0]);
        var again = NextJson(host, "state");
        var first = again.GetProperty("ach").EnumerateArray().First(e => e.GetProperty("id").GetString() == "kills_1");
        Assert.IsTrue(first.TryGetProperty("done", out _));
        Assert.AreEqual(0, server.Host.Scripts!.Errors);
    }
}
