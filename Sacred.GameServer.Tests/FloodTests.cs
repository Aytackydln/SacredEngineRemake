using System.Buffers.Binary;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

[TestClass]
public class FloodTests
{
    private sealed class Server : IDisposable
    {
        public readonly GameServerHost Host;
        private readonly Thread _thread;
        public Server(Action<GameServerConfig> configure)
        {
            var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 };
            configure(config);
            Host = new GameServerHost(config, new AdmissionRules(null), null);
            _thread = new Thread(Host.Run) { IsBackground = true };
            _thread.Start();
        }
        public int Port => Host.TcpPort;
        public void Dispose() { Host.Stop(); _thread.Join(); Host.Dispose(); }
    }

    private static byte[] Word(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); return b; }

    private static (RawClient Host, RawClient Joiner) Started(Server server)
    {
        var host = RawClient.Admit(server.Port, "Host", 1);
        host.Send(0x016, default); host.Send(0x018, Word(0x1964)); host.WaitFor(0x018);
        var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        joiner.Send(0x015, default); joiner.Send(0x017, default); joiner.WaitFor(0x017);
        return (host, joiner);
    }

    private static int Count(RawClient client, ushort opcode, int windowMs = 600)
    {
        var count = 0;
        var deadline = Environment.TickCount64 + windowMs;
        while (Environment.TickCount64 < deadline)
            if (client.TryReceive((int)Math.Max(1, deadline - Environment.TickCount64)) is { } m && m.Opcode == opcode) count++;
        return count;
    }

    [TestMethod]
    public void BucketRefillsAtTheRateAndCapsAtTheBurst()
    {
        var bucket = new RateBucket();
        for (var i = 0; i < 5; i++) Assert.IsTrue(bucket.TryTake(1000, 1, 10, 5));
        Assert.IsFalse(bucket.TryTake(1000, 1, 10, 5));
        Assert.IsTrue(bucket.TryTake(1100, 1, 10, 5));          // 100 ms at 10/s = one token
        Assert.IsFalse(bucket.TryTake(1100, 1, 10, 5));
        Assert.IsTrue(bucket.TryTake(100_000, 5, 10, 5));       // a long pause refills only up to the burst
        Assert.IsFalse(bucket.TryTake(100_000, 1, 10, 5));
    }

    [TestMethod]
    public void LoweredBurstAppliesAtOnce()
    {
        var bucket = new RateBucket();
        Assert.IsTrue(bucket.TryTake(0, 1, 10, 100));            // 99 left
        Assert.IsTrue(bucket.TryTake(0, 1, 10, 3));              // capped to 3, one taken
        Assert.IsTrue(bucket.TryTake(0, 1, 10, 3));
        Assert.IsTrue(bucket.TryTake(0, 1, 10, 3));
        Assert.IsFalse(bucket.TryTake(0, 1, 10, 3));
    }

    [TestMethod]
    public void ChatIsMutedOthersStrikeAndKick()
    {
        var options = new FloodOptions { MessagesPerSecond = 1, MessageBurst = 3, ChatPerSecond = 1, ChatBurst = 1, KickAfterDrops = 3 };   // the chat line also uses a message token
        var state = new FloodState();
        Assert.AreEqual(FloodGuard.Verdict.Allow, FloodGuard.Message(ref state, 0x005, false, 0, options));
        for (var i = 0; i < 10; i++) Assert.AreEqual(FloodGuard.Verdict.Drop, FloodGuard.Message(ref state, 0x005, false, 0, options));
        Assert.AreEqual(0, state.Strikes, "chat spam does not count towards the kick");
        Assert.AreEqual(FloodGuard.Verdict.Allow, FloodGuard.Message(ref state, 0x1B0, false, 0, options));
        Assert.AreEqual(FloodGuard.Verdict.Allow, FloodGuard.Message(ref state, 0x1B0, false, 0, options));
        Assert.AreEqual(FloodGuard.Verdict.Drop, FloodGuard.Message(ref state, 0x1B0, false, 0, options));
        Assert.AreEqual(FloodGuard.Verdict.Drop, FloodGuard.Message(ref state, 0x1B0, false, 0, options));
        Assert.AreEqual(FloodGuard.Verdict.Kick, FloodGuard.Message(ref state, 0x1B0, false, 0, options));
        // Strikes expire with the window.
        var later = new FloodState();
        for (var i = 0; i < 3; i++) FloodGuard.Message(ref later, 0x1B0, false, 0, options);
        Assert.AreEqual(FloodGuard.Verdict.Drop, FloodGuard.Message(ref later, 0x1B0, false, 0, options));
        Assert.AreEqual(FloodGuard.Verdict.Drop, FloodGuard.Message(ref later, 0x1B0, false, 1, options));
        Assert.AreEqual(2, later.Strikes);
        for (var i = 0; i < 3; i++) Assert.AreEqual(FloodGuard.Verdict.Allow, FloodGuard.Message(ref later, 0x1B0, false, 20_000, options));
        Assert.AreEqual(FloodGuard.Verdict.Drop, FloodGuard.Message(ref later, 0x1B0, false, 20_000, options));
        Assert.AreEqual(1, later.Strikes, "a new window starts the count again");
    }

    [TestMethod]
    public void UploadsAreFreeOnlyDuringTheGrace()
    {
        var options = new FloodOptions { MessagesPerSecond = 1, MessageBurst = 1, UploadGraceSeconds = 60, KickAfterDrops = 0 };
        var state = new FloodState { AdmittedMs = 1000 };
        Assert.IsTrue(FloodGuard.InUploadGrace(state, inGame: false, 2000, options));
        for (var i = 0; i < 1000; i++) Assert.AreEqual(FloodGuard.Verdict.Allow, FloodGuard.Message(ref state, 0x16E, true, 2000, options));
        Assert.AreEqual(FloodGuard.Verdict.Allow, FloodGuard.Message(ref state, 0x1B0, true, 2000, options));   // not an upload
        Assert.AreEqual(FloodGuard.Verdict.Drop, FloodGuard.Message(ref state, 0x1B0, true, 2000, options));
        Assert.IsFalse(FloodGuard.InUploadGrace(state, inGame: true, 2000, options));
        Assert.IsFalse(FloodGuard.InUploadGrace(state, inGame: false, 62_000, options));
    }

    [TestMethod]
    public void ExcessMessagesAreDroppedAndTheFlooderIsRemoved()
    {
        using var server = new Server(c => { c.Flood.Enabled = true; c.Flood.MessagesPerSecond = 20; c.Flood.MessageBurst = 20; c.Flood.KickAfterDrops = 50; });
        var (host, joiner) = Started(server);
        using (host) using (joiner)
        {
            for (var i = 0; i < 40; i++) joiner.Send(0x1B0, Word((uint)i));
            var forwarded = Count(host, 0x1B0);
            Assert.IsTrue(forwarded is >= 10 and < 40, $"forwarded {forwarded} of 40");
            Assert.IsTrue(server.Host.FloodDrops > 0);
            for (var i = 0; i < 200; i++) joiner.Send(0x1B0, Word((uint)i));
            joiner.WaitFor(0x157);                                            // kicked
            host.WaitFor(0x150);
        }
    }

    [TestMethod]
    public void ChatSpamIsMutedNotKicked()
    {
        using var server = new Server(c => { c.Flood.Enabled = true; c.Flood.ChatPerSecond = 1; c.Flood.ChatBurst = 3; c.Flood.KickAfterDrops = 5; });
        var (host, joiner) = Started(server);
        using (host) using (joiner)
        {
            var line = new byte[4 + 6];
            "h\0i\0"u8.CopyTo(line.AsSpan(4));
            for (var i = 0; i < 30; i++) joiner.Send(0x005, line);
            Assert.AreEqual(3, Count(host, 0x005));
            Assert.IsTrue(joiner.Silent(0x157), "a chat spammer is muted, not removed");
        }
    }

    [TestMethod]
    public void RelayModeHasNoLimitsByDefault()
    {
        using var server = new Server(c => { c.Flood.MessagesPerSecond = 1; c.Flood.MessageBurst = 1; });
        var (host, joiner) = Started(server);
        using (host) using (joiner)
        {
            for (var i = 0; i < 30; i++) joiner.Send(0x1B0, Word((uint)i));
            Assert.AreEqual(30, Count(host, 0x1B0, 1000));
            Assert.AreEqual(0, server.Host.FloodDrops);
        }
    }

    [TestMethod]
    public void AuthoritativeModeTurnsItOnAndKeysParse()
    {
        var config = new GameServerConfig();
        Assert.IsFalse(config.Flood.IsEnabled(config.Auth.Enabled));
        config.ApplyLine("authoritative");
        Assert.IsTrue(config.Flood.IsEnabled(config.Auth.Enabled));
        config.ApplyLine("flood-protection=0");
        Assert.IsFalse(config.Flood.IsEnabled(config.Auth.Enabled));
        config.ApplyLine("FLOOD_CHAT_PER_SECOND:7");
        config.ApplyLine("max-connections-per-address=2");
        Assert.AreEqual(7, config.Flood.ChatPerSecond);
        Assert.AreEqual(2, config.Flood.MaxConnectionsPerAddress);
    }

    [TestMethod]
    public void ConnectionsPerAddressAreLimited()
    {
        using var server = new Server(c => { c.Flood.Enabled = true; c.Flood.MaxConnectionsPerAddress = 2; });
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        Exception? refused = null;
        try { using var c = RawClient.Admit(server.Port, "C", 3); }
        catch (Exception e) { refused = e; }
        Assert.IsNotNull(refused, "the third connection from 127.0.0.1 is closed");
    }

    [TestMethod]
    public void FloodCheckDoesNotAllocate()
    {
        var options = new FloodOptions();
        var state = new FloodState { AdmittedMs = 1 };
        FloodGuard.Message(ref state, 0x1B0, false, 0, options);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100_000; i++)
        {
            FloodGuard.Frame(ref state, 200, false, i, options);
            FloodGuard.Message(ref state, (ushort)(i % 2 == 0 ? 0x005 : 0x1B0), FloodGuard.InUploadGrace(state, false, i, options), i, options);
        }
        Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
