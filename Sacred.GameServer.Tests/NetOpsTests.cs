using System.Collections.Concurrent;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;
using Sacred.GameServer.Wire;
using Sacred.Network.Admission;

namespace Sacred.GameServer.Tests;

/// <summary>Custom net opcodes (0x1E0 control / 0x1E1 data): handshake, peers, relaying, server channels and every cap.
/// Servers listen on ephemeral ports (TcpPort = 0), never the game's ports.</summary>
[TestClass]
public partial class NetOpsTests
{
    private sealed class NetServer : IDisposable
    {
        public readonly GameServerHost Host;
        public readonly ConcurrentQueue<(int Slot, string Channel, bool Json, byte[] Data)> Received = new();
        private readonly Thread _thread;
        private readonly string? _folder;

        public NetServer(Action<GameServerConfig>? configure = null, params (string Name, bool ServerOnly)[] channels) : this(null, configure, channels) { }

        public NetServer(string? script, Action<GameServerConfig>? configure = null, params (string Name, bool ServerOnly)[] channels)
        {
            var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 };
            if (script is not null)
            {
                _folder = Directory.CreateTempSubdirectory("netops-").FullName;
                Directory.CreateDirectory(Path.Combine(_folder, "scripts"));
                File.WriteAllText(Path.Combine(_folder, "scripts", "net.lua"), script);
                config.Scripts.Directory = Path.Combine(_folder, "scripts"); config.Scripts.DataDirectory = Path.Combine(_folder, "data");
            }
            configure?.Invoke(config);
            Host = new GameServerHost(config, new AdmissionRules(null), null);
            if (script is not null) Host.StartScripts();
            foreach (var (name, serverOnly) in channels) Host.RegisterNetChannel(name, serverOnly);
            Host.NetMessage += (from, channel, json, data) => Received.Enqueue((from.Slot, channel.Name, json, data));
            _thread = new Thread(Host.Run) { IsBackground = true };
            _thread.Start();
        }

        public int Port => Host.TcpPort;
        public void Dispose()
        {
            Host.Stop(); _thread.Join(); Host.Dispose();
            if (_folder is not null) try { Directory.Delete(_folder, true); } catch (IOException) { }
        }

        public bool WaitReceived(int count, int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Received.Count < count && Environment.TickCount64 < deadline) Thread.Sleep(5);
            return Received.Count >= count;
        }

        public bool WaitStat(Func<NetOpsStats, long> counter, long atLeast, int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (counter(Host.NetStats) < atLeast && Environment.TickCount64 < deadline) Thread.Sleep(5);
            return counter(Host.NetStats) >= atLeast;
        }
    }

    private static byte[] Hello(uint features = 3, uint modHash = 0xABCD1234)
    {
        var d = new byte[NetOpsWire.HelloBytes];
        NetOpsWire.WriteHello(d, new(NetOpsWire.ProtocolVersion, features, modHash));
        return d;
    }

    private static int _chatNumber;

    /// <summary>One message to the server (target 0) or a player on the chat carrier, as the Gear client frames it.</summary>
    private static void SendChat(RawClient c, uint target, byte kind, string name, ReadOnlySpan<byte> body)
    {
        var number = Interlocked.Increment(ref _chatNumber) & 0x7FFF;
        foreach (var frame in NetChatWire.Encode(NetChatWire.Join(kind, name, body), number))
        {
            var payload = new byte[4 + frame.Length + 2];
            BitConverter.TryWriteBytes(payload, target);
            frame.CopyTo(payload, 4);
            c.Send(0x003, payload);
        }
    }

    /// <summary>The next complete net chat message the server sent (from slot 0), as (kind, name, body).</summary>
    private static (byte Kind, string Name, byte[] Body) NextChat(RawClient c, int timeoutMs = 3000)
    {
        var bytes = new List<byte>(); var total = 0; var index = 0;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var m = c.WaitFor(0x003, (int)Math.Max(1, deadline - Environment.TickCount64));
            if (m.Payload.Length < 6 || !NetChatWire.TryRead(m.Payload.AsSpan(4), out var frame)) continue;
            Assert.AreEqual((byte)0, m.Origin, "channel messages of the server come from slot 0");
            if (frame.Index == 0) { bytes.Clear(); total = frame.Length; index = 0; }
            Assert.AreEqual(index++, frame.Index);
            Assert.IsTrue(NetChatWire.Unpack(frame.Packed, NetChatWire.BytesIn(total, frame.Index), bytes));
            if (bytes.Count < total) continue;
            Assert.IsTrue(NetChatWire.TrySplit(bytes.ToArray(), out var kind, out var name, out var body));
            return (kind, name, body.ToArray());
        }
        throw new AssertFailedException("no net chat message");
    }

    private static byte[] AnswerBody(ushort control, ushort data, bool accept)
    {
        var body = new byte[6];
        body[0] = NetOpsWire.ProtocolVersion;
        BitConverter.TryWriteBytes(body.AsSpan(1), control); BitConverter.TryWriteBytes(body.AsSpan(3), data);
        body[5] = (byte)(accept ? 1 : 0);
        return body;
    }

    /// <summary>The chat handshake (the answer accepts the server's numbers), then HELLO on the agreed types.</summary>
    internal static NetOpsWire.HelloAck Handshake(RawClient c, ushort control = NetOpsWire.DefaultControl)
    {
        SendChat(c, 0, NetChatWire.KindAnswer, NetChatWire.HandshakeName, AnswerBody(control, (ushort)(control + 1), true));
        c.Send(control, Hello());
        while (true)
        {
            var m = c.WaitFor(control);
            if (NetOpsWire.TryReadHelloAck(m.Payload, out var ack)) return ack;
        }
    }

    private static ushort NextPeers(RawClient c, ushort control = NetOpsWire.DefaultControl)
    {
        while (true)
        {
            var m = c.WaitFor(control);
            if (NetOpsWire.TryReadPeers(m.Payload, out var mask)) return mask;
        }
    }

    private static void SendData(RawClient c, string channel, byte target, ReadOnlySpan<byte> data, ushort id, byte flags = 3, ushort opcode = NetOpsWire.DefaultData)
    {
        var payload = new byte[NetOpsWire.DataHeaderBytes + data.Length];
        NetOpsWire.WriteData(payload, new(NetOpsWire.ChannelId(channel), flags, target, id), data);
        c.Send(opcode, payload);
    }

    /// <summary>The next complete message the server sent on the data opcode (all fragments), as (channel id, json flag, bytes).</summary>
    private static (uint Channel, bool Json, byte[] Data) NextMessage(RawClient c, ushort opcode = NetOpsWire.DefaultData)
    {
        var all = new List<byte>(); uint channel = 0; var json = false;
        while (true)
        {
            var m = c.WaitFor(opcode);
            Assert.AreEqual((byte)0, m.Origin, "messages of the server come from slot 0");
            Assert.IsTrue(NetOpsWire.TryReadData(m.Payload, out var h, out var d));
            if (h.First) { all.Clear(); channel = h.Channel; json = h.Json; }
            all.AddRange(d.ToArray());
            if (h.Last) return (channel, json, all.ToArray());
        }
    }

    private static byte[] Bytes(int length, byte seed = 1) { var b = new byte[length]; for (var i = 0; i < b.Length; i++) b[i] = (byte)(seed + i * 7); return b; }

    // ---- wire ---------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void ChannelIdIsFnv1a()
    {
        Assert.AreEqual(0x811C9DC5u, NetOpsWire.ChannelId(""));
        Assert.AreEqual(0xE40C292Cu, NetOpsWire.ChannelId("a"));
        Assert.AreEqual(0xBF9CF968u, NetOpsWire.ChannelId("foobar"));
    }

    [TestMethod]
    public void FramesRoundTrip()
    {
        var buffer = new byte[64];
        var n = NetOpsWire.WriteData(buffer, new(0xDEADBEEF, 5, 0xFF, 0x1234), [1, 2, 3]);
        Assert.IsTrue(NetOpsWire.TryReadData(buffer.AsSpan(0, n), out var header, out var data));
        Assert.AreEqual(new NetOpsWire.DataHeader(0xDEADBEEF, 5, 0xFF, 0x1234), header);
        Assert.IsTrue(header.First && !header.Last && header.Json);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, data.ToArray());
        n = NetOpsWire.WriteHelloAck(buffer, new(1, 15, 0x1E1, 4000, 262144, 65536, 8));
        Assert.AreEqual(NetOpsWire.HelloAckBytes, n);
        Assert.IsTrue(NetOpsWire.TryReadHelloAck(buffer.AsSpan(0, n), out var ack));
        Assert.AreEqual(new NetOpsWire.HelloAck(1, 15, 0x1E1, 4000, 262144, 65536, 8), ack);
        Assert.IsFalse(NetOpsWire.TryReadData(buffer.AsSpan(0, 7), out _, out _));
    }

    [TestMethod]
    public void IdsMustBeAboveTheStockRange()
    {
        Assert.IsTrue(NetOpsWire.ValidIds(0x1E0, 0x1E1));
        Assert.IsFalse(NetOpsWire.ValidIds(0x1D5, 0x1E1));
        Assert.IsFalse(NetOpsWire.ValidIds(0x1E0, 0x1E0));
        Assert.IsFalse(NetOpsWire.ValidIds(0x1E0, 0x400));
        var options = new NetOpsOptions();
        Assert.IsFalse(options.SetIds(0x100, 0x1E1));
        Assert.AreEqual((ushort)0x1E0, options.Control);
        options.Apply("NETOPS_CONTROL", "0x1F0");
        options.Apply("NETOPS_DATA", "497");
        Assert.AreEqual((ushort)0x1F0, options.Control);
        Assert.AreEqual((ushort)0x1F1, options.Data);
        options.Apply("NETOPS_DATA", "0x10");   // invalid: unchanged
        Assert.AreEqual((ushort)0x1F1, options.Data);
    }

    [TestMethod]
    public void ChannelNamesAreCheckedAtRegistration()
    {
        using var server = new NetServer();
        Assert.ThrowsException<ArgumentException>(() => server.Host.RegisterNetChannel(""));
        Assert.ThrowsException<ArgumentException>(() => server.Host.RegisterNetChannel(new string('x', 65)));
        Assert.ThrowsException<ArgumentException>(() => server.Host.RegisterNetChannel("bad\nname"));
        // find two names with the same FNV-1a id (birthday search over ~100k names)
        var seen = new Dictionary<uint, string>();
        string? first = null, second = null;
        for (var i = 0; first is null; i++)
        {
            var name = "n" + i;
            var id = NetOpsWire.ChannelId(name);
            if (seen.TryGetValue(id, out var other)) { first = other; second = name; }
            else seen[id] = name;
        }
        server.Host.RegisterNetChannel(first);
        var e = Assert.ThrowsException<ArgumentException>(() => server.Host.RegisterNetChannel(second!));
        StringAssert.Contains(e.Message, "collides");
        Assert.AreSame(server.Host.RegisterNetChannel(first), server.Host.RegisterNetChannel(first));
    }

    // ---- handshake and peers ------------------------------------------------------------------------------------------

    [TestMethod]
    public void HelloIsAnsweredWithTheServersLimits()
    {
        using var server = new NetServer(c => { c.NetOps.MaxInFlight = 5; c.NetOps.Apply("NETOPS_RATE", "20000"); });
        using var a = RawClient.Admit(server.Port, "A", 1);
        var ack = Handshake(a);
        Assert.AreEqual(NetOpsWire.ProtocolVersion, ack.Protocol);
        Assert.AreEqual(NetOpsWire.DefaultData, ack.DataOpcode);
        Assert.AreEqual((ushort)4000, ack.MaxFragment);
        Assert.AreEqual(256u * 1024, ack.MaxMessage);
        Assert.AreEqual(20000u, ack.BytesPerSecond);
        Assert.AreEqual((byte)5, ack.MaxInFlight);
        Assert.IsTrue((ack.Features & NetOpsWire.FeatureFragments) != 0 && (ack.Features & NetOpsWire.FeatureRelay) != 0);
        Assert.AreEqual((ushort)1, NextPeers(a));
    }

    [TestMethod]
    public void PeersListFollowsWhoSaidHello()
    {
        using var server = new NetServer();
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        using var c = RawClient.Admit(server.Port, "C", 3);
        Handshake(a);
        Assert.AreEqual((ushort)0b001, NextPeers(a));
        Handshake(c);
        Assert.AreEqual((ushort)0b101, NextPeers(c));
        Assert.AreEqual((ushort)0b101, NextPeers(a));
        Assert.IsTrue(b.Silent(NetOpsWire.DefaultControl), "a player that never said HELLO is never sent a custom opcode");
        Assert.IsTrue(b.Silent(NetOpsWire.DefaultData));
        Handshake(b);
        Assert.AreEqual((ushort)0b111, NextPeers(b));
        c.Dispose();                       // leaves: the others get the new list
        Assert.AreEqual((ushort)0b111, NextPeers(a));   // (the update that included B)
        Assert.AreEqual((ushort)0b011, NextPeers(a));
    }

    [TestMethod]
    public void OpcodesCanBeChangedAndAreAnnounced()
    {
        using var server = new NetServer(c => { c.ApplyLine("NETOPS_CONTROL=0x1F0"); c.ApplyLine("NETOPS_DATA=0x1F1"); });
        using var a = RawClient.Admit(server.Port, "A", 1);
        a.Send(NetOpsWire.DefaultControl, Hello());           // the old id is just unhandled now
        Assert.IsTrue(a.Silent(NetOpsWire.DefaultControl, 300));
        var ack = Handshake(a, 0x1F0);
        Assert.AreEqual((ushort)0x1F1, ack.DataOpcode);
        using var b = RawClient.Admit(server.Port, "B", 2);
        Handshake(b, 0x1F0);
        SendData(b, "x:y", NetOpsWire.TargetHost, [9, 8, 7], 1, opcode: 0x1F1);
        Assert.AreEqual((byte)2, a.WaitFor(0x1F1).Origin);
    }

    [TestMethod]
    public void TurnedOffTheOpcodesAreUnhandledLikeInTheOriginal()
    {
        using var server = new NetServer(c => c.ApplyLine("NETOPS=0"));
        using var a = RawClient.Admit(server.Port, "A", 1);
        a.Send(NetOpsWire.DefaultControl, Hello());
        Assert.IsTrue(a.Silent(NetOpsWire.DefaultControl, 400));
        a.Send(0x00B, default);   // the connection is fine
        Assert.IsFalse(a.IsClosed());
    }

    // ---- relaying -----------------------------------------------------------------------------------------------------

    [TestMethod]
    public void RelayStampsTheRealSenderAndTargetsOnlyCapablePlayers()
    {
        using var server = new NetServer();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        using var c = RawClient.Admit(server.Port, "C", 3);
        using var plain = RawClient.Admit(server.Port, "Plain", 4);
        Handshake(host); Handshake(b); Handshake(c);

        var body = Bytes(100);
        SendData(b, "auction:bid", NetOpsWire.TargetHost, body, 7);
        var got = host.WaitFor(NetOpsWire.DefaultData);
        Assert.AreEqual((byte)2, got.Origin, "the server puts the real sender slot into the header");
        Assert.AreEqual((byte)0, got.Sender);
        Assert.IsTrue(NetOpsWire.TryReadData(got.Payload, out var header, out var data));
        Assert.AreEqual(NetOpsWire.ChannelId("auction:bid"), header.Channel);
        Assert.AreEqual((ushort)7, header.MessageId);
        CollectionAssert.AreEqual(body, data.ToArray());
        Assert.IsTrue(c.Silent(NetOpsWire.DefaultData, 150), "only the host gets a message for the host");

        SendData(host, "chat:all", NetOpsWire.TargetAll, [1], 1);
        Assert.AreEqual((byte)1, b.WaitFor(NetOpsWire.DefaultData).Origin);
        Assert.AreEqual((byte)1, c.WaitFor(NetOpsWire.DefaultData).Origin);
        Assert.IsTrue(host.Silent(NetOpsWire.DefaultData, 150), "not echoed to the sender");
        Assert.IsTrue(plain.Silent(NetOpsWire.DefaultData, 150), "not to a player without HELLO");

        SendData(c, "dm", NetOpsWire.SlotTarget(2), [5], 1);
        Assert.AreEqual((byte)3, b.WaitFor(NetOpsWire.DefaultData).Origin);
        SendData(c, "dm", NetOpsWire.SlotTarget(4), [5], 2);      // slot 4 never said HELLO: nothing happens
        Assert.IsTrue(server.WaitStat(s => s.NoRecipient, 1));
        Assert.IsTrue(plain.Silent(NetOpsWire.DefaultData, 150));
    }

    [TestMethod]
    public void FragmentsAreRelayedInOrderAndAcrossMessages()
    {
        using var server = new NetServer(c => c.NetOps.Apply("NETOPS_MAX_FRAGMENT", "100"));
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        Handshake(a); Handshake(b);
        var all = Bytes(250);
        SendData(b, "big", NetOpsWire.TargetHost, all[..100], 3, NetOpsWire.FlagFirst);
        SendData(b, "other", NetOpsWire.TargetHost, [9], 4);                   // another message in between
        SendData(b, "big", NetOpsWire.TargetHost, all[100..200], 3, 0);
        SendData(b, "big", NetOpsWire.TargetHost, all[200..], 3, NetOpsWire.FlagLast);
        var assembled = new List<byte>();
        var sawOther = false;
        for (var i = 0; i < 4; i++)
        {
            var m = a.WaitFor(NetOpsWire.DefaultData);
            NetOpsWire.TryReadData(m.Payload, out var h, out var d);
            if (h.Channel == NetOpsWire.ChannelId("other")) { sawOther = true; continue; }
            assembled.AddRange(d.ToArray());
        }
        Assert.IsTrue(sawOther);
        CollectionAssert.AreEqual(all, assembled);
    }

    [TestMethod]
    public void ServerOnlyChannelsCannotBeRelayed()
    {
        using var server = new NetServer(null, ("vault:grant", true));
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        Handshake(a); Handshake(b);
        SendData(b, "vault:grant", NetOpsWire.TargetHost, [1], 1);
        SendData(b, "vault:grant", NetOpsWire.TargetAll, [1], 2);
        Assert.IsTrue(server.WaitStat(s => s.ServerOnlyRefused, 2));
        Assert.IsTrue(a.Silent(NetOpsWire.DefaultData, 200));
        SendData(b, "vault:grant", NetOpsWire.TargetServer, [1, 2, 3], 3);      // to the server it is fine
        Assert.IsTrue(server.WaitReceived(1));
    }

    // ---- server channels ----------------------------------------------------------------------------------------------

    [TestMethod]
    public void ServerChannelGetsTheReassembledMessageFromTheRealSender()
    {
        using var server = new NetServer(c => c.NetOps.Apply("NETOPS_MAX_FRAGMENT", "64"), ("test:echo", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        Handshake(a); Handshake(b);
        var all = Bytes(200);
        SendData(b, "test:echo", NetOpsWire.TargetServer, all[..64], 9, (byte)(NetOpsWire.FlagFirst | NetOpsWire.FlagJson));
        SendData(b, "test:echo", NetOpsWire.TargetServer, all[64..128], 9, 0);
        SendData(b, "test:echo", NetOpsWire.TargetServer, all[128..192], 9, 0);
        SendData(b, "test:echo", NetOpsWire.TargetServer, all[192..], 9, NetOpsWire.FlagLast);
        Assert.IsTrue(server.WaitReceived(1));
        server.Received.TryDequeue(out var got);
        Assert.AreEqual(2, got.Slot);
        Assert.AreEqual("test:echo", got.Channel);
        Assert.IsTrue(got.Json);
        CollectionAssert.AreEqual(all, got.Data);
        Assert.IsTrue(a.Silent(NetOpsWire.DefaultData, 150), "a message for the server is not relayed");
        Assert.AreEqual(1, server.Host.NetStats.Delivered);
    }

    [TestMethod]
    public void UnknownServerChannelsAreCountedAndDropped()
    {
        using var server = new NetServer(null, ("known", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        Handshake(a);
        SendData(a, "nobody:listens", NetOpsWire.TargetServer, [1], 1);
        SendData(a, "nobody:listens", NetOpsWire.TargetServer, [1], 2, NetOpsWire.FlagFirst);
        SendData(a, "nobody:listens", NetOpsWire.TargetServer, [1], 2, NetOpsWire.FlagLast);   // orphan of the dropped first
        Assert.IsTrue(server.WaitStat(s => s.UnknownChannel, 2));
        Assert.AreEqual(0, server.Received.Count);
        Assert.IsFalse(a.IsClosed());
    }

    [TestMethod]
    public void ServerSendsFragmentedMessagesToCapablePlayersOnly()
    {
        using var server = new NetServer(c => c.NetOps.Apply("NETOPS_MAX_FRAGMENT", "100"), ("echo", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var plain = RawClient.Admit(server.Port, "Plain", 2);
        Handshake(a);
        var payload = Bytes(350);
        server.Host.NetMessage += (from, channel, json, data) => Assert.IsTrue(server.Host.SendNetToSlot(from.Slot, channel.Id, json, data, out _));
        for (var offset = 0; offset < payload.Length; offset += 100)
            SendData(a, "echo", NetOpsWire.TargetServer, payload.AsSpan(offset, Math.Min(100, payload.Length - offset)), 1,
                (byte)((offset == 0 ? NetOpsWire.FlagFirst : 0) | (offset + 100 >= payload.Length ? NetOpsWire.FlagLast : 0)));
        var assembled = new List<byte>();
        var last = false;
        var fragments = 0;
        while (!last)
        {
            var m = a.WaitFor(NetOpsWire.DefaultData);
            Assert.AreEqual((byte)0, m.Origin, "from the server");
            Assert.IsTrue(NetOpsWire.TryReadData(m.Payload, out var h, out var d));
            Assert.AreEqual(NetOpsWire.ChannelId("echo"), h.Channel);
            Assert.AreEqual(fragments == 0, h.First);
            assembled.AddRange(d.ToArray());
            last = h.Last; fragments++;
        }
        Assert.AreEqual(4, fragments);
        CollectionAssert.AreEqual(payload, assembled);
        Assert.IsTrue(plain.Silent(NetOpsWire.DefaultData, 150));
        Assert.IsFalse(server.Host.SendNetToSlot(2, 1, false, payload, out var error));
        StringAssert.Contains(error, "no custom net support");
    }

    // ---- caps ---------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void TokenBucketLimitsABurstAndRefills()
    {
        using var server = new NetServer(c => { c.NetOps.Apply("NETOPS_RATE", "1024"); c.NetOps.Apply("NETOPS_BURST", "6000"); });
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        Handshake(a); Handshake(b);
        for (ushort i = 0; i < 5; i++) SendData(b, "spam", NetOpsWire.TargetHost, Bytes(2000), i);   // 6000 bytes burst: two pass
        Assert.IsTrue(server.WaitStat(s => s.RateLimited, 3));
        Assert.AreEqual(2, server.Host.NetStats.Relayed);
        Assert.IsFalse(a.IsClosed());
    }

    [TestMethod]
    public void OnlyAFewMessagesMayBeInFlight()
    {
        using var server = new NetServer(c => c.NetOps.Apply("NETOPS_MAX_IN_FLIGHT", "2"), ("k", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        Handshake(a);
        for (ushort i = 1; i <= 4; i++) SendData(a, "k", NetOpsWire.TargetServer, [1], i, NetOpsWire.FlagFirst);
        Assert.IsTrue(server.WaitStat(s => s.TooManyInFlight, 2));
        SendData(a, "k", NetOpsWire.TargetServer, [2], 1, NetOpsWire.FlagLast);       // finishing one frees a place
        Assert.IsTrue(server.WaitReceived(1));
        SendData(a, "k", NetOpsWire.TargetServer, [1], 5, NetOpsWire.FlagFirst);
        Thread.Sleep(100);
        Assert.AreEqual(2, server.Host.NetStats.TooManyInFlight);
    }

    [TestMethod]
    public void MessagesLargerThanTheLimitAreDropped()
    {
        using var server = new NetServer(c => { c.NetOps.Apply("NETOPS_MAX_FRAGMENT", "512"); c.NetOps.Apply("NETOPS_MAX_MESSAGE", "1024"); }, ("k", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        Handshake(a);
        SendData(a, "k", NetOpsWire.TargetServer, Bytes(512), 1, NetOpsWire.FlagFirst);
        SendData(a, "k", NetOpsWire.TargetServer, Bytes(512), 1, 0);
        SendData(a, "k", NetOpsWire.TargetServer, Bytes(512), 1, NetOpsWire.FlagLast);
        Assert.IsTrue(server.WaitStat(s => s.TooLarge, 1));
        Thread.Sleep(100);
        Assert.AreEqual(0, server.Received.Count);
    }

    [TestMethod]
    public void StalledMessagesTimeOutAndFreeTheirMemory()
    {
        using var server = new NetServer(c => c.NetOps.Apply("NETOPS_FRAGMENT_TIMEOUT", "1"), ("k", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        Handshake(a);
        SendData(a, "k", NetOpsWire.TargetServer, Bytes(100), 1, NetOpsWire.FlagFirst);
        Assert.IsTrue(server.WaitStat(s => s.TimedOut, 1, 4000));
        SendData(a, "k", NetOpsWire.TargetServer, Bytes(100), 1, NetOpsWire.FlagLast);   // the rest arrives too late
        Assert.IsTrue(server.WaitStat(s => s.Orphans, 1));
        Assert.AreEqual(0, server.Received.Count);
    }

    [TestMethod]
    public void ChannelsPerClientAreLimited()
    {
        using var server = new NetServer(c => c.NetOps.Apply("NETOPS_MAX_CHANNELS", "2"), ("c1", false), ("c2", false), ("c3", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        Handshake(a);
        foreach (var name in new[] { "c1", "c2", "c3", "c1" }) SendData(a, name, NetOpsWire.TargetServer, [1], 1);
        Assert.IsTrue(server.WaitStat(s => s.TooManyChannels, 1));
        Assert.IsTrue(server.WaitReceived(3));
        Assert.AreEqual(3, server.Received.Count);       // c1, c2, c1 again; c3 was refused
        Assert.IsFalse(server.Received.Any(r => r.Channel == "c3"));
    }

    [TestMethod]
    public void UnfinishedMessagesAreBoundedPerSession()
    {
        using var server = new NetServer(c => { c.NetOps.Apply("NETOPS_REASSEMBLY_SESSION", "4096"); c.NetOps.Apply("NETOPS_MAX_FRAGMENT", "1000"); }, ("k", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        Handshake(a);
        SendData(a, "k", NetOpsWire.TargetServer, Bytes(1000), 1, NetOpsWire.FlagFirst);
        for (var i = 0; i < 5; i++) SendData(a, "k", NetOpsWire.TargetServer, Bytes(1000), 1, 0);
        Assert.IsTrue(server.WaitStat(s => s.ReassemblyLimit, 1));
        Assert.AreEqual(0, server.Received.Count);
    }

    [TestMethod]
    public void NoHelloMeansNoService()
    {
        using var server = new NetServer(null, ("k", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        SendData(a, "k", NetOpsWire.TargetServer, [1], 1);
        Assert.IsTrue(server.WaitStat(s => s.NotCapable, 1));
        Assert.AreEqual(0, server.Received.Count);
        Assert.IsTrue(a.Silent(NetOpsWire.DefaultControl, 150));
    }

    [TestMethod]
    public void MalformedFramesAreIgnoredWithoutDisconnecting()
    {
        using var server = new NetServer(null, ("k", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        Handshake(a);
        a.Send(NetOpsWire.DefaultData, new byte[] { 1, 2, 3 });                   // shorter than the header
        a.Send(NetOpsWire.DefaultControl, new byte[] { 9 });                      // unknown control kind
        a.Send(NetOpsWire.DefaultData, new byte[5000]);                           // larger than a fragment
        SendData(a, "k", 0x7, [1], 1);                                            // a target that does not exist
        Assert.IsTrue(server.WaitStat(s => s.Malformed, 4));
        Assert.IsFalse(a.IsClosed());
        SendData(a, "k", NetOpsWire.TargetServer, [1], 2);
        Assert.IsTrue(server.WaitReceived(1));
    }

    // ---- Lua: the net table -------------------------------------------------------------------------------------------

    private static readonly byte[] JsonFlags = [NetOpsWire.FlagFirst | NetOpsWire.FlagLast | NetOpsWire.FlagJson];

    [TestMethod]
    public void LuaChannelAnswersWithTablesAndByteStrings()
    {
        using var server = new NetServer("""
            local ch = net.channel('lua:echo')
            ch:on(function(player, payload)
                if type(payload) == 'table' then
                    assert(ch:send(player, { twice = payload.n * 2, from = player.slot, list = payload.list }))
                else
                    assert(ch:send(player, 'got ' .. payload))
                end
            end)
            """);
        using var a = RawClient.Admit(server.Port, "A", 1);
        Handshake(a);
        SendData(a, "lua:echo", NetOpsWire.TargetServer, "{\"n\":21,\"list\":[1,2,3]}"u8, 1, JsonFlags[0]);
        var (channel, json, data) = NextMessage(a);
        Assert.AreEqual(NetOpsWire.ChannelId("lua:echo"), channel);
        Assert.IsTrue(json);
        var reply = System.Text.Json.JsonDocument.Parse(data).RootElement;
        Assert.AreEqual(42, reply.GetProperty("twice").GetInt32());
        Assert.AreEqual(1, reply.GetProperty("from").GetInt32());
        Assert.AreEqual(3, reply.GetProperty("list").GetArrayLength());
        SendData(a, "lua:echo", NetOpsWire.TargetServer, [0x61, 0x00, 0xFF], 2);
        (_, json, data) = NextMessage(a);
        Assert.IsFalse(json);
        CollectionAssert.AreEqual(new byte[] { (byte)'g', (byte)'o', (byte)'t', (byte)' ', 0x61, 0x00, 0xFF }, data);
    }

    [TestMethod]
    public void LuaSendReachesOnlyCapablePlayersAndSupportsTellsWho()
    {
        using var server = new NetServer("""
            local ch = net.channel('lua:cast')
            ch:on(function(player, payload)
                local note = {}
                for slot = 1, 3 do note[#note + 1] = slot .. '=' .. tostring(net.supports(slot)) end
                assert(ch:send('all', table.concat(note, ',')))
                local ok, err = ch:send(3, 'x')
                assert(ok == nil and err:find('no custom net support'), tostring(err))
                assert(net.supports(player) == true)
            end)
            """);
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        using var plain = RawClient.Admit(server.Port, "Plain", 3);
        Handshake(a); Handshake(b);
        SendData(a, "lua:cast", NetOpsWire.TargetServer, [1], 1);
        var (_, _, forA) = NextMessage(a);
        var (_, _, forB) = NextMessage(b);
        Assert.AreEqual("1=true,2=true,3=false", System.Text.Encoding.Latin1.GetString(forA));
        CollectionAssert.AreEqual(forA, forB);
        Assert.IsTrue(plain.Silent(NetOpsWire.DefaultData, 200));
        Assert.AreEqual(0, server.Host.Scripts!.Errors);
    }

    [TestMethod]
    public void LuaChannelsCanBeServerOnlyAndCollisionsAreReported()
    {
        using var server = new NetServer("""
            local vault = net.channel('lua:vault', { server_only = true })
            vault:on(function(player, payload) vault:send(player, 'ok') end)
            local first = net.channel('n512789')
            local second, err = net.channel('n749192')
            local bad, why = net.channel('')
            first:on(function(player)
                first:send(player, tostring(second == nil) .. '|' .. tostring(err) .. '|' .. tostring(bad == nil))
            end)
            """);
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        Handshake(a); Handshake(b);
        SendData(b, "lua:vault", NetOpsWire.TargetHost, [1], 1);                    // not relayed through a server-only channel
        Assert.IsTrue(server.WaitStat(s => s.ServerOnlyRefused, 1));
        Assert.IsTrue(a.Silent(NetOpsWire.DefaultData, 150));
        SendData(b, "lua:vault", NetOpsWire.TargetServer, [1], 2);
        Assert.AreEqual("ok", System.Text.Encoding.Latin1.GetString(NextMessage(b).Data));
        SendData(b, "n512789", NetOpsWire.TargetServer, [1], 3);
        var text = System.Text.Encoding.Latin1.GetString(NextMessage(b).Data);
        StringAssert.StartsWith(text, "true|");
        StringAssert.Contains(text, "collides");
        StringAssert.EndsWith(text, "|true");
    }

    [TestMethod]
    public void BrokenHandlersAreContainedCountedAndSwitchedOff()
    {
        using var server = new NetServer("""
            net.channel('lua:bad'):on(function() error('boom') end)
            local good = net.channel('lua:good')
            good:on(function(player) good:send(player, 'fine') end)
            net.channel('lua:loop'):on(function() while true do end end)
            """, c => c.NetOps.Apply("NETOPS_HANDLER_BUDGET", "5000"));
        using var a = RawClient.Admit(server.Port, "A", 1);
        Handshake(a);
        for (ushort i = 0; i < 22; i++) SendData(a, "lua:bad", NetOpsWire.TargetServer, [1], i);
        Assert.IsTrue(server.WaitStat(s => s.Delivered, 22));
        SendData(a, "lua:loop", NetOpsWire.TargetServer, [1], 100);               // an endless loop is stopped by the budget
        SendData(a, "lua:good", NetOpsWire.TargetServer, [1], 101);
        Assert.AreEqual("fine", System.Text.Encoding.Latin1.GetString(NextMessage(a).Data));
        var errors = server.Host.Scripts!.Errors;
        Assert.AreEqual(21, errors, "20 errors switch the handler off (the 21st message finds none), the endless loop counts one");
        Assert.IsFalse(a.IsClosed());
    }

    [TestMethod]
    public void EveryHandlerCallStartsWithAFreshInstructionBudget()
    {
        using var server = new NetServer("""
            local ch = net.channel('lua:count')
            local total = 0
            ch:on(function(player)
                for i = 1, 3000 do total = total + 1 end      -- 3000 loop turns per call: a few calls would exhaust a budget shared between them
                ch:send(player, tostring(total))
            end)
            """, c => c.NetOps.Apply("NETOPS_HANDLER_BUDGET", "20000"));
        using var a = RawClient.Admit(server.Port, "A", 1);
        Handshake(a);
        for (ushort i = 1; i <= 8; i++)
        {
            SendData(a, "lua:count", NetOpsWire.TargetServer, [1], i);
            Assert.AreEqual((3000 * i).ToString(), System.Text.Encoding.Latin1.GetString(NextMessage(a).Data));
        }
        Assert.AreEqual(0, server.Host.Scripts!.Errors);
    }
    // ---- the chat carrier ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void ChatFramesMatchTheClientsVector()
    {
        // produced by Sacred Gear's kernel.lua framing (message number 5): kind 'k', channel "test", body {"a":1}
        ushort[] expected = [0xE1A0, 0x4005, 0x4000, 0x4000, 0x400D, 0x7582, 0x5D19, 0x6E6E, 0x87B2, 0x5309, 0x48E8, 0xA2FA];
        var message = NetChatWire.Join(NetChatWire.KindJson, "test", "{\"a\":1}"u8);
        var frames = NetChatWire.Encode(message, 5);
        Assert.AreEqual(1, frames.Count);
        var units = new ushort[frames[0].Length / 2];
        for (var i = 0; i < units.Length; i++) units[i] = (ushort)(frames[0][i * 2] | (frames[0][i * 2 + 1] << 8));
        CollectionAssert.AreEqual(expected, units);
    }

    [TestMethod]
    public void ChatFramesRoundTripAtEverySize()
    {
        foreach (var length in new[] { 2, 3, 100, 226, 227, 228, 229, 460, 461, 462, 5000, 65536 })
        {
            var message = Bytes(length, 3);
            var frames = NetChatWire.Encode(message, 123);
            Assert.AreEqual(NetChatWire.FrameCount(length), frames.Count, "frames for " + length);
            var bytes = new List<byte>();
            for (var i = 0; i < frames.Count; i++)
            {
                Assert.IsTrue(frames[i].Length <= NetChatWire.MaxUnits * 2);
                Assert.IsTrue(NetChatWire.TryRead(frames[i], out var frame));
                Assert.AreEqual(i, frame.Index);
                Assert.IsTrue(NetChatWire.Unpack(frame.Packed, NetChatWire.BytesIn(length, i), bytes));
            }
            CollectionAssert.AreEqual(message, bytes.ToArray(), "round trip of " + length);
        }
        Assert.IsFalse(NetChatWire.TryRead("hello"u8.ToArray(), out _));
    }

    [TestMethod]
    public void TheServerQueriesAPlayerThatAnnouncedItself()
    {
        using var server = new NetServer(c => { c.ApplyLine("NETOPS_CONTROL=0x1F0"); c.ApplyLine("NETOPS_DATA=0x1F1"); });
        using var host = RawClient.Admit(server.Port, "Host", 1);
        Assert.IsTrue(host.Silent(0x003, 300), "no query before the player is in the game");
        host.Send(0x016, default); host.Send(0x018, new byte[] { 0x64, 0x19, 0, 0 }); host.WaitFor(0x018);
        Assert.IsTrue(host.Silent(0x003, 400), "a plain client that entered the world is never written to (it would show the frames as chat)");
        SendChat(host, 0, NetChatWire.KindHello, NetChatWire.HandshakeName, [NetOpsWire.ProtocolVersion]);       // a Gear client announces itself on entering
        var (kind, name, body) = NextChat(host);
        Assert.AreEqual(NetChatWire.KindQuery, kind);
        Assert.AreEqual(NetChatWire.HandshakeName, name);
        Assert.AreEqual(5, body.Length);
        Assert.AreEqual(NetOpsWire.ProtocolVersion, body[0]);
        Assert.AreEqual((ushort)0x1F0, BitConverter.ToUInt16(body, 1));
        Assert.AreEqual((ushort)0x1F1, BitConverter.ToUInt16(body, 3));
    }

    [TestMethod]
    public void HelloBeforeTheChatAgreementIsIgnored()
    {
        using var server = new NetServer();
        using var a = RawClient.Admit(server.Port, "A", 1);
        a.Send(NetOpsWire.DefaultControl, Hello());
        Assert.IsTrue(server.WaitStat(s => s.NotCapable, 1));
        Assert.IsTrue(a.Silent(NetOpsWire.DefaultControl, 200), "no HELLO_ACK without an agreement");
        SendChat(a, 0, NetChatWire.KindAnswer, NetChatWire.HandshakeName, AnswerBody(0x1E0, 0x1E1, false));   // declines the custom types
        a.Send(NetOpsWire.DefaultControl, Hello());
        Assert.IsTrue(server.WaitStat(s => s.NotCapable, 2));
        Assert.IsTrue(a.Silent(NetOpsWire.DefaultControl, 200));
        SendChat(a, 0, NetChatWire.KindAnswer, NetChatWire.HandshakeName, AnswerBody(0x1E5, 0x1E6, true));   // other numbers than the server's
        a.Send(NetOpsWire.DefaultControl, Hello());
        Assert.IsTrue(server.WaitStat(s => s.NotCapable, 3));
        Assert.IsTrue(a.Silent(NetOpsWire.DefaultControl, 200));
        Handshake(a);                                                                                          // the real agreement
    }

    [TestMethod]
    public void LuaChannelsWorkOverChatForPlayersWithoutTheCustomTypes()
    {
        using var server = new NetServer("""
            local ch = net.channel('lua:echo')
            ch:on(function(player, payload)
                local how = net.transport(player)
                if type(payload) == 'table' then
                    assert(ch:send(player, { how = how, n = payload.n }))
                else
                    assert(ch:send(player, how .. ':' .. payload))
                end
            end)
            """);
        using var a = RawClient.Admit(server.Port, "A", 1);
        SendChat(a, 0, NetChatWire.KindAnswer, NetChatWire.HandshakeName, AnswerBody(0x1E0, 0x1E1, false));   // channels over chat only
        SendChat(a, 0, NetChatWire.KindJson, "lua:echo", "{\"n\":7}"u8);
        var (kind, name, body) = NextChat(a);
        Assert.AreEqual(NetChatWire.KindJson, kind);
        Assert.AreEqual("lua:echo", name);
        var reply = System.Text.Json.JsonDocument.Parse(body).RootElement;
        Assert.AreEqual("chat", reply.GetProperty("how").GetString());
        Assert.AreEqual(7, reply.GetProperty("n").GetInt32());
        var big = Bytes(5000, 9);                                           // a message in 22 frames, both ways
        SendChat(a, 0, NetChatWire.KindBytes, "lua:echo", big);
        (kind, name, body) = NextChat(a, 8000);
        Assert.AreEqual(NetChatWire.KindBytes, kind);
        var expected = System.Text.Encoding.Latin1.GetBytes("chat:").Concat(big).ToArray();
        CollectionAssert.AreEqual(expected, body);
        Assert.IsTrue(a.Silent(NetOpsWire.DefaultData, 150), "a chat-only player never gets a custom frame");
        Assert.AreEqual(0, server.Host.Scripts!.Errors);
    }

    [TestMethod]
    public void LuaSendPicksTheTransportPerPlayer()
    {
        using var server = new NetServer("""
            local ch = net.channel('lua:cast')
            ch:on(function(player)
                local t = {}
                for slot = 1, 3 do t[#t + 1] = slot .. '=' .. tostring(net.transport(slot)) end
                assert(ch:send('all', table.concat(t, ',')))
            end)
            """);
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        using var plain = RawClient.Admit(server.Port, "Plain", 3);
        Handshake(a);                                                                                          // custom types
        SendChat(b, 0, NetChatWire.KindAnswer, NetChatWire.HandshakeName, AnswerBody(0x1E0, 0x1E1, false));   // chat only
        SendChat(b, 0, NetChatWire.KindBytes, "lua:cast", [1]);
        var text = "1=custom,2=chat,3=nil";
        Assert.AreEqual(text, System.Text.Encoding.Latin1.GetString(NextMessage(a).Data));
        var (_, _, forB) = NextChat(b);
        Assert.AreEqual(text, System.Text.Encoding.Latin1.GetString(forB));
        Assert.IsTrue(plain.Silent(0x003, 200) && plain.Silent(NetOpsWire.DefaultData, 50), "a player that never spoke channels gets nothing");
    }

    [TestMethod]
    public void ChatPathKeepsItsCaps()
    {
        using var server = new NetServer(c => { c.NetOps.Apply("NETOPS_MAX_CHANNELS", "2"); c.NetOps.Apply("NETOPS_RATE", "1024"); c.NetOps.Apply("NETOPS_BURST", "6000"); }, ("c1", false), ("c2", false), ("c3", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        SendChat(a, 0, NetChatWire.KindAnswer, NetChatWire.HandshakeName, AnswerBody(0x1E0, 0x1E1, false));
        foreach (var name in new[] { "c1", "c2", "c3" }) SendChat(a, 0, NetChatWire.KindBytes, name, [1]);
        SendChat(a, 0, NetChatWire.KindBytes, "nobody", [1]);
        Assert.IsTrue(server.WaitStat(s => s.TooManyChannels, 1));
        Assert.AreEqual(2, server.Received.Count, "the third channel of one client is refused");
        // the token bucket counts chat bytes too: a burst of frames is cut off
        for (var i = 0; i < 40; i++) SendChat(a, 0, NetChatWire.KindBytes, "c1", Bytes(2000, (byte)i));
        Assert.IsTrue(server.WaitStat(s => s.RateLimited, 1));
        Assert.IsFalse(a.IsClosed());
    }

    [TestMethod]
    public void ChatFramesForServerOnlyChannelsAreNotRelayedBetweenPlayers()
    {
        using var server = new NetServer(null, ("vault:grant", true), ("open:channel", false));
        using var a = RawClient.Admit(server.Port, "A", 1);
        using var b = RawClient.Admit(server.Port, "B", 2);
        SendChat(b, 1, NetChatWire.KindBytes, "vault:grant", Bytes(700));       // three frames, named in the first
        Assert.IsTrue(server.WaitStat(s => s.ServerOnlyRefused, 1));
        Assert.IsTrue(a.Silent(0x003, 200), "nothing of it reaches the other player");
        SendChat(b, 1, NetChatWire.KindBytes, "open:channel", Bytes(700));      // an ordinary channel is just chat to the server
        var (kind, name, body) = (default(byte), "", Array.Empty<byte>());
        var bytes = new List<byte>(); var total = 0;
        while (true)
        {
            var m = a.WaitFor(0x003);
            Assert.AreEqual((byte)2, m.Origin, "relayed with the real sender");
            Assert.IsTrue(NetChatWire.TryRead(m.Payload.AsSpan(4), out var frame));
            if (frame.Index == 0) total = frame.Length;
            NetChatWire.Unpack(frame.Packed, NetChatWire.BytesIn(total, frame.Index), bytes);
            if (bytes.Count >= total) break;
        }
        Assert.IsTrue(NetChatWire.TrySplit(bytes.ToArray(), out kind, out name, out var rest));
        Assert.AreEqual("open:channel", name);
        Assert.AreEqual(700, rest.Length);
    }}
