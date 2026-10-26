namespace AssetViewer.Tests;

/// <summary>Fails if an indexer falls back to blocking browser I/O.</summary>
internal sealed class AsyncOnlyStream(Stream inner) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous browser read.");
    public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Synchronous browser read.");
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
