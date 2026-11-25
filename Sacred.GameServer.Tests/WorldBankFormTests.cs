using System.Buffers.Binary;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Tests;

/// <summary>0x1B5 serves bank 0x1B3 in the participant form of server 0x4D22E7 (section 1 = runtime word +0x120 only).</summary>
[TestClass]
public class WorldBankFormTests
{
    private const int UploadRecord = 292;

    private sealed class Server : IDisposable
    {
        private readonly GameServerHost _host;
        private readonly Thread _thread;
        public Server()
        {
            _host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 }, new AdmissionRules(null), null);
            _thread = new Thread(_host.Run) { IsBackground = true };
            _thread.Start();
        }
        public int Port => _host.TcpPort;
        public void Dispose() { _host.Stop(); _thread.Join(); _host.Dispose(); }
    }

    private static void Put(List<byte> bytes, uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); bytes.AddRange(b.ToArray()); }

    /// <summary>Host upload form: header, 2 keyed word lists, 3 x 292-byte records, then sections 2..6.</summary>
    private static byte[] Bank3Upload(out uint[] runtimeWords)
    {
        uint[] counts = [2, 3, 2, 1, 1, 1, 1, 0, 0, 0];
        var bytes = new List<byte>();
        foreach (var c in counts) Put(bytes, c);
        Put(bytes, 0x11); Put(bytes, 2); Put(bytes, 7); Put(bytes, 8);
        Put(bytes, 0x22); Put(bytes, 0);
        runtimeWords = [0xA0A0A0A0, 0xB1B1B1B1, 0xC2C2C2C2];
        for (var r = 0; r < 3; r++)
        {
            var record = new byte[UploadRecord];
            Array.Fill(record, (byte)(r + 1));
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x120), runtimeWords[r]);
            bytes.AddRange(record);
        }
        var fill = (byte)0x70;
        for (var section = 2; section <= 6; section++)
        {
            var records = (int)counts[section];
            var size = GoldWorldBankLayout.GetElementSize(0x1B3, section);
            var chunk = new byte[records * size];
            Array.Fill(chunk, ++fill);
            bytes.AddRange(chunk);
        }
        return bytes.ToArray();
    }

    private static byte[] Header(ushort opcode)
    {
        var length = GoldWorldBankLayout.HeaderSize + (opcode == 0x1B1 ? GoldWorldBankGlobals.Size : 0);
        return new byte[length];
    }

    private static RawMessage NextBank(RawClient client)
    {
        RawMessage m;
        do m = client.TryReceive(3000) ?? throw new AssertFailedException("bank missing"); while (m.Opcode is < 0x1B1 or > 0x1B4);
        return m;
    }

    [TestMethod]
    public void Bank3IsServedInServerForm()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        var upload = Bank3Upload(out var words);
        Assert.AreEqual(3, GoldWorldBankSnapshot.FromPayload(0x1B3, upload).Sections[1].Count);
        foreach (ushort op in new ushort[] { 0x1B1, 0x1B2, 0x1B3, 0x1B4 }) host.Send(op, op == 0x1B3 ? upload : Header(op));
        host.Send(0x018, [1, 0, 0, 0]);
        host.WaitFor(0x018);
        using var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        joiner.Send(0x1B5, default);

        var seen = new List<ushort>();
        for (var i = 0; i < 4; i++)
        {
            var m = NextBank(joiner);
            seen.Add(m.Opcode);
            Assert.AreEqual((byte)0, m.Sender);
            var snapshot = GoldWorldBankSnapshot.FromServerPayload(m.Opcode, m.Payload);   // what the remake decodes for Sender == 0
            if (m.Opcode != 0x1B3) { CollectionAssert.AreEqual(m.Opcode == 0x1B1 ? Header(0x1B1) : Header(m.Opcode), m.Payload); continue; }

            Assert.AreEqual(upload.Length - 3 * (UploadRecord - 4), m.Payload.Length);
            var section1 = snapshot.Sections[1];
            Assert.AreEqual(3, section1.Count);
            for (var r = 0; r < 3; r++) Assert.AreEqual(words[r], BinaryPrimitives.ReadUInt32LittleEndian(section1.Records[r].GetElement(0)));
            var keyed = snapshot.Sections[0];
            Assert.AreEqual(2, keyed.Count);
            Assert.AreEqual(0x11u, keyed.Records[0].Key);
            Assert.AreEqual(2, keyed.Records[0].Count);
            // Sections 2..6 are unchanged and the stream is consumed exactly.
            for (var s = 2; s <= 6; s++) Assert.AreEqual((int)BinaryPrimitives.ReadUInt32LittleEndian(upload.AsSpan(s * 4)), snapshot.Sections[s].Count);
            Assert.AreEqual(0, snapshot.RemainingData.Length);
        }
        CollectionAssert.AreEqual(new ushort[] { 0x1B2, 0x1B4, 0x1B3, 0x1B1 }, seen);
    }

    [TestMethod]
    public void MalformedBank3IsReplayedUnchanged()
    {
        using var server = new Server();
        using var host = RawClient.Admit(server.Port, "Host", 1);
        var bad = new byte[GoldWorldBankLayout.HeaderSize + 20];
        BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(4), 5);   // claims five 292-byte records
        host.Send(0x1B3, bad);
        host.Send(0x018, [1, 0, 0, 0]);
        host.WaitFor(0x018);
        using var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        joiner.Send(0x1B5, default);
        CollectionAssert.AreEqual(bad, NextBank(joiner).Payload);
    }
}
