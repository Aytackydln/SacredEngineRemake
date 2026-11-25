using Sacred.GameServer.Net.Kcp;

namespace Sacred.GameServer.Tests;

/// <summary>The KCP port against itself over a lossy, reordering in-memory link, with Sacred Bild's settings.</summary>
[TestClass]
public class KcpTests
{
    private sealed class Wire
    {
        public readonly List<(uint Due, byte[] Data)> InFlight = new();
        private readonly Random _random;
        private readonly double _loss;
        public uint Now;
        public Wire(int seed, double loss) { _random = new Random(seed); _loss = loss; }
        public void Send(byte[] buffer, int offset, int length)
        {
            if (_random.NextDouble() < _loss) return;
            InFlight.Add((Now + (uint)_random.Next(5, 40), buffer.AsSpan(offset, length).ToArray()));
        }
        public void Deliver(Kcp to)
        {
            for (var i = InFlight.Count - 1; i >= 0; i--)
                if ((int)(Now - InFlight[i].Due) >= 0) { to.Input(InFlight[i].Data); InFlight.RemoveAt(i); }
        }
    }

    private static Kcp Create(uint conv, Kcp.OutputHandler output, int reserved)
    {
        var kcp = new Kcp(conv, output, reserved) { Stream = true };
        kcp.SetMtu(1184);
        kcp.WndSize(128, 256);
        kcp.NoDelay(1, 10, 1, 1);
        return kcp;
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(0.2)]
    public void StreamArrivesIntactOverALossyLink(double loss)
    {
        const int reserved = 16;
        var ab = new Wire(1, loss);
        var ba = new Wire(2, loss);
        var a = Create(0x1234, (b, n) => ab.Send(b, reserved, n - reserved), reserved);
        var b = Create(0x1234, (buf, n) => ba.Send(buf, reserved, n - reserved), reserved);
        var source = new byte[300_000];
        new Random(3).NextBytes(source);
        var received = new byte[source.Length];
        var got = 0;
        var sent = 0;
        var scratch = new byte[256 * 1024];
        for (uint t = 0; t < 120_000 && got < source.Length; t += 5)
        {
            ab.Now = ba.Now = t;
            while (sent < source.Length && a.WaitSnd < 200)
            {
                var piece = Math.Min(64 * a.Mss, source.Length - sent);
                Assert.AreEqual(0, a.Send(source.AsSpan(sent, piece)));
                sent += piece;
            }
            a.Update(t); b.Update(t);
            ab.Deliver(b); ba.Deliver(a);
            int n;
            while ((n = b.Recv(scratch)) > 0) { scratch.AsSpan(0, n).CopyTo(received.AsSpan(got)); got += n; }
        }
        Assert.AreEqual(source.Length, got);
        CollectionAssert.AreEqual(source, received);
    }

    [TestMethod]
    public void HeaderMatchesTheCLayout()
    {
        byte[]? packet = null;
        var kcp = Create(0xA1B2C3D4, (buf, n) => packet = buf.AsSpan(0, n).ToArray(), 0);
        kcp.Send("hello"u8);
        kcp.Update(0); kcp.Update(10);           // first flush only opens the congestion window (cwnd 0 -> 1)... nc=1 sends at once
        Assert.IsNotNull(packet);
        Assert.AreEqual(24 + 5, packet!.Length);
        CollectionAssert.AreEqual(new byte[] { 0xD4, 0xC3, 0xB2, 0xA1, 81, 0 }, packet[..6]);  // conv, IKCP_CMD_PUSH, frg
        Assert.AreEqual(5u, BitConverter.ToUInt32(packet, 20));
    }
}
