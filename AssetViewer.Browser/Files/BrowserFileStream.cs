using Sacred.Assets.Utils;

namespace AssetViewer.Browser.Files;

/// <summary>Seekable, read-only Blob ranges. No archive is mounted or copied into Wasm.
/// A 1 KiB read-ahead buffer amortizes adjacent metadata reads; payloads bypass it.</summary>
internal sealed class BrowserFileStream(int fileId, long length) : Stream, IBatchedReadStream
{
    private const int ReadAhead = 1024;
    private byte[] _cache = [];
    private long _cacheOffset;
    private long _position;
    private bool _disposed;
    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position
    {
        get => _position;
        set { ObjectDisposedException.ThrowIf(_disposed, this); ArgumentOutOfRangeException.ThrowIfNegative(value); _position = value; }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var count = (int)Math.Min(buffer.Length, Math.Max(0, length - _position));
        if (count == 0) return 0;
        if (_position < _cacheOffset || _position + count > _cacheOffset + _cache.Length)
        {
            var offset = _position;
            if (count >= ReadAhead)
            {
                var read = await BrowserInterop.ReadRangeIntoAsync(fileId, offset, buffer[..count]);
                cancellationToken.ThrowIfCancellationRequested();
                if (read != count) throw new EndOfStreamException("The browser returned an incomplete file range.");
                _position += count;
                return count;
            }
            var request = (int)Math.Min(Math.Max(count, ReadAhead), length - offset);
            var bytes = await BrowserInterop.ReadRangeAsync(fileId, offset, request);
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length != request) throw new EndOfStreamException("The browser returned an incomplete file range.");
            _cacheOffset = offset;
            _cache = bytes;
        }
        _cache.AsMemory(checked((int)(_position - _cacheOffset)), count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public async ValueTask ReadRangesAsync(ReadOnlyMemory<StreamReadRange> ranges, Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (ranges.Length > StreamRangeReader.MaxBatchRanges || destination.Length > StreamRangeReader.MaxBatchBytes)
            throw new ArgumentException("The metadata batch exceeds its bounded size.");
        var requests = new double[ranges.Length * 2];
        var size = 0;
        for (var index = 0; index < ranges.Length; index++)
        {
            var range = ranges.Span[index];
            if (range.Offset < 0 || range.Length < 0 || range.Offset > length - range.Length)
                throw new EndOfStreamException("An indexed range exceeds the browser file.");
            requests[index * 2] = range.Offset;
            requests[index * 2 + 1] = range.Length;
            size = checked(size + range.Length);
        }
        if (size != destination.Length) throw new ArgumentException("The destination must match the concatenated ranges.");
        if (size == 0) return;
        await BrowserInterop.ReadRangesIntoAsync(fileId, requests, destination);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var count = (int)Math.Min(buffer.Length, Math.Max(0, length - _position));
        if (count == 0) return 0;
        if (_position < _cacheOffset || _position + count > _cacheOffset + _cache.Length)
            throw new NotSupportedException("Browser files require asynchronous range reads.");
        _cache.AsSpan(checked((int)(_position - _cacheOffset)), count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = checked(origin switch
    {
        SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset,
        SeekOrigin.End => Length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin))
    });
    protected override void Dispose(bool disposing) { _disposed = true; _cache = []; base.Dispose(disposing); }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
