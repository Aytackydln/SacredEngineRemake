using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Sacred.GameServer.Config;
using Sacred.GameServer.Net.Kcp;
using Sacred.GameServer.Net.Sbt;
using Sacred.GameServer.Session;
using Sacred.GameServer.Wire;
using Sacred.Network.Admission;

namespace Sacred.GameServer.Tests;

/// <summary>A Sacred Bild style player over real UDP: SBT handshake, then the TinCat login through KCP.</summary>
[TestClass]
public class SbtTransportTests
{
    private static byte[] Hello(uint nonce, int tcpPort, ReadOnlySpan<byte> cookie)
    {
        var p = new byte[SbtWire.HelloBytes];
        SbtWire.Magic(p, SbtWire.Hello);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), SbtWire.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), nonce);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12), (ushort)tcpPort);
        cookie.CopyTo(p.AsSpan(16));
        return p;
    }

    private static byte[] Receive(UdpClient udp, byte type)
    {
        var deadline = Environment.TickCount64 + 3000;
        while (Environment.TickCount64 < deadline)
        {
            var task = udp.ReceiveAsync();
            if (!task.Wait((int)Math.Max(1, deadline - Environment.TickCount64))) break;
            if (task.Result.Buffer is { Length: >= 4 } d && d[3] == type) return d;
        }
        throw new AssertFailedException($"no SBT '{(char)type}'");
    }

    [TestMethod]
    public void HandshakeRefusalsAndLoginOverKcp()
    {
        var host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 }, new AdmissionRules(null), null);
        var listener = (SbtListener)host.AddListener(inbox => new SbtListener(0, inbox, () => host.TcpPort));
        var thread = new Thread(host.Run) { IsBackground = true };
        thread.Start();
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));          // bound to loopback, not the wildcard
            udp.Connect(IPAddress.Loopback, listener.Port);

            udp.Send(Hello(7, host.TcpPort + 1, default));                    // wrong game port
            Assert.AreEqual(SbtWire.RefusedNotListening, BinaryPrimitives.ReadUInt32LittleEndian(Receive(udp, SbtWire.Refused).AsSpan(12)));

            udp.Send(Hello(9, host.TcpPort, default));                        // no cookie: challenge only
            var challenge = Receive(udp, SbtWire.Challenge);
            Assert.AreEqual(SbtWire.ChallengeBytes, challenge.Length);
            udp.Send(Hello(9, host.TcpPort, challenge.AsSpan(16, 16)));
            var welcome = Receive(udp, SbtWire.Welcome);
            var id = BinaryPrimitives.ReadUInt32LittleEndian(welcome.AsSpan(12));
            var token = BinaryPrimitives.ReadUInt64LittleEndian(welcome.AsSpan(16));
            udp.Send(Hello(9, host.TcpPort, challenge.AsSpan(16, 16)));        // repeat: same session
            Assert.AreEqual(id, BinaryPrimitives.ReadUInt32LittleEndian(Receive(udp, SbtWire.Welcome).AsSpan(12)));
            Assert.AreEqual(1, listener.SessionCount);

            var kcp = new Kcp(id, (b, n) => { SbtWire.SessionHeader(b, SbtWire.Data, id, token); udp.Send(b, n); }, SbtWire.HeaderBytes) { Stream = true };
            kcp.SetMtu(SbtWire.KcpMtu); kcp.WndSize(128, 256); kcp.NoDelay(1, 10, 1, 1);
            var login = new byte[TinCatWire.HeaderBytes + TinCatWire.LoginPayloadBytes];
            BinaryPrimitives.WriteUInt32LittleEndian(login.AsSpan(TinCatWire.HeaderBytes), TinCatWire.Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(login.AsSpan(TinCatWire.HeaderBytes + 4), TinCatWire.UnassignedId);
            TinCatWire.SealFrame(login, TinCatWire.UnassignedId, TinCatWire.ServerId, TinCatWire.LoginRequest, TinCatWire.LoginPayloadBytes);
            kcp.Send(login);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var stream = new byte[64 * 1024];
            var got = 0;
            while (clock.ElapsedMilliseconds < 3000 && got < TinCatWire.HeaderBytes)
            {
                kcp.Update((uint)clock.ElapsedMilliseconds);
                while (udp.Available > 0)
                {
                    IPEndPoint? any = null;
                    var d = udp.Receive(ref any);
                    if (d.Length > SbtWire.HeaderBytes && d[3] == SbtWire.Data) kcp.Input(d.AsSpan(SbtWire.HeaderBytes));
                }
                int n;
                while ((n = kcp.Recv(stream.AsSpan(got))) > 0) got += n;
                Thread.Sleep(5);
            }
            Assert.IsTrue(got >= TinCatWire.HeaderBytes, "no TinCat answer over KCP");
            Assert.AreEqual(TinCatWire.Magic, BinaryPrimitives.ReadUInt32LittleEndian(stream));
            Assert.AreEqual(TinCatWire.LoginAccepted, BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(12)));

            var close = new byte[SbtWire.HeaderBytes];
            SbtWire.SessionHeader(close, SbtWire.Close, id, token);
            udp.Send(close);
            var deadline = Environment.TickCount64 + 2000;
            while (listener.SessionCount != 0 && Environment.TickCount64 < deadline) Thread.Sleep(10);
            Assert.AreEqual(0, listener.SessionCount);
        }
        finally { host.Stop(); thread.Join(); host.Dispose(); }
    }
}
