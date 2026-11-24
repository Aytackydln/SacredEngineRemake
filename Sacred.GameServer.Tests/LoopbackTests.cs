using System.Net;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;
using Sacred.Network.Admission;
using Sacred.Network.Connection;
using Sacred.Network.Protocol;

namespace Sacred.GameServer.Tests;

/// <summary>Drives the server with the remake's own native-compatible client (Sacred.Network.GoldGameConnection).</summary>
[TestClass]
public class LoopbackTests
{
    private static GoldFirstContact FirstContact(string name, ushort version = 0, int identity = 1)
    {
        var payload = new byte[GoldFirstContactLayout.Size];
        BitConverter.TryWriteBytes(payload, identity);
        BitConverter.TryWriteBytes(payload.AsSpan(GoldFirstContactLayout.VersionOffset), version);
        System.Text.Encoding.Unicode.GetBytes(name, payload.AsSpan(GoldFirstContactLayout.NameOffset, GoldFirstContactLayout.NameBytes));
        return GoldFirstContact.FromPayload(payload);
    }

    private static byte[] ChatPayload(string text, int recipient = -1)
    {
        var payload = new byte[264 + text.Length * 2 + 2];
        BitConverter.TryWriteBytes(payload.AsSpan(4), recipient);
        System.Text.Encoding.Unicode.GetBytes(text, payload.AsSpan(264));
        return payload;
    }

    private sealed class Running : IDisposable
    {
        public readonly GameServerHost Host;
        private readonly Thread _thread;
        public Running(GameServerConfig config, AdmissionRules? rules = null)
        {
            Host = new GameServerHost(config, rules ?? new AdmissionRules(null), null);
            _thread = new Thread(Host.Run) { IsBackground = true };
            _thread.Start();
        }
        public void Dispose() { Host.Stop(); _thread.Join(); Host.Dispose(); }
    }

    private static async Task<GoldGameConnection> Connect(int port, GoldFirstContact contact)
    {
        var client = new GoldGameConnection();
        client.Start("127.0.0.1", port, contact);
        for (var i = 0; i < 200; i++)
        {
            if (client.Admission.Phase is GoldAdmissionPhase.SlotAssigned or GoldAdmissionPhase.Rejected) break;
            await Task.Delay(25);
        }
        return client;
    }

    private static GameServerConfig Config() => new() { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 };

    [TestMethod]
    public async Task ClientsAreAdmittedWithSlotsAndHostRole()
    {
        using var server = new Running(Config());
        await using var first = await Connect(server.Host.TcpPort, FirstContact("Seraphim", identity: 11));
        await using var second = await Connect(server.Host.TcpPort, FirstContact("Gladiator", identity: 12));
        Assert.AreEqual(GoldAdmissionPhase.SlotAssigned, first.Admission.Phase);
        Assert.AreEqual((byte)1, first.Admission.PlayerSlot);
        Assert.AreEqual(1, first.Admission.Flags & 1);   // first player is the client host
        Assert.AreEqual((byte)2, second.Admission.PlayerSlot);
        Assert.AreEqual(0, second.Admission.Flags & 1);
        Assert.AreEqual(2, server.Host.PlayerCount);
    }

    [TestMethod]
    public async Task DuplicateLobbyIdentityIsAllowedWithoutLobby()
    {
        // FUN_004eb0a0 only checks identities while the lobby is connected (see LobbyTests for the rejection).
        using var server = new Running(Config());
        await using var first = await Connect(server.Host.TcpPort, FirstContact("A", identity: 5));
        await using var second = await Connect(server.Host.TcpPort, FirstContact("B", identity: 5));
        Assert.AreEqual(GoldAdmissionPhase.SlotAssigned, second.Admission.Phase);
    }

    [TestMethod]
    public async Task VersionMismatchIsRejectedWhenDataIsVerified()
    {
        var expected = new GoldLanCompatibility(0x1234, new GoldDataFingerprints(new uint[GoldDataFingerprints.Count]), 0, 0, 0);
        using var server = new Running(Config(), new AdmissionRules(expected));
        await using var client = await Connect(server.Host.TcpPort, FirstContact("A", version: 0x4321));
        Assert.AreEqual(GoldAdmissionPhase.Rejected, client.Admission.Phase);
        Assert.AreEqual(RejectionReason.VersionMismatch, client.Admission.RejectionReason);
    }

    [TestMethod]
    public async Task DataFingerprintMismatchReportsMask()
    {
        var values = new uint[GoldDataFingerprints.Count];
        Array.Fill(values, 7u);
        var expected = new GoldLanCompatibility(0x1234, new GoldDataFingerprints(values), 0, 0, 0);
        using var server = new Running(Config(), new AdmissionRules(expected));
        await using var client = await Connect(server.Host.TcpPort, FirstContact("A", version: 0x1234));
        Assert.AreEqual(GoldAdmissionPhase.Rejected, client.Admission.Phase);
        Assert.AreEqual(RejectionReason.ChecksumOrData, client.Admission.RejectionReason);
        Assert.AreEqual(0x1FFFFFFu, client.Admission.DataMismatchMask & 0x1FFFFFFu);
    }

    [TestMethod]
    public async Task PasswordIsCheckedAndWrongTriesAreLimited()
    {
        var config = Config();
        config.Password = "secret"; config.MaxPasswordTries = 2;
        using var server = new Running(config);
        await using var client = await Connect(server.Host.TcpPort, FirstContact("A"));
        Assert.AreEqual(GoldAdmissionPhase.SlotAssigned, client.Admission.Phase);
        await client.SendPasswordAsync("wrong");
        for (var i = 0; i < 100 && client.Admission.Password == GoldPasswordStatus.WaitingForResponse; i++) await Task.Delay(20);
        Assert.AreEqual(GoldPasswordStatus.Rejected, client.Admission.Password);
        await client.SendPasswordAsync("secret");
        for (var i = 0; i < 100 && client.Admission.Password == GoldPasswordStatus.WaitingForResponse; i++) await Task.Delay(20);
        Assert.AreEqual(GoldPasswordStatus.Accepted, client.Admission.Password);
    }

    [TestMethod]
    public async Task ChatIsRelayedToOtherPlayersWithRetainedOrigin()
    {
        using var server = new Running(Config());
        await using var first = await Connect(server.Host.TcpPort, FirstContact("A", identity: 1));
        await using var second = await Connect(server.Host.TcpPort, FirstContact("B", identity: 2));
        var text = new byte[4 + 12];
        System.Text.Encoding.Unicode.GetBytes("hello", text.AsSpan(4));
        await first.SendAsync(new GoldMessage(0x005, 1, 1, 0, text));
        GoldMessage? chat = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (chat is null)
        {
            var batch = await second.Events.ReadAsync(timeout.Token);
            if (batch is GoldMessagesReceived received) chat = received.Messages.FirstOrDefault(m => m.Opcode == 0x005);
        }
        Assert.AreEqual((byte)0, chat.Sender);
        Assert.AreEqual((byte)1, chat.Origin);
        var decoded = Sacred.Network.Gameplay.GoldChannelChatCodec.Decode(chat);
        Assert.AreEqual("hello", decoded.Text);
        Assert.AreEqual(Sacred.Network.Gameplay.GoldChatChannel.Public, decoded.Channel);
    }

    [TestMethod]
    public async Task PacketLoopDoesNotAllocate()
    {
        var host = new GameServerHost(Config(), new AdmissionRules(null), null);
        try
        {
            await using var first = new GoldGameConnection();
            await using var second = new GoldGameConnection();
            first.Start("127.0.0.1", host.TcpPort, FirstContact("A", identity: 1));
            for (var i = 0; i < 400 && first.Admission.Phase != GoldAdmissionPhase.SlotAssigned; i++) { host.RunTick(); await Task.Delay(5); }
            second.Start("127.0.0.1", host.TcpPort, FirstContact("B", identity: 2));
            host.Start();
            // The test thread single-steps the server; the clients run on their own threads.
            var hostThread = Thread.CurrentThread;
            for (var i = 0; i < 400 && (first.Admission.Phase != GoldAdmissionPhase.SlotAssigned || second.Admission.Phase != GoldAdmissionPhase.SlotAssigned); i++)
            {
                host.RunTick();
                await Task.Delay(5);
            }
            Assert.AreEqual(GoldAdmissionPhase.SlotAssigned, first.Admission.Phase);
            var payload = new byte[4 + 2 * 120 + 2];
            for (var i = 0; i < 120; i++) payload[4 + i * 2] = (byte)('a' + (i * 7) % 26); // long line: deflate path
            var small = new byte[4 + 6];
            "h\0i\0"u8.CopyTo(small.AsSpan(4));                  // short line: raw path
            long allocated = 0;
            for (var round = 0; round < 300; round++)
            {
                await first.SendAsync(new GoldMessage(0x005, first.Admission.PlayerSlot, first.Admission.PlayerSlot, 0, round % 2 == 0 ? payload : small));
                await Task.Delay(2);
                var before = GC.GetAllocatedBytesForCurrentThread();
                host.RunTick();
                var after = GC.GetAllocatedBytesForCurrentThread();
                if (round >= 50) allocated += after - before; // 50 rounds of warm-up (pool, JIT, tables)
            }
            Console.WriteLine($"server-thread bytes allocated over 250 measured ticks: {allocated}");
            Assert.IsTrue(allocated < 2048, $"packet loop allocated {allocated} bytes");
        }
        finally { host.Dispose(); }
    }
}
