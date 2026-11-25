using System.Buffers.Binary;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>0x198, the original's trade state machine (case 0x4C850E), in relay mode: two players pair, offer, lock in and exchange;
/// a leaving partner ends the trade; malformed or out-of-order messages do no harm.</summary>
[TestClass]
public class TradeTests
{
    private sealed class Pair : IDisposable
    {
        private readonly GameServerHost _host;
        private readonly Thread _thread;
        public readonly RawClient A, B;
        public Pair()
        {
            _host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 }, new AdmissionRules(null), null);
            _thread = new Thread(_host.Run) { IsBackground = true };
            _thread.Start();
            A = RawClient.Admit(_host.TcpPort, "Alice", 1);
            A.Send(0x016, default); A.Send(0x018, Word(0x1964)); A.WaitFor(0x018);
            B = RawClient.Admit(_host.TcpPort, "Bob", 2);
            B.Send(0x017, default); B.WaitFor(0x017);
        }
        public void Dispose() { A.Dispose(); B.Dispose(); _host.Stop(); _thread.Join(); _host.Dispose(); }
    }

    private static byte[] Word(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); return b; }
    private static byte[] Words(params uint[] values) { var b = new byte[values.Length * 4]; for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(i * 4), values[i]); return b; }
    private static uint W(RawMessage m, int index) => BinaryPrimitives.ReadUInt32LittleEndian(m.Payload.AsSpan(index * 4));

    private static void Expect(RawClient client, uint other, uint code, byte origin)
    {
        var m = client.WaitFor(0x198);
        Assert.AreEqual(other, W(m, 0), $"partner word of 0x{code:X}");
        Assert.AreEqual(code, W(m, 1), "code");
        Assert.AreEqual(origin, m.Origin, $"origin of 0x{code:X}");
    }

    [TestMethod]
    public void AFullTradeInRelayMode()
    {
        using var t = new Pair();
        uint a = t.A.Slot, b = t.B.Slot;
        t.A.Send(0x198, Words(b, 0x20));                                    // invitation
        Expect(t.B, a, 0x20, (byte)a);
        t.B.Send(0x198, Words(a, 0x21));                                    // accepted
        Expect(t.A, b, 0x25, (byte)b);
        Expect(t.B, a, 0x25, (byte)a);

        t.A.Send(0x198, [.. Words(b, 0x26, 1, 0x4711), .. new byte[] { 1, 2, 3, 4, 5 }]);   // one offered item with its data
        Expect(t.A, b, 0x2A, (byte)b);                                      // offer accepted
        Expect(t.B, a, 0x2B, (byte)a);
        var offer = t.B.WaitFor(0x198);                                     // the offer itself, relayed to the partner
        Assert.AreEqual(a, W(offer, 0));
        Assert.AreEqual(0x26u, W(offer, 1));
        Assert.AreEqual(0x4711u, W(offer, 3));

        t.B.Send(0x198, Words(a, 0x27, 250));                               // gold
        var gold = t.A.WaitFor(0x198);
        Assert.AreEqual(b, W(gold, 0));
        Assert.AreEqual(0x27u, W(gold, 1));
        Assert.AreEqual(250u, W(gold, 2));

        t.A.Send(0x198, Words(b, 0x28));                                    // A locks in
        Expect(t.A, b, 0x29, (byte)b);
        t.B.WaitFor(0x198);                                                 // forwarded lock-in
        t.B.Send(0x198, Words(a, 0x28));                                    // B locks in: both locked, the exchange runs
        Expect(t.B, a, 0x29, (byte)a);
        t.A.WaitFor(0x198);                                                 // forwarded lock-in
        var toA = t.A.WaitFor(0x198);
        Assert.AreEqual(b, W(toA, 0));
        Assert.AreEqual(0x2Fu, W(toA, 1));
        Assert.AreEqual(250u, W(toA, 2), "partner's gold");
        Assert.AreEqual(0u, W(toA, 3), "own gold");
        var toB = t.B.WaitFor(0x198);
        Assert.AreEqual(a, W(toB, 0));
        Assert.AreEqual(0x2Fu, W(toB, 1));
        Assert.AreEqual(0u, W(toB, 2));
        Assert.AreEqual(250u, W(toB, 3));
        Assert.AreEqual(1, toB.Payload[16], "A's item data reaches B");
        Assert.AreEqual(5, toB.Payload[20]);

        // both trades are over: a new invitation is possible
        t.A.Send(0x198, Words(b, 0x20));
        Expect(t.B, a, 0x20, (byte)a);
    }

    [TestMethod]
    public void BusyPartnerAndCancel()
    {
        using var t = new Pair();
        uint a = t.A.Slot, b = t.B.Slot;
        t.A.Send(0x198, Words(b, 0x20));
        Expect(t.B, a, 0x20, (byte)a);
        t.B.Send(0x198, Words(a, 0x20));                                    // A already invited B: B is busy for A... and vice versa
        Expect(t.B, a, 0x24, (byte)a);                                      // "busy" comes back to the one who asked
        t.A.Send(0x198, Words(b, 0x22));                                    // A withdraws the invitation
        Expect(t.A, b, 0x31, (byte)b);
        var gone = t.B.WaitFor(0x198);
        Assert.AreEqual(0u, W(gone, 0));
        Assert.AreEqual(0x22u, W(gone, 1));

        t.A.Send(0x198, Words(b, 0x20)); Expect(t.B, a, 0x20, (byte)a);
        t.B.Send(0x198, Words(a, 0x21)); Expect(t.A, b, 0x25, (byte)b); Expect(t.B, a, 0x25, (byte)a);
        t.B.Send(0x198, Words(a, 0x30));                                    // cancel
        Expect(t.B, a, 0x31, (byte)a);
        Expect(t.A, b, 0x31, (byte)b);
    }

    [TestMethod]
    public void TheTradeEndsWhenThePartnerLeavesOrDisconnects()
    {
        using var t = new Pair();
        uint a = t.A.Slot, b = t.B.Slot;
        t.A.Send(0x198, Words(b, 0x20)); Expect(t.B, a, 0x20, (byte)a);
        t.B.Send(0x198, Words(a, 0x21)); Expect(t.A, b, 0x25, (byte)b); Expect(t.B, a, 0x25, (byte)a);
        t.A.Dispose();                                                      // A drops the connection
        Expect(t.B, a, 0x31, (byte)a);                                      // B is told the trade is over
        // B can start a new trade later (its state was cleared)
        Assert.IsTrue(t.B.Silent(0x198, 200));
    }

    [TestMethod]
    public void MalformedAndOutOfOrderMessagesAreHarmless()
    {
        using var t = new Pair();
        uint a = t.A.Slot, b = t.B.Slot;
        t.A.Send(0x198, Word(b));                                           // too short: ignored
        t.A.Send(0x198, Words(b, 0x26, 1, 5));                              // offer without a trade
        t.A.Send(0x198, Words(b, 0x28));                                    // lock-in without a trade
        t.A.Send(0x198, Words(b, 0x30));                                    // cancel without a trade
        t.A.Send(0x198, Words(b, 0x99));                                    // unknown sub command
        t.A.Send(0x198, Words(b, 0x21));                                    // accepting an invitation nobody made
        Assert.IsTrue(t.A.Silent(0x198, 300));
        Assert.IsTrue(t.B.Silent(0x198, 100));

        t.A.Send(0x198, Words(99, 0x20));                                   // no such partner
        Expect(t.A, 99, 0x31, 99 & 0x1F);                                // the origin field holds five bits
        t.A.Send(0x198, [.. Words(b, 0x26, 0xFFFFFFFF), .. new byte[8]]);    // absurd item count, still no trade
        t.B.Send(0x198, Words(a, 0x20));                                    // the server is still fine
        Assert.AreEqual(0x20u, W(t.A.WaitFor(0x198), 1));
    }
}

/// <summary>0x180 (directed relay), 0x004 (message to a named player) and 0x139 (statistics request) in relay mode.</summary>
[TestClass]
public class DirectedMessageTests
{
    private static byte[] Words(params uint[] values) { var b = new byte[values.Length * 4]; for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(i * 4), values[i]); return b; }

    private static (GameServerHost, Thread, RawClient, RawClient) Start()
    {
        var host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 }, new AdmissionRules(null), null);
        var thread = new Thread(host.Run) { IsBackground = true };
        thread.Start();
        var a = RawClient.Admit(host.TcpPort, "Alice", 1);
        a.Send(0x016, default); a.Send(0x018, Words(0x1964)); a.WaitFor(0x018);
        var b = RawClient.Admit(host.TcpPort, "Bob", 2);
        b.Send(0x017, default); b.WaitFor(0x017);
        return (host, thread, a, b);
    }

    [TestMethod]
    public void DirectedRelayGoesToTheNamedSlotOnly()
    {
        var (host, thread, a, b) = Start();
        try
        {
            a.Send(0x180, Words(b.Slot, 7, 8));
            var got = b.WaitFor(0x180);
            Assert.AreEqual(a.Slot, got.Origin);
            CollectionAssert.AreEqual(Words(b.Slot, 7, 8), got.Payload);
            a.Send(0x180, Words(99, 1));                               // no such slot
            a.Send(0x180, Words(0, 1));
            Assert.IsTrue(a.Silent(0x180, 200));
            Assert.IsTrue(b.Silent(0x180, 100));
        }
        finally { a.Dispose(); b.Dispose(); host.Stop(); thread.Join(); host.Dispose(); }
    }

    [TestMethod]
    public void MessageToANamedPlayerBySlotOrLobbyId()
    {
        var (host, thread, a, b) = Start();
        try
        {
            a.Send(0x004, Words(b.Slot, 42));
            var bySlot = b.WaitFor(0x004);
            Assert.AreEqual(a.Slot, bySlot.Origin);
            CollectionAssert.AreEqual(Words(b.Slot, 42), bySlot.Payload);
            a.Send(0x004, Words(a.Slot, 1));                           // oneself: nothing comes back
            a.Send(0x004, Words(0xDEAD0001, 1));                       // unknown lobby id
            Assert.IsTrue(a.Silent(0x004, 200));
        }
        finally { a.Dispose(); b.Dispose(); host.Stop(); thread.Join(); host.Dispose(); }
    }

    [TestMethod]
    public void StatisticsRequestIsAnsweredToTheRequester()
    {
        var (host, thread, a, b) = Start();
        try
        {
            a.Send(0x139, Words(b.Slot));
            var echo = a.WaitFor(0x139);
            Assert.AreEqual(b.Slot, echo.Origin);
            Assert.IsTrue(b.Silent(0x139, 100));
        }
        finally { a.Dispose(); b.Dispose(); host.Stop(); thread.Join(); host.Dispose(); }
    }
}
