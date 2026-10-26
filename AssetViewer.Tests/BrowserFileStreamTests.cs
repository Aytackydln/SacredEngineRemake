using AssetViewer.Browser.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Assets.Utils;

namespace AssetViewer.Tests;

[TestClass]
public sealed class BrowserFileStreamTests
{
    [TestMethod]
    public async Task SparseHeaderBatchesKeepRequestOrderLongOffsetsAndSeekPosition()
    {
        BrowserInterop.Batches.Clear(); BrowserInterop.Reads.Clear();
        using var stream = new BrowserFileStream(7, 5L << 30) { Position = 73 };
        var ranges = Enumerable.Range(0, 600).Select(index =>
            new StreamReadRange((4L << 30) + (599 - index) * 8192 + 11, 80)).ToArray();
        var bytes = await StreamRangeReader.ReadAsync(stream, ranges);
        Assert.AreEqual(3, BrowserInterop.Batches.Count);
        Assert.AreEqual(0, BrowserInterop.Reads.Count, "Header batches must bypass serial reads and read-ahead.");
        Assert.AreEqual(73L, stream.Position, "Scatter reads must not alter the payload seek position.");
        for (var index = 0; index < ranges.Length; index++)
            for (var offset = 0; offset < ranges[index].Length; offset++)
                Assert.AreEqual((byte)(ranges[index].Offset + offset), bytes[index * 80 + offset]);
        Assert.IsTrue(BrowserInterop.Batches.All(batch => batch.Length <= StreamRangeReader.MaxBatchRanges * 2));
    }

    [TestMethod]
    public async Task BatchesValidateBoundsDestinationAndCancellationBeforeIo()
    {
        BrowserInterop.Batches.Clear();
        using var stream = new BrowserFileStream(1, 1000);
        await Assert.ThrowsExceptionAsync<EndOfStreamException>(async () =>
            await stream.ReadRangesAsync(new[] { new StreamReadRange(990, 11) }, new byte[11]));
        await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
            await stream.ReadRangesAsync(new[] { new StreamReadRange(1, 10) }, new byte[9]));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
            await stream.ReadRangesAsync(new[] { new StreamReadRange(1, 10) }, new byte[10], new CancellationToken(true)));
        Assert.AreEqual(0, BrowserInterop.Batches.Count);
        stream.Dispose();
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(async () =>
            await stream.ReadRangesAsync(new[] { new StreamReadRange(1, 10) }, new byte[10]));
    }

    [TestMethod]
    public async Task BatchSizeIsBoundedByBytesAsWellAsRangeCount()
    {
        BrowserInterop.Batches.Clear();
        using var stream = new BrowserFileStream(1, 1 << 20);
        var ranges = Enumerable.Range(0, 12).Select(index => new StreamReadRange(index * 8192, 8192)).ToArray();
        var bytes = await StreamRangeReader.ReadAsync(stream, ranges);
        Assert.AreEqual(98304, bytes.Length);
        Assert.AreEqual(2, BrowserInterop.Batches.Count);
        Assert.IsTrue(BrowserInterop.Batches.All(batch => batch.Where((_, index) => index % 2 == 1).Sum() <= StreamRangeReader.MaxBatchBytes));
    }

    [TestMethod]
    public async Task MultiGigabyteArchiveUsesBoundedRangesAndExactLongOffsets()
    {
        const long length = 5L * 1024 * 1024 * 1024 + 37;
        BrowserInterop.Reads.Clear();
        using var stream = new BrowserFileStream(7, length);
        stream.Position = 4L * 1024 * 1024 * 1024 + 11;
        var first = new byte[64]; await stream.ReadExactlyAsync(first);
        CollectionAssert.AreEqual(Enumerable.Range(0, 64).Select(i => (byte)(stream.Position - 64 + i)).ToArray(), first);
        var second = new byte[64]; await stream.ReadExactlyAsync(second);
        Assert.AreEqual(1, BrowserInterop.Reads.Count, "Adjacent probes should reuse bounded read-ahead.");
        Assert.AreEqual(1024, BrowserInterop.Reads[0].Count);
        Assert.AreEqual(4L * 1024 * 1024 * 1024 + 11, BrowserInterop.Reads[0].Offset);
        stream.Seek(-10, SeekOrigin.End);
        var tail = new byte[100]; Assert.AreEqual(10, await stream.ReadAsync(tail));
        Assert.AreEqual(0, await stream.ReadAsync(tail));
        Assert.AreEqual(10, BrowserInterop.Reads[^1].Count);
        Assert.IsTrue(BrowserInterop.Reads.Sum(read => read.Count) < 2048);
    }

    [TestMethod]
    public async Task LargePayloadTransfersDirectlyIntoDestination()
    {
        BrowserInterop.Reads.Clear();
        using var stream = new BrowserFileStream(1, 1L << 32);
        var bytes = new byte[128 * 1024];
        await stream.ReadExactlyAsync(bytes);
        Assert.AreEqual(1, BrowserInterop.Reads.Count);
        Assert.IsTrue(BrowserInterop.Reads[0].Direct, "Payload reads must avoid a second managed payload array.");
        Assert.AreEqual(bytes.Length, BrowserInterop.Reads[0].Count);
        Assert.AreEqual((byte)255, bytes[255]);
    }

    [TestMethod]
    public async Task CancellationAndDisposalDoNotReadOrAdvanceTheStream()
    {
        BrowserInterop.Reads.Clear();
        using var stream = new BrowserFileStream(1, 1000);
        var cancellation = new CancellationToken(canceled: true);
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () => _ = await stream.ReadAsync(new byte[1], cancellation));
        Assert.AreEqual(0L, stream.Position); Assert.AreEqual(0, BrowserInterop.Reads.Count);
        Assert.ThrowsException<NotSupportedException>(() => stream.ReadByte());
        stream.Dispose();
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(async () => _ = await stream.ReadAsync(new byte[1]));
    }
}
