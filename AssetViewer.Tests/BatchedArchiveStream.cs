using Sacred.Assets.Utils;

namespace AssetViewer.Tests;

/// <summary>Exercises archive scatter reads against installed data, rejecting synchronous I/O.</summary>
internal sealed class BatchedArchiveStream(Stream inner) : Stream, IBatchedReadStream
{
    public int Batches { get; private set; }
    public int Ranges { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => inner.ReadAsync(buffer, token);
    public async ValueTask ReadRangesAsync(ReadOnlyMemory<StreamReadRange> ranges, Memory<byte> destination, CancellationToken token = default)
    {
        Batches++; Ranges += ranges.Length;
        var position = inner.Position;
        var cursor = 0;
        foreach (var range in ranges.ToArray())
        {
            inner.Position = range.Offset;
            await inner.ReadExactlyAsync(destination.Slice(cursor, range.Length), token);
            cursor += range.Length;
        }
        inner.Position = position;
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Blocking browser read.");
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
