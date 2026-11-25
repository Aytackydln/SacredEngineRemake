using System.IO.Compression;
using Sacred.GameServer.Wire;
using Sacred.Network.Protocol;

namespace Sacred.GameServer.Tests;

[TestClass]
public class WireTests
{
    private static byte[] Sample(int length, int seed)
    {
        var random = new Random(seed);
        var data = new byte[length];
        for (var i = 0; i < length; i++) data[i] = (byte)(i % 7 == 0 ? random.Next(256) : (i / 3) % 11);
        return data;
    }

    [TestMethod]
    public void DeflaterOutputIsReadByRealZlib()
    {
        var deflater = new ZlibDeflater();
        foreach (var length in new[] { 1, 80, 1000, 70_000, 200_000 })
        {
            var data = Sample(length, length);
            var buffer = new byte[length * 2 + 64];
            var n = deflater.Compress(data, buffer);
            Assert.IsTrue(n > 0);
            using var zlib = new ZLibStream(new MemoryStream(buffer, 0, n), CompressionMode.Decompress);
            var result = new byte[length];
            zlib.ReadExactly(result);
            CollectionAssert.AreEqual(data, result);
        }
    }

    [TestMethod]
    public void IncompressibleDataFallsBackToStoredBlocks()
    {
        var data = new byte[100_000];
        new Random(7).NextBytes(data);
        var buffer = new byte[data.Length + 100];
        var n = new ZlibDeflater().Compress(data, buffer);
        Assert.IsTrue(n > 0);
        using var zlib = new ZLibStream(new MemoryStream(buffer, 0, n), CompressionMode.Decompress);
        var result = new byte[data.Length];
        zlib.ReadExactly(result);
        CollectionAssert.AreEqual(data, result);
    }

    [TestMethod]
    public void InflaterReadsRealZlibOutput()
    {
        var inflater = new ZlibInflater();
        foreach (var level in new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize })
        {
            var data = Sample(150_000, 3);
            var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, level, leaveOpen: true)) zlib.Write(data);
            var output = new byte[data.Length];
            Assert.IsTrue(inflater.TryInflate(compressed.ToArray(), output, out var written), level.ToString());
            Assert.AreEqual(data.Length, written);
            CollectionAssert.AreEqual(data, output);
        }
    }

    [TestMethod]
    public void EnvelopeInteroperatesWithSacredNetworkCodec()
    {
        var deflater = new ZlibDeflater();
        var inflater = new ZlibInflater();
        foreach (var length in new[] { 16, 76, 80, 500, 30_000 })
        {
            var batch = new byte[length];
            var n = GoldWire.WriteMessage(batch, 0x005, 0, 3, 9, Sample(length - 16, length));
            Assert.AreEqual(length, n);
            var envelope = new byte[length * 2 + 64];
            var size = GoldWire.EncodeEnvelope(batch, envelope, deflater);
            Assert.IsTrue(size > 0);
            // ours -> repo codec
            CollectionAssert.AreEqual(batch, GoldEnvelopeCodec.Decode(envelope.AsSpan(0, size)));
            var decoded = GoldMessageCodec.DecodeBatch(batch);
            Assert.AreEqual((ushort)0x005, decoded[0].Opcode);
            Assert.AreEqual((byte)3, decoded[0].Origin);
            // repo codec -> ours
            var theirs = GoldEnvelopeCodec.Encode(batch);
            var scratch = new byte[length + 16];
            Assert.IsTrue(GoldWire.TryDecodeEnvelope(theirs, scratch, inflater, out var decodedLength));
            CollectionAssert.AreEqual(batch, scratch.AsSpan(0, decodedLength).ToArray());
            // our message bytes equal the repo's encoding
            var repoMessage = GoldMessageCodec.Encode(new GoldMessage(0x005, 0, 3, 9, Sample(length - 16, length)));
            CollectionAssert.AreEqual(repoMessage, batch);
        }
    }

    [TestMethod]
    public void MessageChecksumMatchesRepoImplementation()
    {
        var payload = Sample(333, 1);
        var span = new byte[16 + payload.Length];
        GoldWire.WriteMessage(span, 0x123, 5, 6, 77, payload);
        var reader = new GoldBatchReader(span);
        Assert.IsTrue(reader.TryRead(out var message));
        Assert.AreEqual((ushort)0x123, message.Opcode);
        Assert.AreEqual((ushort)77, message.Sequence);
        Assert.IsFalse(reader.TryRead(out _));
        Assert.IsFalse(reader.Failed);
        span[40] ^= 1;
        var corrupted = new GoldBatchReader(span);
        Assert.IsFalse(corrupted.TryRead(out _));
        Assert.IsTrue(corrupted.Failed);
    }
}
